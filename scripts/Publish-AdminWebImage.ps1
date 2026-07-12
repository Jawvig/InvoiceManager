<#
.SYNOPSIS
    Builds the admin website container image from src/InvoiceManager.AdminWeb/Dockerfile and,
    optionally, pushes it to a (public) ghcr.io package.

.DESCRIPTION
    This is the same artifact CI produces, runnable locally and electively. Publishing a real
    image before `Deploy-Infra.ps1` lets the first `terraform apply` create the Container App
    against a genuine image on port 8080 (a far better test than the stock bootstrap image).

    Deploy-Infra.ps1 -PublishAdminWebImage calls this script and threads the resulting image
    reference into the Terraform plan via -var=adminweb_image.

    Note: for Azure Container Apps to pull anonymously, the ghcr package must be public. Package
    visibility is a one-time GitHub setting (Packages -> package -> Package settings), not
    something the push can set.

.PARAMETER Image
    Full image reference (registry/owner/name:tag). When omitted it is derived from the origin
    remote owner and -Tag.

.PARAMETER Tag
    Tag to use when Image is not supplied. Defaults to a timestamped local tag.

.PARAMETER Push
    Push the built image to the registry. Requires a prior `docker login ghcr.io` (or gh's
    credential helper). Omit to build only.
#>
[CmdletBinding()]
param(
    [string] $Image,
    [string] $Tag = "local-$(Get-Date -Format yyyyMMddHHmmss)",
    [switch] $Push
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-RepositoryOwner {
    $remoteUrl = & git remote get-url origin
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to read the git origin remote to determine the ghcr owner. Pass -Image explicitly."
    }

    if ($remoteUrl -match "github\.com[/:]([^/]+)/") {
        return $Matches[1]
    }

    throw "Could not parse a GitHub owner from '$remoteUrl'. Pass -Image explicitly."
}

if (-not (Get-Command "docker" -ErrorAction SilentlyContinue)) {
    throw "Docker is required to build the admin website image but was not found on PATH."
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")

if (-not $Image) {
    $owner = (Get-RepositoryOwner).ToLowerInvariant()
    $Image = "ghcr.io/$owner/invoicemanager-adminweb:$Tag"
}

$dockerfile = Join-Path $repoRoot "src/InvoiceManager.AdminWeb/Dockerfile"

Write-Host "Building admin website image: $Image"
# Build context is the repository root (the app has cross-project references under src/).
& docker build -f $dockerfile -t $Image $repoRoot | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "docker build failed."
}

if ($Push) {
    Write-Host "Pushing $Image"
    & docker push $Image | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "docker push failed. Run 'docker login ghcr.io' first (or check package permissions)."
    }
}
else {
    Write-Host "Built locally (not pushed). Re-run with -Push to publish to ghcr."
}

# Emit the image reference as the sole pipeline output so callers can capture it.
Write-Output $Image
