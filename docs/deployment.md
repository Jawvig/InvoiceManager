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
  ghcr.io image; ingress exposed on port 8080. Its canonical public names are
  `invoicemanager-test.omnics.tech` for test and `invoicemanager.omnics.tech`
  for production, with Azure-managed TLS certificates.
- **Namecheap DNS**: the environment's AdminWeb CNAME and Azure verification
  TXT record in the shared `omnics.tech` zone, managed in non-authoritative
  `MERGE` mode.
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
- **Document Intelligence**: An `azurerm_cognitive_account` (kind
  `FormRecognizer`) used by the `GraphEmail` invoice source to read
  invoice date/total out of PDF attachments via the prebuilt `invoice` model.
  RBAC-only (`local_auth_enabled = false`); the Functions managed identity
  holds `Cognitive Services User` on it, and its endpoint is passed to the
  Functions app as `DocumentIntelligence__Endpoint`.

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
  `user_impersonation` and Microsoft Graph `User.Read` and `Mail.Read` (the
  latter used by the `GraphEmail` invoice source to search the same
  delegated mailbox already used for OneDrive uploads).
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

The admin website's canonical OIDC callback is derived from the application base
name and the existing environment suffix rule. Terraform also retains callbacks
for the generated Container Apps hostname as a diagnostic and rollback path.
Both sets are computed without referencing the container app resource, avoiding
a dependency cycle with the app registration that supplies the app's `ClientId`.

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
./scripts/Deploy-Infra.ps1 -Environment <test|production> [-Location <location>] [-SubscriptionId <subscription-id>] [-ApplicationName <name>] [-PlanOnly] [-AutoApprove] [-ClearDatabase] [-SkipGitHubManagement] [-PublishAdminWebImage] [-PromptFreeAgentClientId] [-PromptFreeAgentClientSecret] [-PromptNamecheapApiKey]
```

`-PromptFreeAgentClientId` / `-PromptFreeAgentClientSecret` force the script to
re-prompt for that FreeAgent OAuth credential even when it is already present
in the target environment's Key Vault (see below) — separate flags because the
client secret rotates far more often than the client ID.

`-PromptNamecheapApiKey` securely prompts for a replacement Namecheap API key
and overwrites the repository-specific credential stored for the current
Windows user. Normal deployments reuse the stored key without prompting.

`-SkipGitHubManagement` runs a **GitHub-less apply**: it passes
`-var=manage_github=false` and skips every `gh` interaction (the tool check,
authentication/token, owner/repository/reviewer derivation, and the stale-variable
cleanup). Use it when an operator can provision Azure but cannot administer
GitHub; the Terraform-owned CI identity, deploy environment, secrets, and
variables are then **not** managed (so CI deployment must be wired up separately).

`-PublishAdminWebImage` electively runs the admin website image build (the same
`src/InvoiceManager.AdminWeb/Dockerfile` CI uses, via
`scripts/Publish-AdminWebImage.ps1`), pushes it to the ghcr package, and pins the
Terraform plan to that image (`-var=adminweb_image=...`). Use it so the first
apply creates the Container App against a genuine image on port 8080 rather than
the stock bootstrap reference. Requires Docker and a prior `docker login ghcr.io`;
the ghcr package must be made public once for anonymous pulls.

### AdminWeb custom domains and Namecheap credentials

Terraform derives the AdminWeb hostname from the single environment-neutral
`adminweb_hostname_base` value (`InvoiceManager` by default) and the same suffix
rule used by Azure resources:

| Environment | Canonical hostname | Namecheap records |
| --- | --- | --- |
| Test | `invoicemanager-test.omnics.tech` | CNAME `invoicemanager-test` and TXT `asuid.invoicemanager-test` |
| Production | `invoicemanager.omnics.tech` | CNAME `invoicemanager` and TXT `asuid.invoicemanager` |

The CNAME targets the generated Azure Container Apps hostname. The TXT value is
the Container App's custom-domain verification ID. The Namecheap resource uses
explicit `MERGE` mode, so other `omnics.tech` records remain unmanaged and must
not be proposed for deletion. Test and production use separate Terraform states
but update the same Namecheap zone; never run their infrastructure applies at
the same time.

Before the first deployment from a machine:

1. Enable API access in the Namecheap account.
2. Add the machine/operator's public IPv4 address to Namecheap's API whitelist.
3. Run `Deploy-Infra.ps1`. It prompts for `NAMECHEAP_USER_NAME`,
   `NAMECHEAP_API_USER`, and `NAMECHEAP_CLIENT_IP` when missing and offers to
   persist each as a current-user environment variable. Values saved at user
   scope are refreshed into the running process immediately.
4. Enter the API key at the secure prompt. The script stores it as the generic
   Windows credential `InvoiceManager/NamecheapApiKey`, with local-machine
   persistence in the current user's Credential Manager vault.

The API key is never persisted as an environment variable. The script reads it
through the native Windows Credential Manager API, places it in
`NAMECHEAP_API_KEY` only for the Terraform portion of the current process, and
restores or removes that process value in a `finally` block. Do not put any
Namecheap credentials in `.tfvars`, source control, Terraform state, saved
plans, outputs, or command-line arguments.

To rotate the key, run a deployment (or plan) with
`-PromptNamecheapApiKey`. To remove it, open Windows Credential Manager and
delete the generic credential named `InvoiceManager/NamecheapApiKey`, or run:

```powershell
cmdkey /delete:InvoiceManager/NamecheapApiKey
```

`cmdkey` is suitable for deletion only; the deployment script deliberately uses
the native credential APIs because `cmdkey` cannot retrieve a stored secret.
Remove a persisted non-secret setting, if needed, with:

```powershell
[Environment]::SetEnvironmentVariable("NAMECHEAP_USER_NAME", $null, "User")
[Environment]::SetEnvironmentVariable("NAMECHEAP_API_USER", $null, "User")
[Environment]::SetEnvironmentVariable("NAMECHEAP_CLIENT_IP", $null, "User")
```

Roll out the custom domain to test first:

1. Add
   `https://invoicemanager-test.omnics.tech/freeagent-authorization/callback`
   to the existing `Omnics InvoiceManager Sandbox` app.
2. Run a test plan and confirm the Namecheap resource says `MERGE` and contains
   only the test CNAME and verification TXT record. It must not propose deleting
   unrelated zone records.
3. Apply test. DNS propagation and Azure managed-certificate issuance are
   asynchronous; the custom-domain create timeout is 60 minutes.
4. Confirm the custom hostname resolves, HTTPS has a valid certificate, both
   Entra callback flows work, FreeAgent authorization completes, and the
   generated Azure hostname remains usable.
5. Run a later plan to confirm the stored API key is reused without prompting.

Production deployment is deferred until its Terraform backend and FreeAgent app
exist. When they do, configure the documented production FreeAgent callback
before applying production. To roll back, revert the custom-domain Terraform
change and apply the affected environment. Terraform removes only that
environment's `MERGE`-managed CNAME/TXT records and Azure binding; the generated
Container Apps hostname and its Entra callbacks remain available throughout.

The script:

1. Checks that Terraform, Azure CLI, and GitHub CLI (`gh`) are installed
   (`gh` is skipped under `-SkipGitHubManagement`).
2. Loads or prompts for the Namecheap non-secret settings and, immediately
   before Terraform runs, retrieves or securely captures the API key.
3. Prompts for Azure CLI login when needed.
4. Confirms `gh` is authenticated and sources `GITHUB_TOKEN` from
   `gh auth token` so the `github` Terraform provider can manage the deploy
   environment. It also derives `github_owner` / `github_owner_id` /
   `github_repository` / `github_repository_id` (from
   `gh api repos/{owner}/{repo}`) and `production_reviewer` (from
   `gh api user`) and passes them to Terraform as `-var`, so no account
   identity is hardcoded. The owner/repository ids feed the OIDC federated
   credential's immutable subject. Under
   `-SkipGitHubManagement` this whole step is skipped and Terraform runs with
   `-var=manage_github=false`.
5. Creates the environment-specific Terraform state resource group, storage
   account, and blob container if missing.
6. Runs `terraform init`.
7. Runs `terraform plan`.
8. On the first apply only, deletes any leftover deploy-target GitHub
   Environment variables that the retired publishing step created out-of-band
   (they would otherwise collide with the provider's create); skipped once
   Terraform owns them, and skipped entirely under `-SkipGitHubManagement`.
9. Runs `terraform apply` when the plan has changes, unless `-PlanOnly` is
   supplied. Terraform provisions the per-environment CI identity, its RBAC, and
   the GitHub deploy environment, secrets, and variables (see the workflow
   section below).
10. Removes the provider-injected empty-key Function App storage connection
   strings (see below).
11. Ensures FreeAgent client credentials exist in the target environment's Key
    Vault, prompting interactively for `FreeAgentAuthorization--ClientId`
    and/or `FreeAgentAuthorization--ClientSecret` only when a value is missing
    (or `-PromptFreeAgentClientId`/`-PromptFreeAgentClientSecret` forces
    re-entry). FreeAgent has no Terraform provider — its OAuth app
    (`Omnics InvoiceManager Sandbox` for `test`, `Omnics InvoiceManager` for
    `production`) must be registered manually at
    [dev.freeagent.com](https://dev.freeagent.com/) first. The refresh token
    is captured separately, through the admin website's FreeAgent
    authorization page, not by this script.
12. Seeds the invoice configurations, passing `--environment <env>` (and
    `--clear-database` when `-ClearDatabase` is supplied — see below).

### Function App storage connection string cleanup

The `azurerm` provider's `azurerm_function_app_flex_consumption` resource
silently re-injects empty-key `AzureWebJobsStorage` and
`DEPLOYMENT_STORAGE_CONNECTION_STRING` app settings on every create/update, even
though neither is in our Terraform `app_settings` and neither appears in the plan
(hashicorp/terraform-provider-azurerm
[#29149](https://github.com/hashicorp/terraform-provider-azurerm/issues/29149),
[#29993](https://github.com/hashicorp/terraform-provider-azurerm/issues/29993) —
both open as of azurerm 4.80.0). The blank-key `AzureWebJobsStorage` scalar
shadows the identity-based `AzureWebJobsStorage__*` settings, so the host falls
back to shared-key auth with an empty key and fails with `403 AuthenticationFailed`
on the `azure-webjobs-secrets` container — which stops the timer listener from
starting. After `terraform apply`, `Deploy-Infra.ps1` deletes these settings so
the host restarts onto managed-identity storage. The step is idempotent and runs
every deploy, so it converges regardless of the provider bug. Remove it once the
upstream provider issues are fixed and the pinned `azurerm` version includes the
fix.

### Seeding behavior (`--environment`, `--clear-database`)

The seeder **requires** `--environment <env>` so it can make the data
environment-aware (it exits non-zero when the flag is absent), and optionally
`--clear-database`. Locally the Aspire AppHost seeds the emulator with
`--environment test`, so OneDrive destinations are nested under the root `Test`
folder described below.

Seed values include `InvoiceManager__Seed__DriveId`,
`InvoiceManager__Seed__DriveName`, `InvoiceManager__Seed__Microsoft365FolderItemId`,
`InvoiceManager__Seed__AzureFolderItemId`,
`InvoiceManager__Seed__Microsoft365TestFolderItemId`,
`InvoiceManager__Seed__AzureTestFolderItemId`,
`InvoiceManager__Seed__BillingAccountId`, and
`InvoiceManager__Seed__AzureBillingAccountId` — eight values in total. For local
development, sign in to the Azure CLI as the user that owns the target OneDrive
and has billing-account access, then run:

```powershell
./tools/dev-setup/Set-SeedEnvironment.ps1
```

The script uses Microsoft Graph PowerShell to discover the signed-in user's
default OneDrive and installs the `Microsoft.Graph.Authentication` module at
CurrentUser scope when it is not already available. It resolves the stable Graph
item IDs for the `Bills/Microsoft 365`, `Bills/Azure + Visual Studio`,
`Test/Bills/Microsoft 365`, and `Test/Bills/Azure + Visual Studio` folders (which
must already exist in the target OneDrive) via path-based lookup. It uses the
Azure Billing `billingAccounts` REST endpoint and requires exactly one account of
each expected type, mapping the `Business` account to
`InvoiceManager__Seed__BillingAccountId` and the `Individual` account to
`InvoiceManager__Seed__AzureBillingAccountId`. It sets all eight values in the
current process and persistent User environment. Authentication prompts may
appear. Restart Visual Studio afterward so its AppHost process inherits the new
User values.

- **Test folder isolation**: test configurations address the distinct
  `Test/Bills/...` folder item IDs resolved above (real, separate Graph items from
  their production `Bills/...` counterparts), not a `Test` prefix inserted into a
  shared path at request time — so test downloads never collide with production
  files even though item-ID addressing has no path to prefix.
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
Terraform-managed Entra app registration. The whole operational site is limited
to direct members of a per-environment Entra security group. Terraform emits the
security-group claim and adds the deploying operator through a non-authoritative
membership resource, so additional direct members are not removed on apply.

Ordinary OIDC sign-in creates only the administrator site session and never writes
the shared MSAL cache. A separate, confirmed workflow-authorization action captures
Microsoft delegated authorization for Azure Resource Manager and Microsoft Graph,
then persists the serialized cache in Key Vault as
`MicrosoftAuthorization--MsalTokenCache`. Billing/OneDrive discovery and Functions
use that explicitly captured account.

The scopes requested on sign-in are hard-coded in
`MicrosoftOpenIdConnectOptionsSetup` (`src/InvoiceManager.AdminWeb/Program.cs`),
not derived from the app registration's declared `required_resource_access`.
Terraform can add a new delegated permission (e.g. `Mail.Read` for the
`GraphEmail` source) to the app registration, but that alone changes
nothing about what the interactive sign-in actually asks for or what the
admin has consented to — the scope must also be added here, and the admin
must sign in again afterward, before a new scope takes effect.

The admin website runs both locally (from `src/InvoiceManager.AdminWeb`) and
deployed to Azure Container Apps. Terraform registers both `/signin-oidc` and
`/workflow-signin-oidc` for the local `https://localhost:5001` origin, the
canonical custom hostname, and the generated Container Apps hostname. For test,
the canonical callbacks are:

```text
https://invoicemanager-test.omnics.tech/signin-oidc
https://invoicemanager-test.omnics.tech/workflow-signin-oidc
```

Production uses the same paths on `https://invoicemanager.omnics.tech`. Behind
the Container Apps ingress the app honors `X-Forwarded-Proto`
(forwarded-headers middleware) so the callback is built as `https://`. Both
administrator and workflow authorization use the authorization code flow with
PKCE and return the code in the callback query string. Query mode avoids
treating Entra's cross-origin callback as a form post, which .NET 11's automatic
CSRF protection rejects before the OIDC handler can validate it.

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
dotnet user-secrets set "KeyVault:Uri" "https://<key-vault-name>.vault.azure.net/" --project src/InvoiceManager.AdminWeb
dotnet user-secrets set "AdminAuthorization:GroupObjectId" "<admin-group-object-id>" --project src/InvoiceManager.AdminWeb
dotnet user-secrets set "FreeAgent:Environment" "Sandbox" --project src/InvoiceManager.AdminWeb
```

`FreeAgentAuthorization:ClientId`/`ClientSecret` are not set here — like
`MicrosoftAuthorization:ClientSecret`, they are loaded from Key Vault at
startup, never from local user-secrets.

`KeyVault:Uri` is shared, application-wide configuration (not specific to
Microsoft authorization) — every secret-backed store, including the FreeAgent
authorization store described below, resolves its Key Vault client from this
one setting.

The AppHost (`src/InvoiceManager.AppHost`, `UserSecretsId` `InvoiceManager.AppHost`) needs its own copies of the
`MicrosoftAuthorization` values above, plus the Document Intelligence endpoint used by the
`GraphEmail` invoice source — there is no local emulator for Document Intelligence, so this
must point at a real resource already provisioned by Terraform:

```bash
dotnet user-secrets set "DocumentIntelligence:Endpoint" "https://<doc-intel-resource-name>.cognitiveservices.azure.com/" --project src/InvoiceManager.AppHost
```

Without it, the Functions app fails `DocumentIntelligenceOptions` validation at startup and the
AppHost's `functions` resource never becomes healthy.

When the admin website starts, it uses `KeyVault:Uri` and `DefaultAzureCredential`
to load `MicrosoftAuthorization:ClientSecret` from Key Vault before binding and
validating the final authentication configuration.
Local developers must be signed in to Azure with access to the test Key Vault.
Key Vault access is controlled through Azure RBAC rather than legacy vault
access policies.

#### FreeAgent authorization

The `/Authorization` page also has a FreeAgent section, structurally parallel
to the Microsoft one above but visually separated and backed by its own form.
It registers a second named OAuth scheme (`FreeAgentWorkflowAuthorization`,
via `AddOAuth`) whose authorization/token endpoints are derived from
`FreeAgent:Environment` (`Sandbox` or `Production` — see `FreeAgentHosts`),
never from a separately configurable URL. On successful authorization, the
refresh token is written straight to Key Vault
(`FreeAgentAuthorization--RefreshToken`, via `IFreeAgentAuthorizationStore`)
and never placed in the authentication cookie.

FreeAgent OAuth apps are registered manually at
[dev.freeagent.com](https://dev.freeagent.com/) (no Terraform provider — see
`Deploy-Infra.ps1`'s FreeAgent client-credential provisioning above):

- **`Omnics InvoiceManager Sandbox`** — used by the test environment and by
  opt-in sandbox integration tests. Redirect URI:
  `https://invoicemanager-test.omnics.tech/freeagent-authorization/callback`, plus
  `https://localhost:5001/freeagent-authorization/callback` for local dev.
- **Future `Omnics InvoiceManager` production app** — this app does not exist
  yet. When it is created, configure the redirect URI
  `https://invoicemanager.omnics.tech/freeagent-authorization/callback`.

Before validating test authorization, add the canonical test redirect URI to
the existing sandbox app manually. No other external hostname-dependent callback
registration is currently known: Entra is Terraform-managed, and FreeAgent is
the only manual registration used by AdminWeb.

Each app's client ID/secret is provisioned into that environment's Key Vault
by `Deploy-Infra.ps1` (`FreeAgentAuthorization--ClientId`/`--ClientSecret`);
the refresh token is captured separately, once, by an administrator
completing the FreeAgent authorization section on that environment's
`/Authorization` page.

The admin website administers invoice configurations and append-only history.
Every invoice record is created with a required routing snapshot, so no record
migration gate is needed. The website still does not own invoice matching,
reconciliation, filename generation, or FreeAgent behavior.

### Local Playwright auth state for the admin website

The admin website's fallback authorization policy requires a real Entra sign-in
on every page, so AI coding agents (Claude, Codex, Copilot) and Playwright test
projects need a captured browser session to interact with it without prompting
for credentials each time. `tools/InvoiceManager.PlaywrightAuth` (a standalone
console app, not part of the test suite) drives this once:

```bash
dotnet run --project tools/InvoiceManager.PlaywrightAuth
```

It starts `src/InvoiceManager.AdminWeb` standalone on `https://localhost:5001`,
opens Edge, and waits for you to complete Microsoft sign-in. The resulting
storage state is saved to `playwright/.auth/adminweb.json` (gitignored). The
local user secrets set in the block above must already be configured on the
`InvoiceManager.AdminWeb` project for the standalone app to start — the AppHost
copy alone is not enough, since AppHost is not launched here.

The tool only waits for AdminWeb's Kestrel process to start listening on
`/health`, not for a healthy result — `/health` also aggregates Cosmos and
Functions app checks, and neither is reachable (or expected to be) in this
standalone launch, so it always reports `503`. Seeing "Cosmos DB is not
reachable" and "Functions:BaseUrl is not configured" in the AdminWeb console
output while the tool runs is expected and does not affect sign-in; only a
`ValidateOnStart` failure for the `MicrosoftAuthorization`/`AdminAuthorization`
options (missing user secrets) actually stops the app from starting.

`tests/InvoiceManager.AdminWeb.PlaywrightTests` reuses the saved storage state
but, unlike the capture tool, starts AdminWeb through the real AppHost
orchestration (`AdminWebAppHostFixture`, an `Aspire.Hosting.Testing`
collection fixture also usable by future Playwright tests) — Cosmos emulator,
seeder, and Functions all come up too, and the AdminWeb URL is read from the
running orchestration (`DistributedApplication.GetEndpoint("adminweb",
"https")`) rather than assumed to be `https://localhost:5001`. This needs
Docker running and AppHost's own user secrets configured (the `dotnet
user-secrets ... --project src/InvoiceManager.AppHost` copies above), and
takes noticeably longer than the capture tool's standalone launch.

The `playwright` MCP server (`.mcp.json`) loads that file automatically via
`--storage-state`, so once it exists, MCP-driven browser sessions start already
signed in. `tests/InvoiceManager.AdminWeb.PlaywrightTests` reuses the same file
for automated Playwright tests; it is tagged `Category=Integration` (like the
Cosmos emulator integration tests) so CI's `--filter "Category!=Integration"`
skips it, since it needs a real signed-in session and a running AdminWeb
instance.

Re-run the tool whenever the saved session expires (an Entra sign-in prompt
reappearing is the signal).

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

**How CI authenticates and learns the deployment targets** — the entire
CI-deployment surface is **Terraform-owned, per environment**. Terraform state is
already per-environment (a separate backend key per env), so a single config
produces one CI identity and one GitHub deploy environment per environment,
isolated from the other:

- **CI identity** — an Entra app + federated credential (pure OIDC, no client
  secret), federated to only its own GitHub environment
  (`repo:<owner>/<repo>:environment:<env>`) and granted **Contributor scoped to
  only that environment's resource group**. Its identifiers are written as
  **environment-scoped GitHub Actions secrets** `AZURE_CLIENT_ID`,
  `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` (OIDC identifiers, not passwords);
  `deploy.yml` reads them as `secrets.AZURE_*` unchanged.
- **Deploy targets** — the concrete target names are written as
  **environment-scoped GitHub Actions variables**: `FUNCTIONS_APP_NAME`,
  `FUNCTIONS_DEFAULT_HOSTNAME`, `ADMINWEB_CONTAINER_APP_NAME`, `ADMINWEB_FQDN`,
  `AZURE_RESOURCE_GROUP`.
- **Deploy environment + gate** — Terraform (the `integrations/github` provider)
  owns the `test` / `production` GitHub environment itself, its `main`-only
  branch policy, and — for production only — the required-reviewer rule
  (the reviewer is whoever runs `Deploy-Infra.ps1`, resolved from `gh api user`).
  The environment and secrets are created-or-updated (PUT), so they adopt a
  pre-existing GitHub environment with no `terraform import` and never drop the
  reviewer gate.

Set `manage_github = false` (via `-var`) for a GitHub-less apply that skips all
of the above.

**Jobs**:
1. **build-images**: build + push the admin website image to the public ghcr
   package (tagged with the commit SHA) using `GITHUB_TOKEN`; `dotnet publish`
   the Functions app and upload it as an artifact.
2. **deploy-test** (`environment: test`): if the Environment variables are set
   (infra exists), Azure OIDC login, deploy the Functions package, and
   `az containerapp update --image ...:<sha>`. **Before `Deploy-Infra.ps1` has
   ever run the variables are empty, so the job skips gracefully instead of
   failing.**
3. **deploy-production** (`environment: production`): same steps after test.
   Guarded two ways:
   - A **job-level `if: vars.PRODUCTION_DEPLOY_ENABLED == 'true'`**. Environment
     variables are invisible to a job-level `if`, so the guard uses a
     repository-level variable that `Deploy-Infra.ps1 -Environment production`
     sets after a successful production apply. Until production is live the job is
     skipped entirely, so it does not queue an approval prompt on every push to
     `main`.
   - Once the job does start, the **`production` GitHub Environment's
     required-reviewer rule** (and a `main`-only deployment branch policy) is the
     manual approval gate. Terraform owns this environment and its protection
     rules; because it creates-or-updates via PUT, the reviewer gate is set
     atomically on first apply and never dropped (no `terraform import` needed).

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

The CI identity's OIDC identifiers are **environment-scoped** secrets that
Terraform manages per environment (not repository-level secrets, and not
hand-configured in the GitHub UI):

- `AZURE_SUBSCRIPTION_ID`: Azure subscription for deployment.
- `AZURE_TENANT_ID`: Azure AD tenant ID.
- `AZURE_CLIENT_ID`: The per-environment CI Entra app's client id.

There is **no `AZURE_CLIENT_SECRET`** — authentication is pure OIDC federation
(GitHub → Azure token exchange), so there is no password to store or rotate.
These are accessed in workflows via `${{ secrets.AZURE_CLIENT_ID }}` within the
`environment: test` / `environment: production` jobs, which resolve to the
matching environment's secret.

#### Azure Key Vault

Production secrets are stored in Azure Key Vault and accessed by Azure Functions using Managed Identity:

1. Each environment has its own Key Vault (`invoicemanager-test-kv`, `invoicemanager-kv`).
2. Azure Functions use Managed Identity to authenticate to Key Vault.
3. Key Vault data-plane access is granted through Azure RBAC roles.
4. Secrets are referenced in code using `SecretClient` from `Azure.Security.KeyVault.Secrets`.

Example secrets in Key Vault:

- `MicrosoftAuthorization--ClientSecret`
- `MicrosoftAuthorization--MsalTokenCache`
- `FreeAgentAuthorization--ClientId`
- `FreeAgentAuthorization--ClientSecret`
- `FreeAgentAuthorization--RefreshToken`
- `InvoiceIntegrations--AzureTenantId`
- `InvoiceIntegrations--AzureClientId`
- `InvoiceIntegrations--AzureClientSecret`
- `InvoiceIntegrations--OpenAiApiKey`
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

## CI/CD Identity

The GitHub Actions deployment identity is **Terraform-owned, one per environment**
(`infra/terraform/ci.tf`). There is no manual setup and no shared identity across
environments:

1. **Provisioning** (by `Deploy-Infra.ps1` at apply time):
   - An Entra app + service principal (`InvoiceManager-GitHubActions` for
     production, `-test` suffix for test).
   - A federated identity credential subject to only that environment's GitHub
     deploy environment — no client secret.
   - A **Contributor** role assignment scoped to only that environment's resource
     group.

2. **Permissions**:
   - Manage resources in **only its own** resource group (test CI cannot touch
     production and vice-versa).
   - Deploy the Functions package (Kudu) and update the Container App image.
   - Terraform backend state is accessed by the operator running
     `Deploy-Infra.ps1`, not by the CI identity.

3. **Security**:
   - Pure OIDC federation — no stored secret to leak or rotate.
   - Per-environment isolation limits blast radius.
   - The identity, its RBAC, and the GitHub environment/secrets/variables are all
     declared in Terraform and visible to code review, rather than hand-built
     out-of-band.

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
