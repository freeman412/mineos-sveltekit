param(
    [switch]$Build,
    [string]$Ref = "main",
    [string]$InstallDir = "mineos",
    [string]$BundleUrl = "",
    [string]$CliUrl = "",
    [switch]$NoCli,
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

function Get-PlatformArch {
    $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    switch ($arch) {
        "X64" { return "amd64" }
        "Arm64" { return "arm64" }
        default { return "" }
    }
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

if (-not $NoCli) {
    $arch = Get-PlatformArch
    if ([string]::IsNullOrWhiteSpace($arch)) {
        Write-Info "Unsupported CPU architecture for mineos-cli. Skipping."
    } else {
        if ([string]::IsNullOrWhiteSpace($CliUrl)) {
            $assetName = "mineos-cli_windows_$arch.zip"
            $CliUrl = Get-LatestBundleUrl -AssetName $assetName
        }

        if ([string]::IsNullOrWhiteSpace($CliUrl)) {
            Write-Info "Unable to locate mineos-cli asset for windows/$arch. Skipping."
        } else {
            $cliZip = Join-Path $tmpDir "mineos-cli.zip"
            Write-Info "Downloading mineos-cli (windows/$arch)..."
            Invoke-WebRequest -Uri $CliUrl -OutFile $cliZip -UseBasicParsing

            $cliExtract = Join-Path $tmpDir "cli"
            New-Item -ItemType Directory -Force -Path $cliExtract | Out-Null
            Expand-Archive -Path $cliZip -DestinationPath $cliExtract -Force

            $binName = "mineos-windows-$arch.exe"
            $binPath = Join-Path $cliExtract $binName
            if (Test-Path $binPath) {
                $dest = Join-Path $InstallDir "mineos.exe"
                Copy-Item $binPath $dest -Force
                Write-Info "Installed mineos-cli to $dest"
            } else {
                Write-Info "mineos-cli binary not found in archive. Skipping."
            }
        }
    }
}

& $scriptPath @forwardArgs
exit $LASTEXITCODE
