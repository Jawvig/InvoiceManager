[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("test", "production")]
    [string] $Environment,

    [string] $Location = "uksouth",

    [string] $SubscriptionId,

    [string] $ApplicationName = "invoicemanager",

    [switch] $PlanOnly,

    [switch] $AutoApprove,

    [switch] $ClearDatabase,

    [switch] $SkipGitHubVars,

    # Build and push the real admin website image before applying, so the Container App is
    # created against a genuine image (a better apply test than the bootstrap reference).
    [switch] $PublishAdminWebImage,

    # Use an already-published admin website image reference (no build/push). Mutually
    # exclusive with -PublishAdminWebImage; use when the image was pushed out-of-band.
    [string] $AdminWebImage
)

if ($PublishAdminWebImage -and $AdminWebImage) {
    throw "-PublishAdminWebImage and -AdminWebImage are mutually exclusive."
}

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Section {
    param([string] $Message)

    Write-Host ""
    Write-Host "== $Message =="
}

function Test-Command {
    param([string] $Name)

    $null -ne (Get-Command $Name -ErrorAction SilentlyContinue)
}

function Show-TerraformInstallHelp {
    Write-Host "Terraform is required but was not found on PATH."
    Write-Host ""
    Write-Host "Install Terraform from:"
    Write-Host "  https://developer.hashicorp.com/terraform/install"
    Write-Host ""
    Write-Host "Common options:"
    Write-Host "  Windows: winget install Hashicorp.Terraform"
    Write-Host "  macOS:   brew tap hashicorp/tap && brew install hashicorp/tap/terraform"
    Write-Host "  Linux:   follow the HashiCorp package repository instructions for your distribution"
}

function Show-AzureCliInstallHelp {
    Write-Host "Azure CLI is required but was not found on PATH."
    Write-Host ""
    Write-Host "Install Azure CLI from:"
    Write-Host "  https://learn.microsoft.com/cli/azure/install-azure-cli"
    Write-Host ""
    Write-Host "Common options:"
    Write-Host "  Windows: winget install Microsoft.AzureCLI"
    Write-Host "  macOS:   brew install azure-cli"
    Write-Host "  Linux:   follow the Microsoft package repository instructions for your distribution"
}

function Invoke-JsonCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $Command
    )

    $arguments = @($Command | Select-Object -Skip 1)
    $output = & $Command[0] @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed: $($Command -join ' ')"
    }

    if ([string]::IsNullOrWhiteSpace($output)) {
        return $null
    }

    return $output | ConvertFrom-Json
}

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $Command
    )

    $arguments = @($Command | Select-Object -Skip 1)
    & $Command[0] @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed: $($Command -join ' ')"
    }
}

function Get-ShortHash {
    param([string] $Value)

    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value)
        $hash = $sha.ComputeHash($bytes)
        return -join ($hash[0..2] | ForEach-Object { $_.ToString("x2") })
    }
    finally {
        $sha.Dispose()
    }
}

function Get-EnvironmentSuffix {
    param([string] $Name)

    if ($Name -eq "production") {
        return ""
    }

    return "-$Name"
}

function Get-StorageEnvironmentToken {
    param([string] $Name)

    if ($Name -eq "production") {
        return ""
    }

    return $Name.ToLowerInvariant()
}

function Ensure-AzureLogin {
    $account = $null

    try {
        $account = Invoke-JsonCommand -Command @("az", "account", "show", "--output", "json")
    }
    catch {
        Write-Host "Azure CLI is installed but not logged in. Starting 'az login'."
        Invoke-CheckedCommand -Command @("az", "login")
        $account = Invoke-JsonCommand -Command @("az", "account", "show", "--output", "json")
    }

    if ($SubscriptionId) {
        Invoke-CheckedCommand -Command @("az", "account", "set", "--subscription", $SubscriptionId)
        $account = Invoke-JsonCommand -Command @("az", "account", "show", "--output", "json")
    }

    return $account
}

function Ensure-ResourceGroup {
    param(
        [string] $Name,
        [string] $Location
    )

    $exists = $false
    try {
        $null = Invoke-JsonCommand -Command @("az", "group", "show", "--name", $Name, "--output", "json")
        $exists = $true
    }
    catch {
        $exists = $false
    }

    if ($exists) {
        Write-Host "Resource group exists: $Name"
        return
    }

    Write-Host "Creating resource group: $Name"
    Invoke-CheckedCommand -Command @("az", "group", "create", "--name", $Name, "--location", $Location, "--output", "none")
}

function Ensure-StorageAccount {
    param(
        [string] $Name,
        [string] $ResourceGroup,
        [string] $Location
    )

    $exists = $false
    try {
        $null = Invoke-JsonCommand -Command @("az", "storage", "account", "show", "--name", $Name, "--resource-group", $ResourceGroup, "--output", "json")
        $exists = $true
    }
    catch {
        $exists = $false
    }

    if ($exists) {
        Write-Host "Storage account exists: $Name"
        return
    }

    Write-Host "Creating storage account: $Name"
    Invoke-CheckedCommand -Command @(
        "az", "storage", "account", "create",
        "--name", $Name,
        "--resource-group", $ResourceGroup,
        "--location", $Location,
        "--sku", "Standard_LRS",
        "--kind", "StorageV2",
        "--min-tls-version", "TLS1_2",
        "--allow-blob-public-access", "false",
        "--https-only", "true",
        "--output", "none"
    )
}

function Get-StorageAccountKey {
    param(
        [string] $Name,
        [string] $ResourceGroup
    )

    $keys = Invoke-JsonCommand -Command @(
        "az", "storage", "account", "keys", "list",
        "--account-name", $Name,
        "--resource-group", $ResourceGroup,
        "--output", "json"
    )

    return $keys[0].value
}

function Ensure-StorageContainer {
    param(
        [string] $Name,
        [string] $StorageAccount,
        [string] $StorageKey
    )

    Write-Host "Ensuring storage container exists: $Name"
    Invoke-CheckedCommand -Command @(
        "az", "storage", "container", "create",
        "--name", $Name,
        "--account-name", $StorageAccount,
        "--account-key", $StorageKey,
        "--public-access", "off",
        "--output", "none"
    )
}

function Set-ProjectUserSecret {
    param(
        [string] $ProjectPath,
        [string] $Key,
        [string] $Value
    )

    Invoke-CheckedCommand -Command @(
        "dotnet", "user-secrets", "set",
        $Key,
        $Value,
        "--project",
        $ProjectPath
    )
}

function Set-TestAdminWebLocalConfiguration {
    param(
        [string] $TerraformRoot,
        [string] $RepoRoot
    )

    if (-not (Test-Command "dotnet")) {
        throw ".NET SDK is required to configure local admin website user secrets."
    }

    Write-Section "Configuring local user secrets"

    Push-Location $TerraformRoot
    try {
        $outputs = Invoke-JsonCommand -Command @("terraform", "output", "-json")
    }
    finally {
        Pop-Location
    }

    # These non-secret values are read both by the admin website (when run directly) and by
    # the AppHost, which reads its OWN user-secrets store and forwards them to the Functions
    # and admin website resources. Set them for both projects so a clean setup needs no manual
    # step. The client secret is never stored here; it stays in Key Vault.
    $projects = @(
        (Join-Path $RepoRoot "src/InvoiceManager.AdminWeb/InvoiceManager.AdminWeb.csproj"),
        (Join-Path $RepoRoot "src/InvoiceManager.AppHost/InvoiceManager.AppHost.csproj")
    )

    foreach ($project in $projects) {
        Set-ProjectUserSecret -ProjectPath $project -Key "MicrosoftAuthorization:TenantId" -Value $outputs.tenant_id.value
        Set-ProjectUserSecret -ProjectPath $project -Key "MicrosoftAuthorization:ClientId" -Value $outputs.application_client_id.value
        Set-ProjectUserSecret -ProjectPath $project -Key "MicrosoftAuthorization:KeyVaultUri" -Value $outputs.key_vault_uri.value
    }

    Write-Host "Local user secrets configured for the test environment (admin website + AppHost)."
    Write-Host "Client secret remains in Key Vault as MicrosoftAuthorization--ClientSecret."
}

function Invoke-ConfigurationSeeder {
    param(
        [string] $TerraformRoot,
        [string] $RepoRoot,
        [string] $Environment,
        [switch] $ClearDatabase
    )

    Write-Section "Seeding invoice configurations"

    Push-Location $TerraformRoot
    try {
        $outputs = Invoke-JsonCommand -Command @("terraform", "output", "-json")
    }
    finally {
        Pop-Location
    }

    $cosmosEndpoint = $outputs.cosmos_endpoint.value
    $cosmosDatabase = $outputs.cosmos_database_name.value
    $seedFile = Join-Path $RepoRoot "data/seed/invoice-configurations.json"
    $seederProject = Join-Path $RepoRoot "tools/InvoiceManager.Seeder/InvoiceManager.Seeder.csproj"

    # Remove the Terraform state storage key from the seeder's environment so it
    # is not visible to the seeder process or any diagnostic output it produces.
    $savedArmKey = $env:ARM_ACCESS_KEY
    Remove-Item Env:\ARM_ACCESS_KEY -ErrorAction SilentlyContinue

    # Use the same configuration keys the seeder (via CosmosClientFactory) reads.
    # CosmosEndpoint + DefaultAzureCredential authenticates against the real account;
    # the schema already exists (Terraform owns it) so --ensure-schema is deliberately
    # not passed here.
    $env:CosmosEndpoint = $cosmosEndpoint
    $env:CosmosDatabase = $cosmosDatabase

    # Forward the environment so the seeder nests test downloads under a "Test" folder, and
    # optionally clear the containers first for a clean re-seed.
    $seederArgs = @($seedFile, "--environment", $Environment)
    if ($ClearDatabase) {
        $seederArgs += "--clear-database"
        Write-Host "Clearing database contents before seeding (--clear-database)."
    }

    try {
        # Build once so every retry runs the pre-compiled binary rather than
        # triggering a fresh compilation on each attempt.
        Invoke-CheckedCommand -Command @("dotnet", "build", $seederProject)

        # Cosmos DB data-plane RBAC can take up to ~60 s to propagate after
        # terraform apply creates the role assignment. The seeder exits 2 on a
        # transient 403; all other non-zero exits are permanent failures.
        $maxAttempts = 5
        for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
            & dotnet run --project $seederProject --no-build -- @seederArgs
            $exitCode = $LASTEXITCODE

            if ($exitCode -eq 0) { return }

            # Exit code 2 = Cosmos 403 (RBAC propagation delay) — retry.
            # Any other non-zero exit is a permanent failure; surface immediately.
            if ($exitCode -ne 2 -or $attempt -ge $maxAttempts) {
                throw "Seeder failed with exit code $exitCode (attempt $attempt/$maxAttempts)."
            }

            Write-Host "Seeder attempt $attempt/${maxAttempts}: Cosmos 403 (RBAC propagation delay). Retrying in 30 s..."
            Start-Sleep -Seconds 30
        }
    }
    finally {
        Remove-Item Env:\CosmosEndpoint -ErrorAction SilentlyContinue
        Remove-Item Env:\CosmosDatabase -ErrorAction SilentlyContinue
        if ($null -ne $savedArmKey) { $env:ARM_ACCESS_KEY = $savedArmKey }
    }
}

function Publish-GitHubEnvironmentVariables {
    param(
        [string] $TerraformRoot,
        [string] $Environment
    )

    if ($SkipGitHubVars) {
        Write-Host "Skipping GitHub environment variable publishing (-SkipGitHubVars)."
        return
    }

    Write-Section "Publishing GitHub environment variables"

    # Non-fatal: a missing/unauthenticated gh (or GitHub Environment) should not fail an
    # otherwise successful infrastructure deploy. CI reads these variables to learn the
    # concrete deployment targets, and skips deployment gracefully when they are absent.
    if (-not (Test-Command "gh")) {
        Write-Warning "GitHub CLI (gh) not found; skipping. Install it and re-run to enable CI deployment, or set the variables manually."
        return
    }

    try {
        Invoke-CheckedCommand -Command @("gh", "auth", "status")
    }
    catch {
        Write-Warning "GitHub CLI is not authenticated (gh auth login); skipping variable publishing."
        return
    }

    Push-Location $TerraformRoot
    try {
        $outputs = Invoke-JsonCommand -Command @("terraform", "output", "-json")
    }
    finally {
        Pop-Location
    }

    # Ensure the target GitHub Environment exists before setting environment-scoped variables
    # (setting a variable does not auto-create the environment). Create it ONLY when absent:
    # a bare PUT replaces the environment and would reset its protection rules, wiping the
    # production required-reviewer/branch-policy gate. Tolerate failure (e.g. token scope).
    $environmentExists = $false
    try {
        Invoke-CheckedCommand -Command @(
            "gh", "api", "repos/{owner}/{repo}/environments/$Environment", "--silent"
        )
        $environmentExists = $true
    }
    catch {
        $environmentExists = $false
    }

    if ($environmentExists) {
        Write-Host "GitHub environment '$Environment' already exists; leaving its protection rules unchanged."
    }
    else {
        try {
            Invoke-CheckedCommand -Command @(
                "gh", "api", "--method", "PUT", "repos/{owner}/{repo}/environments/$Environment", "--silent"
            )
            Write-Host "Created GitHub environment '$Environment'."
        }
        catch {
            Write-Warning "Could not create the GitHub '$Environment' environment; variable publishing may fail."
        }
    }

    # GitHub Environment variable name -> Terraform output value.
    $variables = [ordered]@{
        FUNCTIONS_APP_NAME          = $outputs.functions_app_name.value
        FUNCTIONS_DEFAULT_HOSTNAME  = $outputs.functions_default_hostname.value
        ADMINWEB_CONTAINER_APP_NAME = $outputs.adminweb_container_app_name.value
        ADMINWEB_FQDN               = $outputs.adminweb_fqdn.value
        AZURE_RESOURCE_GROUP        = $outputs.resource_group_name.value
    }

    foreach ($name in $variables.Keys) {
        try {
            Invoke-CheckedCommand -Command @(
                "gh", "variable", "set", $name,
                "--env", $Environment,
                "--body", $variables[$name]
            )
            Write-Host "  Set $name for environment '$Environment'."
        }
        catch {
            Write-Warning "  Failed to set $name (does the GitHub '$Environment' environment exist?)."
        }
    }

    # The tolerated gh failures above leave $LASTEXITCODE non-zero, which would otherwise make
    # the whole script report failure despite a successful deploy. Reset it on this path.
    $global:LASTEXITCODE = 0
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$terraformRoot = Join-Path $repoRoot "infra/terraform"

Write-Section "Checking tools"

if (-not (Test-Command "terraform")) {
    Show-TerraformInstallHelp
    exit 1
}

if (-not (Test-Command "az")) {
    Show-AzureCliInstallHelp
    exit 1
}

Write-Host "Terraform: $(terraform version -json | ConvertFrom-Json | Select-Object -ExpandProperty terraform_version)"
Write-Host "Azure CLI: $(az version --query '\"azure-cli\"' --output tsv)"

Write-Section "Checking Azure login"
$account = Ensure-AzureLogin
$activeSubscriptionId = $account.id
$tenantId = $account.tenantId

Write-Host "Subscription: $($account.name) ($activeSubscriptionId)"
Write-Host "Tenant: $tenantId"

$suffix = Get-EnvironmentSuffix -Name $Environment
$storageEnvironmentToken = Get-StorageEnvironmentToken -Name $Environment
$subscriptionHash = Get-ShortHash -Value $activeSubscriptionId

$stateResourceGroup = "rg-$ApplicationName-tfstate$suffix"
$stateStorageAccount = ("imtfstate$storageEnvironmentToken$subscriptionHash").ToLowerInvariant()
$stateContainer = "tfstate"
$stateKey = "$ApplicationName$suffix.tfstate"

Write-Section "Ensuring Terraform backend"
Write-Host "Resource group: $stateResourceGroup"
Write-Host "Storage account: $stateStorageAccount"
Write-Host "Container: $stateContainer"
Write-Host "State key: $stateKey"

Ensure-ResourceGroup -Name $stateResourceGroup -Location $Location
Ensure-StorageAccount -Name $stateStorageAccount -ResourceGroup $stateResourceGroup -Location $Location
$stateStorageKey = Get-StorageAccountKey -Name $stateStorageAccount -ResourceGroup $stateResourceGroup
Ensure-StorageContainer -Name $stateContainer -StorageAccount $stateStorageAccount -StorageKey $stateStorageKey

Write-Section "Running Terraform"

$env:ARM_ACCESS_KEY = $stateStorageKey

Push-Location $terraformRoot
try {
    Invoke-CheckedCommand -Command @(
        "terraform", "init",
        "-reconfigure",
        "-backend-config=resource_group_name=$stateResourceGroup",
        "-backend-config=storage_account_name=$stateStorageAccount",
        "-backend-config=container_name=$stateContainer",
        "-backend-config=key=$stateKey"
    )

    $tfVarsFile = "$Environment.tfvars"

    $planArgs = @(
        "-detailed-exitcode",
        "-var-file=$tfVarsFile",
        "-var=location=$Location",
        "-var=application_name=$ApplicationName"
    )

    # Electively build + push the real admin website image and pin the plan to it, so the
    # Container App is created against a genuine image rather than the bootstrap reference.
    # -AdminWebImage takes an already-published reference and skips the build/push.
    if ($PublishAdminWebImage) {
        Write-Section "Publishing admin website image"
        $publishScript = Join-Path $PSScriptRoot "Publish-AdminWebImage.ps1"
        $imageTag = "$Environment-$(Get-Date -Format yyyyMMddHHmmss)"
        $adminWebImage = & $publishScript -Tag $imageTag -Push | Select-Object -Last 1
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($adminWebImage)) {
            throw "Failed to publish the admin website image."
        }
        Write-Host "Admin website image: $adminWebImage"
        $planArgs += "-var=adminweb_image=$adminWebImage"
    }
    elseif ($AdminWebImage) {
        Write-Host "Using pre-published admin website image: $AdminWebImage"
        $planArgs += "-var=adminweb_image=$AdminWebImage"
    }

    $planArgs += "-out=$Environment.tfplan"

    & terraform plan @planArgs
    $planExitCode = $LASTEXITCODE

    if ($planExitCode -eq 0) {
        Write-Host "Terraform plan completed with no changes."
        if (-not $PlanOnly) {
            Invoke-ConfigurationSeeder -TerraformRoot $terraformRoot -RepoRoot $repoRoot -Environment $Environment -ClearDatabase:$ClearDatabase
            if ($Environment -eq "test") {
                Set-TestAdminWebLocalConfiguration -TerraformRoot $terraformRoot -RepoRoot $repoRoot
            }
            Publish-GitHubEnvironmentVariables -TerraformRoot $terraformRoot -Environment $Environment
        }
        return
    }

    if ($planExitCode -ne 2) {
        throw "Command failed: terraform plan -detailed-exitcode -var-file=$tfVarsFile -var=location=$Location -var=application_name=$ApplicationName -out=$Environment.tfplan"
    }

    if ($PlanOnly) {
        Write-Host "Plan created at infra/terraform/$Environment.tfplan"
        return
    }

    if (-not $AutoApprove) {
        $confirmation = Read-Host "Apply this Terraform plan? Type 'yes' to continue"
        if ($confirmation -ne "yes") {
            Write-Host "Terraform apply cancelled."
            return
        }
    }

    $applyCommand = @("terraform", "apply")
    $applyCommand += "$Environment.tfplan"

    Invoke-CheckedCommand -Command $applyCommand

    Invoke-ConfigurationSeeder -TerraformRoot $terraformRoot -RepoRoot $repoRoot -Environment $Environment -ClearDatabase:$ClearDatabase

    if ($Environment -eq "test") {
        Set-TestAdminWebLocalConfiguration -TerraformRoot $terraformRoot -RepoRoot $repoRoot
    }

    Publish-GitHubEnvironmentVariables -TerraformRoot $terraformRoot -Environment $Environment
}
finally {
    Pop-Location
    Remove-Item Env:\ARM_ACCESS_KEY -ErrorAction SilentlyContinue
}
