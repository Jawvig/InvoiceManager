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

- **Azure Functions**: Consumption plan for the InvoiceManager service.
- **Azure Cosmos DB**: Serverless database for invoice configuration and state.
- **Azure Key Vault**: Secrets storage for credentials and API keys.
- **Application Insights**: Telemetry and monitoring.
- **Storage Accounts**: For function app staging and application storage.

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

The script:

1. Checks that Terraform is installed.
2. Checks that Azure CLI is installed.
3. Prompts for Azure CLI login when needed.
4. Creates the environment-specific Terraform state resource group, storage
   account, and blob container if missing.
5. Runs `terraform init`.
6. Runs `terraform plan`.
7. Runs `terraform apply`, unless `-PlanOnly` is supplied.

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

## GitHub Actions Workflow

The deployment pipeline is orchestrated by GitHub Actions workflows.

### Build Workflow

Triggered on: Push to `main` branch or pull request.

**Steps**:
1. Checkout code.
2. Setup .NET 10.
3. Restore NuGet dependencies.
4. Build solution.
5. Run unit tests.
6. Publish artifacts.

### Test Environment Deployment

Triggered on: Successful build on `main` branch.

**Steps**:
1. Download build artifacts.
2. Terraform plan (test environment).
3. Terraform apply (test environment).
4. Deploy Azure Functions to test.
5. Run integration tests against test environment.
6. Publish test results.

### Production Approval & Deployment

Triggered on: Manual approval via GitHub Environments.

**Approval Gate**:
- Requires approval from authorized GitHub users.
- Can be configured in repository settings under "Environments".

**Steps**:
1. Download build artifacts.
2. Terraform plan (production environment).
3. Terraform apply (production environment).
4. Deploy Azure Functions to production.
5. Run smoke tests against production environment.

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

#### GitHub Actions Secrets

Repository secrets are configured in GitHub Settings → Secrets and Variables:

- `AZURE_SUBSCRIPTION_ID`: Azure subscription for deployment.
- `AZURE_TENANT_ID`: Azure AD tenant ID.
- `AZURE_CLIENT_ID`: Service principal app ID for CI/CD.
- `AZURE_CLIENT_SECRET`: Service principal secret for CI/CD.

These are accessed in workflows via `${{ secrets.AZURE_SUBSCRIPTION_ID }}`.

#### Azure Key Vault

Production secrets are stored in Azure Key Vault and accessed by Azure Functions using Managed Identity:

1. Each environment has its own Key Vault (`invoicemanager-test-kv`, `invoicemanager-prod-kv`).
2. Azure Functions use Managed Identity to authenticate to Key Vault.
3. Secrets are referenced in code using `SecretClient` from `Azure.Security.KeyVault.Secrets`.

Example secrets in Key Vault:

- `InvoiceIntegrations--AzureTenantId`
- `InvoiceIntegrations--AzureClientId`
- `InvoiceIntegrations--AzureClientSecret`
- `InvoiceIntegrations--OpenAiApiKey`
- `FreeAgent--ApiKey`
- `OneDrive--ClientId`

#### Cosmos DB Connection

Cosmos DB connection is configured via:

1. **Local Development**: Cosmos DB emulator connection string in `local.settings.json` (not committed).
2. **Test Environment**: Connection endpoint and key stored in Key Vault.
3. **Production Environment**: Connection endpoint and key stored in Key Vault with stricter access policies.

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
# variables.tf - Shared variables
variable "environment" {
  description = "Environment name (test or production)"
  type        = string
}

variable "cosmos_throughput" {
  description = "Cosmos DB provisioned throughput"
  type        = number
}

# test/terraform.tfvars
environment      = "test"
cosmos_throughput = 400

# production/terraform.tfvars
environment      = "production"
cosmos_throughput = 4000
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
