# Deployment Strategy

InvoiceManager uses Terraform for infrastructure provisioning and GitHub Actions for continuous deployment. The deployment pipeline supports both `test` and `production` environments with manual approval required for production deployment.

## Environments

Infrastructure names are environment-aware. Non-production resources include the
environment suffix, for example `-test`. Production resources use the base name
without a `production` or `prod` suffix. This keeps production names clean while
making non-production resources visually distinct.

### Test Environment

The test environment is for validating deployment changes, testing integrations, and staging new features before production.

- **Purpose**: Integration testing, validation, and pre-production verification.
- **Automatic Deployment**: Deployed automatically on every successful build to the `main` branch.
- **Data**: Test data only; credentials are non-production.
- **Azure Resources**: Segregated resource group with naming convention
  `invoicemanager-test-*` for application resources and
  `rg-invoicemanager-tfstate-test` for Terraform state.

### Production Environment

The production environment runs the live invoice management service.

- **Purpose**: Live invoice retrieval, processing, and FreeAgent integration.
- **Manual Approval**: Requires explicit approval after test environment deployment succeeds.
- **Data**: Production data; credentials are production secrets from Azure Key Vault.
- **Azure Resources**: Segregated resource group with unsuffixed production
  names, for example `invoicemanager-*` for application resources and
  `rg-invoicemanager-tfstate` for Terraform state.

## Deployment Pipeline

### Trigger Events

Deployments are triggered by:

1. **Push to main branch**: Runs build, test, and deploys to test environment automatically.
2. **Manual approval**: After test environment succeeds, manual approval deploys to production.

### Deployment Flow

```
Code Push to main
       ↓
Build & Unit Tests
       ↓
Deploy to Test Environment
       ↓
Integration Tests (Test)
       ↓
Manual Approval (GitHub Environment)
       ↓
Deploy to Production Environment
       ↓
Complete
```

## Infrastructure as Code (Terraform)

Terraform manages all Azure infrastructure including:

- **Azure Functions**: Flex Consumption plan (`azurerm_function_app_flex_consumption`)
  running the `dotnet-isolated` InvoiceManager service. Flex supports
  dotnet-isolated 8.0/9.0/10.0 but not net11.0, so the deployed artifact is net10.0
  (`functions_runtime_version = "10.0"`). The libraries the Functions app depends on
  multi-target `net10.0;net11.0`, but the **Functions project itself is single-target**
  (net11.0 for local Aspire runs, net10.0 only when published with
  `dotnet publish -p:PublishForAzure=true`) — Aspire launches it with `dotnet run`,
  which rejects a multi-targeted project. The `union` support types absent from
  net10.0 are polyfilled for that target
  (`src/InvoiceManager.Core/Polyfills/UnionSupport.cs`).
- **Admin website**: Azure Container Apps (scale-to-zero) pulling a public
  ghcr.io image; ingress exposed on port 8080.
- **Azure Cosmos DB**: Serverless database for invoice configuration and state.
- **Azure Key Vault**: Secrets storage for credentials and API keys.
- **Managed identities**: One user-assigned identity per app, each granted the
  Key Vault and Cosmos DB roles it needs (see below).
- **Application Insights + Log Analytics**: Telemetry and monitoring, shared by
  both apps.
- **Storage Accounts**: Function app host/deployment storage (identity-based).
- **Microsoft Identity Setup**: Entra app registration, service principal, and
  redirect URIs (local admin plus the deployed Container Apps callback) used for
  delegated authorization capture.

### Terraform Structure

```
infra/terraform/
├── README.md
├── locals.tf
├── main.tf
├── outputs.tf
├── production.tfvars
├── test.tfvars
├── variables.tf
└── versions.tf
```

Each environment has its own `.tfvars` file and remote backend settings. The
backend configuration is supplied by `scripts/Deploy-Infra.ps1` during
`terraform init`, because the Azure Storage backend must exist before Terraform
can use it.

The initial Terraform configuration creates the Microsoft identity foundation:

- An Entra app registration.
- The tenant-local service principal / Enterprise Application.
- Required delegated API permissions for Azure Resource Manager
  `user_impersonation` and Microsoft Graph `User.Read`.
- An environment Key Vault used by the admin website to store its client secret
  and captured Microsoft authorization token-cache material.
- Azure RBAC assignments for Key Vault data-plane access. Terraform grants the
  deployment identity `Key Vault Secrets Officer` on the environment vault so it
  can write Terraform-managed secrets during apply.

Terraform also creates the runtime hosting and its access grants:

- **Compute**: the Flex Consumption Functions app and the admin website
  Container App, each with its own user-assigned managed identity.
- **Key Vault**: both identities receive `Key Vault Secrets Officer` — not the
  read-only `Secrets User` — because each app reads *and writes*
  `MicrosoftAuthorization--MsalTokenCache` (MSAL persists the refreshed cache
  back to the vault).
- **Cosmos DB** (data plane): the Functions identity gets the built-in
  **Data Contributor** role (reads/writes invoice records); the admin website
  identity gets **Data Reader** (it only reads the account for its health check).
- **Storage**: the Functions identity gets `Storage Blob Data Owner` +
  `Storage Queue Data Contributor` for the identity-based host storage
  connection.
- **App configuration**: Terraform sets each app's settings (Cosmos endpoint +
  database, the `MicrosoftAuthorization` tenant/client/vault values, App Insights
  connection string, `Functions:BaseUrl` for the admin site, and `AZURE_CLIENT_ID`
  so `DefaultAzureCredential` selects the app's user-assigned identity) —
  mirroring the values Aspire/user-secrets supply locally. `ClientSecret` and the
  MSAL token cache are never set here; they load from Key Vault at runtime.

The admin website OIDC callback (`https://adminweb.<env-domain>/signin-oidc`) is
derived from the Container Apps environment default domain and appended to the
Entra app registration's redirect URIs. It is computed from the *environment*
(not the container app resource) to avoid a dependency cycle with the app
registration that supplies the app's `ClientId`.

### Environment-Specific Configuration

Environment-specific values are managed through:

1. **terraform.tfvars**: Environment-specific variable overrides (committed to source control).
2. **Backend State**: Separate Azure Storage accounts and resource groups for
   test and production Terraform state.
3. **Azure Key Vault**: Production secrets loaded by Azure Functions at runtime.

Example `terraform.tfvars` differences:

**infra/terraform/test.tfvars**:
```hcl
environment = "test"
```

**infra/terraform/production.tfvars**:
```hcl
environment = "production"
```

### Local Infrastructure Deployment

Use the PowerShell bootstrap script from the repository root:

```powershell
./scripts/Deploy-Infra.ps1 -Environment test
./scripts/Deploy-Infra.ps1 -Environment production
```

Parameter syntax:

```text
./scripts/Deploy-Infra.ps1 -Environment <test|production> [-Location <location>] [-SubscriptionId <subscription-id>] [-ApplicationName <name>] [-PlanOnly] [-AutoApprove] [-ClearDatabase] [-SkipGitHubVars] [-PublishAdminWebImage]
```

`-PublishAdminWebImage` electively runs the admin website image build (the same
`src/InvoiceManager.AdminWeb/Dockerfile` CI uses, via
`scripts/Publish-AdminWebImage.ps1`), pushes it to the ghcr package, and pins the
Terraform plan to that image (`-var=adminweb_image=...`). Use it so the first
apply creates the Container App against a genuine image on port 8080 rather than
the stock bootstrap reference. Requires Docker and a prior `docker login ghcr.io`;
the ghcr package must be made public once for anonymous pulls.

The script:

1. Checks that Terraform is installed.
2. Checks that Azure CLI is installed.
3. Prompts for Azure CLI login when needed.
4. Creates the environment-specific Terraform state resource group, storage
   account, and blob container if missing.
5. Runs `terraform init`.
6. Runs `terraform plan`.
7. Runs `terraform apply` when the plan has changes, unless `-PlanOnly` is
   supplied.
8. Seeds the invoice configurations, passing `--environment <env>` (and
   `--clear-database` when `-ClearDatabase` is supplied — see below).
9. Publishes the deployment-target GitHub Environment variables unless
   `-SkipGitHubVars` is supplied.

### Seeding behavior (`--environment`, `--clear-database`)

The seeder receives `--environment <env>` so it can make the data
environment-aware, and optionally `--clear-database`:

- **Test folder isolation**: when the environment is `test`, every configuration's
  OneDrive destination is nested under a single root `Test` folder (inserted after
  `root:/`, mirroring the production tree inside it) so test downloads never
  collide with production files.
- **`-ClearDatabase`**: deletes all items from the Cosmos containers (data-plane
  deletes only) before seeding, for a clean re-seed. It is **refused against
  `production`** unless the seeder is also passed `--force`.

The script does not install Terraform or Azure CLI automatically. If either tool
is missing, it prints installation instructions for the current operator to
follow.

Use `-AutoApprove` only when the script should skip its confirmation prompt
before applying the saved plan:

```powershell
./scripts/Deploy-Infra.ps1 -Environment test -AutoApprove
```

The script relies on normal user consent for the currently required delegated
permissions. Permission declarations live in Terraform; interactive application
authentication will be handled by the future admin site.

### Local Admin Authorization Website

The local admin website runs from `src/InvoiceManager.AdminWeb` and uses the
Terraform-managed Entra app registration. It captures Microsoft delegated
authorization for Azure Resource Manager and Microsoft Graph, then persists the
serialized MSAL token cache in the environment Key Vault as
`MicrosoftAuthorization--MsalTokenCache`.

The admin website runs both locally (from `src/InvoiceManager.AdminWeb`) and
deployed to Azure Container Apps. Terraform registers two callback URIs on each
app registration: the local `https://localhost:5001/signin-oidc` (from
`redirect_uris` in the `.tfvars`) and the deployed
`https://adminweb.<env-domain>/signin-oidc` (derived automatically from the
Container Apps environment). Behind the Container Apps ingress the app honors
`X-Forwarded-Proto` (forwarded-headers middleware) so the callback is built as
`https://`.

The deployed image is a **public ghcr.io package** pulled anonymously, so no
registry credential is stored anywhere and there is nothing to rotate. CI builds
and pushes the image (see the deploy workflow below).

Terraform creates the admin website application password for every environment
and stores it in Key Vault as `MicrosoftAuthorization--ClientSecret`. The secret
value is not emitted as a Terraform output and should not be stored in local
user secrets. Terraform state must still be treated as sensitive because it
contains generated application password values.

For the `test` environment, `scripts/Deploy-Infra.ps1` configures local user
secrets after a successful apply, or when Terraform reports no changes. Local
user secrets contain only non-secret settings:

```bash
dotnet user-secrets set "MicrosoftAuthorization:TenantId" "<tenant-id>" --project src/InvoiceManager.AdminWeb
dotnet user-secrets set "MicrosoftAuthorization:ClientId" "<application-client-id>" --project src/InvoiceManager.AdminWeb
dotnet user-secrets set "MicrosoftAuthorization:KeyVaultUri" "https://<key-vault-name>.vault.azure.net/" --project src/InvoiceManager.AdminWeb
```

When the admin website starts, it uses `MicrosoftAuthorization:KeyVaultUri` and
`DefaultAzureCredential` to load `MicrosoftAuthorization:ClientSecret` from Key
Vault before binding and validating the final authentication configuration.
Local developers must be signed in to Azure with access to the test Key Vault.
Key Vault access is controlled through Azure RBAC rather than legacy vault
access policies.

The admin website does not configure invoices, manually reconcile OneDrive
files, or manage FreeAgent authorization in this first implementation.

## GitHub Actions Workflow

Two workflows orchestrate the pipeline: `ci.yml` (build/test/terraform-validate)
and `deploy.yml` (deployment). **Infrastructure is provisioned out-of-band by
`scripts/Deploy-Infra.ps1`** (locally or from an operator machine); the deploy
workflow only ships application code to infrastructure that already exists.

### CI Workflow (`ci.yml`)

Triggered on: push to `main` and pull requests.

1. Checkout code.
2. Setup .NET 11 preview SDK, as pinned by `global.json`.
3. Restore, format-check, build, vulnerable-package check.
4. Run unit tests and non-Docker checks (`Category!=Integration`).
5. Terraform `fmt`/`validate`/`tflint`.

### Deploy Workflow (`deploy.yml`)

Triggered on: successful completion of the CI workflow on `main` (via
`workflow_run`), so deployment always follows a green build. Feature branches
and pull requests never deploy.

**How CI learns the deployment targets** — after a successful apply (or when
Terraform reports no changes), `Deploy-Infra.ps1` publishes the concrete target
names as **GitHub Environment variables** into the `test` / `production`
environments: `FUNCTIONS_APP_NAME`, `FUNCTIONS_DEFAULT_HOSTNAME`,
`ADMINWEB_CONTAINER_APP_NAME`, `ADMINWEB_FQDN`, `AZURE_RESOURCE_GROUP`. (Use
`-SkipGitHubVars` to opt out; requires an authenticated `gh` CLI.)

**Jobs**:
1. **build-images**: build + push the admin website image to the public ghcr
   package (tagged with the commit SHA) using `GITHUB_TOKEN`; `dotnet publish`
   the Functions app and upload it as an artifact.
2. **deploy-test** (`environment: test`): if the Environment variables are set
   (infra exists), Azure OIDC login, deploy the Functions package, and
   `az containerapp update --image ...:<sha>`. **Before `Deploy-Infra.ps1` has
   ever run the variables are empty, so the job skips gracefully instead of
   failing.**
3. **deploy-production** (`environment: production`): same steps after test; the
   `production` GitHub Environment's required-reviewer rule is the manual
   approval gate.

The Container App's image is managed by CI via `az containerapp update`;
Terraform uses `ignore_changes` on the container image so it does not revert the
running tag to its bootstrap reference.

## Configuration & Secrets Management

### Configuration Hierarchy

Configuration is managed at multiple levels:

1. **Committed Configuration** (GitHub):
   - Build settings (project files, NuGet references).
   - Terraform variables (non-sensitive environment settings).
   - Deployment workflow definitions.

2. **Environment Variables** (GitHub Actions):
   - Azure subscription IDs.
   - Resource group names.
   - Terraform backend details.
   - Non-sensitive service configuration.

3. **Azure Key Vault** (Runtime):
   - API keys and authentication tokens.
   - Database connection strings.
   - OAuth credentials.
   - Any production secrets.

### Secrets Management

#### Local Development

Use `dotnet user-secrets` and `aspire` configuration:

```bash
# Set a local secret
dotnet user-secrets set "AzureOptions:TenantId" "your-tenant-id"

# Aspire loads these at runtime
```

Local secrets are stored in `%APPDATA%\Microsoft\UserSecrets\` and never committed.

The Aspire AppHost starts the Cosmos DB emulator, the Functions app, and the
admin website together. The Functions app is launched through Aspire's
first-class Azure Functions integration (`Aspire.Hosting.Azure.Functions`), so
no Azure Functions Core Tools (`func`) installation is required. Aspire
provisions the Functions host storage automatically through the Azurite
emulator. A container runtime (Docker/Podman) must therefore be available for
local orchestration and for the full AppHost integration test. Dockerized
emulator tests are marked with `Category=Integration` and are run locally rather
than on hosted CI runners. Aspire injects the Cosmos connection string into both
application projects and injects the Functions base URL into the admin website.

#### GitHub Actions Secrets

Repository secrets are configured in GitHub Settings → Secrets and Variables:

- `AZURE_SUBSCRIPTION_ID`: Azure subscription for deployment.
- `AZURE_TENANT_ID`: Azure AD tenant ID.
- `AZURE_CLIENT_ID`: Service principal app ID for CI/CD.
- `AZURE_CLIENT_SECRET`: Service principal secret for CI/CD.

These are accessed in workflows via `${{ secrets.AZURE_SUBSCRIPTION_ID }}`.

#### Azure Key Vault

Production secrets are stored in Azure Key Vault and accessed by Azure Functions using Managed Identity:

1. Each environment has its own Key Vault (`invoicemanager-test-kv`, `invoicemanager-kv`).
2. Azure Functions use Managed Identity to authenticate to Key Vault.
3. Key Vault data-plane access is granted through Azure RBAC roles.
4. Secrets are referenced in code using `SecretClient` from `Azure.Security.KeyVault.Secrets`.

Example secrets in Key Vault:

- `MicrosoftAuthorization--ClientSecret`
- `MicrosoftAuthorization--MsalTokenCache`
- `InvoiceIntegrations--AzureTenantId`
- `InvoiceIntegrations--AzureClientId`
- `InvoiceIntegrations--AzureClientSecret`
- `InvoiceIntegrations--OpenAiApiKey`
- `FreeAgent--ApiKey`
- `OneDrive--ClientId`

#### Cosmos DB Connection

Cosmos DB connection is configured via:

1. **Local Development**: Cosmos DB emulator connection string supplied by
   Aspire when running through AppHost, or by `local.settings.json` when running
   the Functions project directly.
2. **Test Environment**: Connection endpoint and key stored in Key Vault.
3. **Production Environment**: Connection endpoint and key stored in Key Vault with stricter RBAC assignments.

### Environment-Specific Application Settings

Azure Functions `local.settings.json` (local only, never committed):

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "InvoiceIntegrations:Azure:TenantId": "test-tenant-id",
    "CosmosOptions:Endpoint": "https://localhost:8081"
  }
}
```

Azure Functions Application Settings (configured via Terraform and Azure Portal):

- Set per environment.
- Can reference Key Vault secrets using `@Microsoft.KeyVault(SecretUri=https://kv.vault.azure.net/secrets/name/version)`.
- Terraform configures these based on environment-specific variables.

### Terraform Variables Pattern

```hcl
# infra/terraform/variables.tf
variable "environment" {
  description = "Environment name (test or production)"
  type        = string
}

variable "redirect_uris" {
  description = "Allowed web redirect URIs for the future admin authentication site."
  type        = list(string)
  default     = []
}

# infra/terraform/test.tfvars
environment = "test"

# infra/terraform/production.tfvars
environment = "production"
```

## CI/CD Service Principal

A dedicated service principal is used for GitHub Actions deployments:

1. **Setup** (one-time):
   - Create a service principal in Azure AD.
   - Grant necessary RBAC roles (e.g., Owner or custom role for test/prod resource groups).
   - Store credentials in GitHub repository secrets.

2. **Permissions**:
   - Manage resources in test and production resource groups.
   - Read/write Terraform backend state.
   - Deploy Azure Functions.

3. **Security**:
   - Use `AZURE_CLIENT_SECRET` only in GitHub (never commit).
   - Rotate credentials regularly.
   - Consider using GitHub OIDC federation (federated credentials) instead of secrets for improved security.

## Deployment Checklist

Before deploying to production:

- [ ] All tests pass in test environment.
- [ ] Integration tests validate invoices can be retrieved.
- [ ] OneDrive integration works in test environment.
- [ ] FreeAgent integration works in test environment.
- [ ] Monitoring and alerting are configured.
- [ ] Key Vault secrets are set for production.
- [ ] Application Insights is receiving telemetry from test environment.
- [ ] Terraform plan shows no unexpected resource changes.
- [ ] Code review approved.

## Rollback Strategy

### Functions Rollback

If a Functions deployment causes issues:

1. Terraform tracks the previous deployment state.
2. Revert the code and push to `main`.
3. Trigger deployment pipeline manually or wait for automatic rebuild.
4. Terraform apply will redeploy the previous version.

### Infrastructure Rollback

If infrastructure changes cause issues:

1. Review the Terraform plan before applying.
2. If needed, revert the Terraform code changes.
3. Run `terraform apply` with the reverted configuration.
4. Manual intervention may be required for data-bearing resources (e.g., Cosmos DB).

## Monitoring and Alerts

Post-deployment:

1. Application Insights monitors function execution and exceptions.
2. Azure Monitor alerts on high error rates or performance degradation.
3. Cosmos DB metrics track request units and throttling.
4. Key Vault access logs track secret retrieval.

## Documentation and References

- [Azure Functions Documentation](https://learn.microsoft.com/en-us/azure/azure-functions/)
- [Terraform Azure Provider](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs)
- [GitHub Actions](https://docs.github.com/en/actions)
- [Azure Key Vault Best Practices](https://learn.microsoft.com/en-us/azure/key-vault/general/best-practices)
