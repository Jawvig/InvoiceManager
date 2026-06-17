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
        "-backend-config=resource_group_name=$stateResourceGroup",
        "-backend-config=storage_account_name=$stateStorageAccount",
        "-backend-config=container_name=$stateContainer",
        "-backend-config=key=$stateKey"
    )

    $tfVarsFile = "$Environment.tfvars"

    Invoke-CheckedCommand -Command @(
        "terraform", "plan",
        "-var-file=$tfVarsFile",
        "-out=$Environment.tfplan"
    )

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
}
finally {
    Pop-Location
    Remove-Item Env:\ARM_ACCESS_KEY -ErrorAction SilentlyContinue
}
