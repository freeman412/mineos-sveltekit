param(
    [switch]$Build,
    [string]$Ref = "main",
    [string]$InstallDir = "mineos",
    [string]$BundleUrl = "",
    [string]$RepoUrl = "https://github.com/freeman412/mineos-sveltekit.git",
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Args
)

$ErrorActionPreference = "Stop"

function Write-Info { Write-Host "[INFO] $($args -join ' ')" -ForegroundColor Cyan }
function Write-Error-Custom { Write-Host "[ERR] $($args -join ' ')" -ForegroundColor Red }

function Get-LatestBundleUrl {
    param([string]$AssetName)
    $api = "https://api.github.com/repos/freeman412/mineos-sveltekit/releases/latest"
    $release = Invoke-RestMethod -Uri $api -UseBasicParsing
    $asset = $release.assets | Where-Object { $_.name -eq $AssetName } | Select-Object -First 1
    return $asset.browser_download_url
}

if ([string]::IsNullOrWhiteSpace($InstallDir)) {
    Write-Error-Custom "Install directory is required."
    exit 1
}

$forwardArgs = @()
if ($Args) { $forwardArgs += $Args }

if ($Build) {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        Write-Error-Custom "git is required for -Build."
        exit 1
    }

    if (Test-Path (Join-Path $InstallDir ".git")) {
        Write-Info "Using existing repo at $InstallDir"
    } elseif (Test-Path $InstallDir -and (Get-ChildItem -Path $InstallDir -Force | Measure-Object).Count -gt 0) {
        Write-Error-Custom "Directory $InstallDir exists and is not empty."
        exit 1
    } else {
        Write-Info "Cloning repo..."
        git clone --depth 1 --branch $Ref $RepoUrl $InstallDir
    }

    $scriptPath = Join-Path $InstallDir "MineOS.ps1"
    if (-not (Test-Path $scriptPath)) {
        Write-Error-Custom "MineOS.ps1 not found in $InstallDir."
        exit 1
    }

    $forwardArgs = @("-Build") + $forwardArgs
    & $scriptPath @forwardArgs
    exit $LASTEXITCODE
}

if ([string]::IsNullOrWhiteSpace($BundleUrl)) {
    $BundleUrl = Get-LatestBundleUrl -AssetName "mineos-install-bundle.zip"
}

if ([string]::IsNullOrWhiteSpace($BundleUrl)) {
    Write-Error-Custom "Unable to locate install bundle URL."
    exit 1
}

$tmpDir = Join-Path $env:TEMP ("mineos-" + [Guid]::NewGuid().ToString("n"))
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$zipPath = Join-Path $tmpDir "mineos-install-bundle.zip"

Write-Info "Downloading install bundle..."
Invoke-WebRequest -Uri $BundleUrl -OutFile $zipPath -UseBasicParsing

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Expand-Archive -Path $zipPath -DestinationPath $InstallDir -Force

$scriptPath = Join-Path $InstallDir "MineOS.ps1"
if (-not (Test-Path $scriptPath)) {
    Write-Error-Custom "MineOS.ps1 not found after extraction."
    exit 1
}

& $scriptPath @forwardArgs
exit $LASTEXITCODE
