[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("test", "production")]
    [string] $Environment,

    [string] $Location = "uksouth",

    [string] $SubscriptionId,

    [string] $ApplicationName = "invoicemanager",

    [switch] $PlanOnly,

    [switch] $AutoApprove
)

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

function Set-AdminWebUserSecret {
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

    Write-Section "Configuring local admin website"

    Push-Location $TerraformRoot
    try {
        $outputs = Invoke-JsonCommand -Command @("terraform", "output", "-json")
    }
    finally {
        Pop-Location
    }

    $adminWebProject = Join-Path $RepoRoot "src/InvoiceManager.AdminWeb/InvoiceManager.AdminWeb.csproj"

    Set-AdminWebUserSecret -ProjectPath $adminWebProject -Key "MicrosoftAuthorization:TenantId" -Value $outputs.tenant_id.value
    Set-AdminWebUserSecret -ProjectPath $adminWebProject -Key "MicrosoftAuthorization:ClientId" -Value $outputs.application_client_id.value
    Set-AdminWebUserSecret -ProjectPath $adminWebProject -Key "MicrosoftAuthorization:KeyVaultUri" -Value $outputs.key_vault_uri.value

    Write-Host "Local admin website user secrets configured for the test environment."
    Write-Host "Client secret remains in Key Vault as MicrosoftAuthorization--ClientSecret."
}

function Invoke-ConfigurationSeeder {
    param(
        [string] $TerraformRoot,
        [string] $RepoRoot
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

    $env:COSMOS_ENDPOINT = $cosmosEndpoint
    $env:COSMOS_DATABASE = $cosmosDatabase
    try {
        # Build once so every retry runs the pre-compiled binary rather than
        # triggering a fresh compilation on each attempt.
        Invoke-CheckedCommand -Command @("dotnet", "build", "--project", $seederProject)

        # Cosmos DB data-plane RBAC can take up to ~60 s to propagate after
        # terraform apply creates the role assignment. The seeder exits 2 on a
        # transient 403; all other non-zero exits are permanent failures.
        $maxAttempts = 5
        for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
            & dotnet run --project $seederProject --no-build -- $seedFile
            $exitCode = $LASTEXITCODE

            if ($exitCode -eq 0) { return }

            # Exit code 2 = Cosmos 403 (RBAC propagation delay) — retry.
            # Any other non-zero exit is a permanent failure; surface immediately.
            if ($exitCode -ne 2 -or $attempt -ge $maxAttempts) {
                throw "Seeder failed with exit code $exitCode (attempt $attempt/$maxAttempts)."
            }

            Write-Host "Seeder attempt $attempt/$maxAttempts: Cosmos 403 (RBAC propagation delay). Retrying in 30 s..."
            Start-Sleep -Seconds 30
        }
    }
    finally {
        Remove-Item Env:\COSMOS_ENDPOINT -ErrorAction SilentlyContinue
        Remove-Item Env:\COSMOS_DATABASE -ErrorAction SilentlyContinue
        if ($null -ne $savedArmKey) { $env:ARM_ACCESS_KEY = $savedArmKey }
    }
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

    & terraform plan `
        -detailed-exitcode `
        "-var-file=$tfVarsFile" `
        "-var=location=$Location" `
        "-var=application_name=$ApplicationName" `
        "-out=$Environment.tfplan"
    $planExitCode = $LASTEXITCODE

    if ($planExitCode -eq 0) {
        Write-Host "Terraform plan completed with no changes."
        if (-not $PlanOnly) {
            Invoke-ConfigurationSeeder -TerraformRoot $terraformRoot -RepoRoot $repoRoot
            if ($Environment -eq "test") {
                Set-TestAdminWebLocalConfiguration -TerraformRoot $terraformRoot -RepoRoot $repoRoot
            }
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

    Invoke-ConfigurationSeeder -TerraformRoot $terraformRoot -RepoRoot $repoRoot

    if ($Environment -eq "test") {
        Set-TestAdminWebLocalConfiguration -TerraformRoot $terraformRoot -RepoRoot $repoRoot
    }
}
finally {
    Pop-Location
    Remove-Item Env:\ARM_ACCESS_KEY -ErrorAction SilentlyContinue
}
