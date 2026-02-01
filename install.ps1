param(
    [switch]$Build,
    [string]$Ref = "main",
    [string]$InstallDir = "mineos",
    [string]$Version = "",
    [switch]$Preview,
    [string]$BundleUrl = "",
    [string]$CliUrl = "",
    [switch]$NoCli,
    [string]$RepoUrl = "https://github.com/freeman412/mineos-sveltekit.git",
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ForwardArgs
)

$ErrorActionPreference = "Stop"

function Write-Info { Write-Host "[INFO] $($args -join ' ')" -ForegroundColor Cyan }
function Write-Error-Custom { Write-Host "[ERR] $($args -join ' ')" -ForegroundColor Red }

# Pause on failure so the user can read the error message.
# Handles both double-click (.ps1) and piped (iex) execution gracefully.
function Wait-OnError {
    param([int]$Code)
    if ($Code -ne 0) {
        Write-Host ""
        Write-Host "Installation failed (exit code $Code)." -ForegroundColor Red
        try {
            Write-Host "Press any key to close..." -ForegroundColor Yellow
            $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
        } catch {
            # Non-interactive host (e.g., piped via iex) - just pause briefly
            Start-Sleep -Seconds 1
        }
    }
}

function Get-LatestPrereleaseTag {
    $api = "https://api.github.com/repos/freeman412/mineos-sveltekit/releases"
    $releases = Invoke-RestMethod -Uri $api -UseBasicParsing
    $prerelease = $releases | Where-Object { $_.prerelease -eq $true } | Select-Object -First 1
    return $prerelease.tag_name
}

function Get-LatestBundleUrl {
    param(
        [string]$AssetName,
        [string]$Version = ""
    )
    if ([string]::IsNullOrWhiteSpace($Version)) {
        $api = "https://api.github.com/repos/freeman412/mineos-sveltekit/releases/latest"
    } else {
        $api = "https://api.github.com/repos/freeman412/mineos-sveltekit/releases/tags/$Version"
    }
    $release = Invoke-RestMethod -Uri $api -UseBasicParsing
    $asset = $release.assets | Where-Object { $_.name -eq $AssetName } | Select-Object -First 1
    return $asset.browser_download_url
}

function Install-Cli {
    param(
        [string]$InstallDir,
        [string]$TmpDir,
        [string]$CliUrl,
        [string]$Version,
        [switch]$NoCli
    )

    if ($NoCli) {
        return $null
    }

    $arch = Get-PlatformArch
    if ([string]::IsNullOrWhiteSpace($arch)) {
        Write-Info "Unsupported CPU architecture for mineos-cli. Skipping."
        return $null
    }

    $assetName = "mineos-cli_windows_$arch.zip"
    $bundleZip = Join-Path $InstallDir ("cli\" + $assetName)
    $cliZip = Join-Path $TmpDir "mineos-cli.zip"

    if (Test-Path $bundleZip) {
        Write-Info "Using bundled mineos-cli (windows/$arch)..."
        Copy-Item $bundleZip $cliZip -Force
    } else {
        if ([string]::IsNullOrWhiteSpace($CliUrl)) {
            $CliUrl = Get-LatestBundleUrl -AssetName $assetName -Version $Version
        }

        if ([string]::IsNullOrWhiteSpace($CliUrl)) {
            Write-Info "Unable to locate mineos-cli asset for windows/$arch. Skipping."
            return $null
        }

        Write-Info "Downloading mineos-cli (windows/$arch)..."
        Invoke-WebRequest -Uri $CliUrl -OutFile $cliZip -UseBasicParsing
    }

    $cliExtract = Join-Path $TmpDir "cli"
    New-Item -ItemType Directory -Force -Path $cliExtract | Out-Null
    Expand-Archive -Path $cliZip -DestinationPath $cliExtract -Force

    $binName = "mineos-windows-$arch.exe"
    $binPath = Join-Path $cliExtract $binName
    if (Test-Path $binPath) {
        $dest = Join-Path $InstallDir "mineos.exe"
        Copy-Item $binPath $dest -Force
        Write-Info "Installed mineos-cli to $dest"
        return $dest
    }

    Write-Info "mineos-cli binary not found in archive. Skipping."
    return $null
}

function Resolve-CliPath {
    param([string]$InstallDir)
    $local = Join-Path $InstallDir "mineos.exe"
    if (Test-Path $local) {
        return $local
    }
    $cmd = Get-Command mineos -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Path
    }
    return $null
}

function Get-PlatformArch {
    $arch = ""
    $archObj = $null
    try {
        $archObj = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
    } catch {
        $archObj = $null
    }
    if ($archObj) {
        $arch = $archObj.ToString()
    }
    if ([string]::IsNullOrWhiteSpace($arch)) {
        $arch = $env:PROCESSOR_ARCHITECTURE
    }
    if ([string]::IsNullOrWhiteSpace($arch) -and $env:PROCESSOR_ARCHITEW6432) {
        $arch = $env:PROCESSOR_ARCHITEW6432
    }
    switch ($arch) {
        "X64" { return "amd64" }
        "AMD64" { return "amd64" }
        "Arm64" { return "arm64" }
        "ARM64" { return "arm64" }
        default { return "" }
    }
}

if ([string]::IsNullOrWhiteSpace($InstallDir)) {
    Write-Error-Custom "Install directory is required."
    return
}

$forwardArgs = @()
if ($ForwardArgs) { $forwardArgs += $ForwardArgs }

if ($Build) {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        Write-Error-Custom "git is required for -Build."
        return
    }

    if (Test-Path (Join-Path $InstallDir ".git")) {
        Write-Info "Using existing repo at $InstallDir"
    } elseif (Test-Path $InstallDir -and (Get-ChildItem -Path $InstallDir -Force | Measure-Object).Count -gt 0) {
        Write-Error-Custom "Directory $InstallDir exists and is not empty."
        return
    } else {
        Write-Info "Cloning repo..."
        git clone --depth 1 --branch $Ref $RepoUrl $InstallDir
    }

    $tmpDir = Join-Path $env:TEMP ("mineos-" + [Guid]::NewGuid().ToString("n"))
    New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
    Install-Cli -InstallDir $InstallDir -TmpDir $tmpDir -CliUrl $CliUrl -Version $Version -NoCli:$NoCli | Out-Null
    $cliPath = Resolve-CliPath -InstallDir $InstallDir
    if (-not $cliPath) {
        Write-Error-Custom "mineos-cli not found. Re-run without -NoCli."
        return
    }
    $forwardArgs = @("install", "--build") + $forwardArgs
    Push-Location $InstallDir
    try {
        & ".\mineos.exe" @forwardArgs
    } finally {
        Pop-Location
    }
    Wait-OnError -Code $LASTEXITCODE
    return
}

# Resolve -Preview to the latest prerelease tag
if ($Preview -and [string]::IsNullOrWhiteSpace($Version)) {
    Write-Info "Looking up latest preview release..."
    $Version = Get-LatestPrereleaseTag
    if ([string]::IsNullOrWhiteSpace($Version)) {
        Write-Error-Custom "No pre-release version found."
        return
    }
    Write-Info "Found preview version: $Version"
    Write-Host ""
    Write-Host "[WARN] Preview versions may be unstable, contain bugs, or cause data loss." -ForegroundColor Yellow
    Write-Host "[WARN] Do not use preview releases in production. Back up your data first." -ForegroundColor Yellow
    Write-Host ""
    $forwardArgs = @("--image-tag", "preview") + $forwardArgs
}

if ([string]::IsNullOrWhiteSpace($BundleUrl)) {
    $BundleUrl = Get-LatestBundleUrl -AssetName "mineos-install-bundle.zip" -Version $Version
}

if ([string]::IsNullOrWhiteSpace($BundleUrl)) {
    Write-Error-Custom "Unable to locate install bundle URL."
    return
}

$tmpDir = Join-Path $env:TEMP ("mineos-" + [Guid]::NewGuid().ToString("n"))
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$zipPath = Join-Path $tmpDir "mineos-install-bundle.zip"

Write-Info "Downloading install bundle..."
Invoke-WebRequest -Uri $BundleUrl -OutFile $zipPath -UseBasicParsing

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Expand-Archive -Path $zipPath -DestinationPath $InstallDir -Force

try {
    Install-Cli -InstallDir $InstallDir -TmpDir $tmpDir -CliUrl $CliUrl -Version $Version -NoCli:$NoCli | Out-Null
} catch {
    Write-Info "mineos-cli install failed: $($_.Exception.Message)"
}

$cliPath = Resolve-CliPath -InstallDir $InstallDir
if (-not $cliPath) {
    Write-Error-Custom "mineos-cli not found. Re-run without -NoCli."
    return
}

$forwardArgs = @("install") + $forwardArgs
Push-Location $InstallDir
try {
    & ".\mineos.exe" @forwardArgs
} finally {
    Pop-Location
}

Wait-OnError -Code $LASTEXITCODE
