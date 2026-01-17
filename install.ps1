# MineOS Install Script for Windows
# Interactive setup using PowerShell
#
# Fixes:
# - Reliable docker compose invocation (v2 + v1 fallback)
# - Configurable Minecraft port range during setup
# - Startup wait is bounded + reports failing container logs
# - Avoids PowerShell NativeCommandError crashes from docker output/warnings
#
# IMPORTANT:
# PowerShell 7+ can treat native stderr as an error record that respects $ErrorActionPreference.
# We disable that with $PSNativeCommandUseErrorActionPreference = $false, and we run compose
# via Start-Process to avoid NativeCommandError in Windows PowerShell.

$ErrorActionPreference = "Stop"

# Disable native-command stderr -> PowerShell error record behavior (prevents NativeCommandError explosions)
if ($PSVersionTable.PSVersion.Major -ge 7) {
    $global:PSNativeCommandUseErrorActionPreference = $false
}

# Colors
function Write-Info { Write-Host "[INFO] $($args -join ' ')" -ForegroundColor Cyan }
function Write-Success { Write-Host "[OK] $($args -join ' ')" -ForegroundColor Green }
function Write-Warn { Write-Host "[WARN] $($args -join ' ')" -ForegroundColor Yellow }
function Write-Error-Custom { Write-Host "[ERR] $($args -join ' ')" -ForegroundColor Red }

function Write-Header {
    param([string]$Message)
    Write-Host ""
    Write-Host "================================" -ForegroundColor Green
    Write-Host $Message -ForegroundColor Green
    Write-Host "================================" -ForegroundColor Green
    Write-Host ""
}

function Test-CommandExists {
    param([string]$Command)
    return $null -ne (Get-Command $Command -ErrorAction SilentlyContinue)
}

# Detect Docker Compose (v2 preferred, v1 fallback)
function Set-ComposeCommand {
    if (Get-Command docker -ErrorAction SilentlyContinue) {
        & docker compose version *> $null
        if ($LASTEXITCODE -eq 0) {
            $script:composeExe = "docker"
            $script:composeBaseArgs = @("compose")
            $script:composeCmdText = "docker compose"
            return
        }
    }

    if (Get-Command docker-compose -ErrorAction SilentlyContinue) {
        & docker-compose version *> $null
        if ($LASTEXITCODE -eq 0) {
            $script:composeExe = "docker-compose"
            $script:composeBaseArgs = @()
            $script:composeCmdText = "docker-compose"
            return
        }
    }

    throw "Docker Compose not found (or not usable from this shell)."
}

# Invoke compose safely:
# - Always captures stdout+stderr as text to avoid PowerShell interpreting stderr as an error record
# - Returns { Output, ExitCode }
function Invoke-Compose {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Args
    )

    if (-not $script:composeExe) { Set-ComposeCommand }

    $allArgs = @()
    if ($script:composeBaseArgs) { $allArgs += $script:composeBaseArgs }
    if ($Args) { $allArgs += $Args }

    $outFile = New-TemporaryFile
    $errFile = New-TemporaryFile

    try {
        $proc = Start-Process -FilePath $script:composeExe `
            -ArgumentList $allArgs `
            -NoNewWindow `
            -Wait `
            -PassThru `
            -RedirectStandardOutput $outFile `
            -RedirectStandardError $errFile

        $out = ""
        if (Test-Path $outFile) { $out = Get-Content $outFile -Raw }
        $err = ""
        if (Test-Path $errFile) { $err = Get-Content $errFile -Raw }

        return [pscustomobject]@{ Output = ($out + $err); ExitCode = $proc.ExitCode }
    } finally {
        Remove-Item $outFile, $errFile -ErrorAction SilentlyContinue
    }
}

function Invoke-ComposeOrThrow {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Args,
        [string]$What = "compose command"
    )

    $r = Invoke-Compose -Args $Args
    if ($r.ExitCode -ne 0) {
        throw "Failed $What (exit $($r.ExitCode)). Output:`n$($r.Output)"
    }
    return $r
}

function Get-ComposeVersion {
    $r = Invoke-ComposeOrThrow -Args @("version") -What "getting compose version"
    $m = [regex]'\d+\.\d+\.\d+'
    $ver = $m.Match([string]$r.Output).Value
    if ([string]::IsNullOrWhiteSpace($ver)) { return ([string]$r.Output).Trim() }
    return $ver
}

function Get-EnvValue {
    param([string]$Key)
    if (-not (Test-Path ".env")) { return $null }
    $pattern = "^{0}=" -f [regex]::Escape($Key)
    $line = Get-Content ".env" | Where-Object { $_ -match $pattern } | Select-Object -First 1
    if (-not $line) { return $null }
    $parts = $line -split '=', 2
    if ($parts.Length -eq 2) { return $parts[1] }
    return $null
}

function Load-ExistingConfig {
    $script:adminUser = Get-EnvValue "Auth__SeedUsername"
    if ([string]::IsNullOrWhiteSpace($script:adminUser)) { $script:adminUser = "admin" }

    $script:adminPass = Get-EnvValue "Auth__SeedPassword"
    if ([string]::IsNullOrWhiteSpace($script:adminPass)) { $script:adminPass = "" }

    $script:apiKey = Get-EnvValue "ApiKey__SeedKey"
    if ([string]::IsNullOrWhiteSpace($script:apiKey)) { $script:apiKey = "" }

    $script:apiPort = Get-EnvValue "API_PORT"
    if ([string]::IsNullOrWhiteSpace($script:apiPort)) { $script:apiPort = "5078" }

    $script:webPort = Get-EnvValue "WEB_PORT"
    if ([string]::IsNullOrWhiteSpace($script:webPort)) { $script:webPort = "3000" }

    $script:mcPortRange = Get-EnvValue "MC_PORT_RANGE"
    if ([string]::IsNullOrWhiteSpace($script:mcPortRange)) { $script:mcPortRange = "25565-25570" }

    $script:mcExtraPorts = Get-EnvValue "MC_EXTRA_PORTS"
    if ([string]::IsNullOrWhiteSpace($script:mcExtraPorts)) { $script:mcExtraPorts = "" }
}

function Assert-PortNumber {
    param([string]$Value, [string]$Name)
    if (-not ($Value -match '^\d+$')) { throw "$Name must be a number." }
    $n = [int]$Value
    if ($n -lt 1 -or $n -gt 65535) { throw "$Name must be between 1 and 65535." }
    return $n
}

function Assert-PortRange {
    param([string]$Value, [string]$Name)

    if ($Value -match '^\d+$') {
        [void](Assert-PortNumber -Value $Value -Name $Name)
        return $Value
    }

    if (-not ($Value -match '^(\d+)\-(\d+)$')) { throw "$Name must be like 25565-25570 (or a single port like 25565)." }

    $start = [int]$Matches[1]
    $end   = [int]$Matches[2]
    if ($start -lt 1 -or $start -gt 65535 -or $end -lt 1 -or $end -gt 65535) { throw "$Name ports must be between 1 and 65535." }
    if ($end -lt $start) { throw "$Name end port must be >= start port." }
    return "$start-$end"
}

function Test-Dependencies {
    Write-Header "Checking Dependencies"
    $allOk = $true

    if (Test-CommandExists "docker") {
        $dockerOut = docker --version 2>&1
        $dockerVersion = ([regex]'\d+\.\d+\.\d+').Match([string]$dockerOut).Value
        if ([string]::IsNullOrWhiteSpace($dockerVersion)) { $dockerVersion = ([string]$dockerOut).Trim() }
        Write-Success "Docker is installed (version $dockerVersion)"
    } else {
        Write-Error-Custom "Docker is not installed"
        Write-Info "Please install Docker Desktop: https://docs.docker.com/desktop/install/windows-install/"
        $allOk = $false
    }

    try {
        Set-ComposeCommand
        $composeVersion = Get-ComposeVersion
        Write-Success "Docker Compose is installed (version $composeVersion)"
    } catch {
        Write-Error-Custom "Docker Compose is not installed"
        Write-Info "Please install Docker Desktop (includes Docker Compose)"
        $allOk = $false
    }

    if (Test-CommandExists "dotnet") {
        $dotnetVersion = (dotnet --version 2>&1).ToString().Trim()
        Write-Success ".NET SDK is installed (version $dotnetVersion) - optional"
    } else {
        Write-Warn ".NET SDK not found (optional, only needed for development)"
    }

    if (Test-CommandExists "node") {
        $nodeVersion = (node --version 2>&1).ToString().Trim()
        Write-Success "Node.js is installed (version $nodeVersion) - optional"
    } else {
        Write-Warn "Node.js not found (optional, only needed for development)"
    }

    if (-not $allOk) {
        Write-Error-Custom "Required dependencies are missing. Please install them and try again."
        exit 1
    }

    Write-Success "All required dependencies are installed!"
}

function Start-ConfigWizard {
    Write-Header "Configuration Wizard"
    Write-Info "This wizard will help you configure MineOS"
    Write-Host ""

    $script:dbType = "sqlite"
    $script:dbConnection = "Data Source=/app/data/mineos.db"

    $script:adminUser = Read-Host "Admin username (default: admin)"
    if ([string]::IsNullOrWhiteSpace($script:adminUser)) { $script:adminUser = "admin" }

    do {
        $adminPassSecure = Read-Host "Admin password" -AsSecureString
        $script:adminPass = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
            [Runtime.InteropServices.Marshal]::SecureStringToBSTR($adminPassSecure)
        )
        if ([string]::IsNullOrWhiteSpace($script:adminPass)) { Write-Error-Custom "Password cannot be empty" }
    } while ([string]::IsNullOrWhiteSpace($script:adminPass))

    $script:baseDir = Read-Host "Base directory for Minecraft servers (default: C:\minecraft)"
    if ([string]::IsNullOrWhiteSpace($script:baseDir)) { $script:baseDir = "C:\minecraft" }

    $script:apiPort = Read-Host "API port (default: 5078)"
    if ([string]::IsNullOrWhiteSpace($script:apiPort)) { $script:apiPort = "5078" }
    [void](Assert-PortNumber -Value $script:apiPort -Name "API port")

    $script:webPort = Read-Host "Web UI port (default: 3000)"
    if ([string]::IsNullOrWhiteSpace($script:webPort)) { $script:webPort = "3000" }
    [void](Assert-PortNumber -Value $script:webPort -Name "Web UI port")

    Write-Host ""
    $script:mcPortRange = Read-Host "Minecraft server port range to expose (default: 25565-25570)"
    if ([string]::IsNullOrWhiteSpace($script:mcPortRange)) { $script:mcPortRange = "25565-25570" }
    $script:mcPortRange = Assert-PortRange -Value $script:mcPortRange -Name "Minecraft port range"

    $script:mcExtraPorts = Read-Host "Extra Minecraft ports (comma-separated, optional; e.g. 25575,24454). Press Enter to skip"
    if (-not [string]::IsNullOrWhiteSpace($script:mcExtraPorts)) {
        if (-not ($script:mcExtraPorts -match '^\s*\d+(\s*,\s*\d+)*\s*$')) {
            throw "Extra ports must be comma-separated numbers, like 25575,24454"
        }
        $script:mcExtraPorts = ($script:mcExtraPorts -split '\s*,\s*' | ForEach-Object {
            [void](Assert-PortNumber -Value $_ -Name "Extra port")
            $_
        }) -join ","
    } else {
        $script:mcExtraPorts = ""
    }

    Write-Host ""
    $script:curseforgeKey = Read-Host "CurseForge API key (optional, press Enter to skip)"
    $script:discordWebhook = Read-Host "Discord webhook URL (optional, press Enter to skip)"

    $script:jwtSecret = -join ((65..90) + (97..122) + (48..57) | Get-Random -Count 32 | ForEach-Object { [char]$_ })
    $script:apiKey = -join ((65..90) + (97..122) + (48..57) | Get-Random -Count 32 | ForEach-Object { [char]$_ })
}

function New-EnvFile {
    Write-Header "Creating Environment File"

    $envContent = @"
# Database Configuration
DB_TYPE=$dbType
ConnectionStrings__DefaultConnection=$dbConnection

# Authentication
Auth__SeedUsername=$adminUser
Auth__SeedPassword=$adminPass
Auth__JwtSecret=$jwtSecret
Auth__JwtIssuer=mineos
Auth__JwtAudience=mineos
Auth__JwtExpiryHours=24

# API Configuration
ApiKey__SeedKey=$apiKey

# Host Configuration
Host__BaseDirectory=$($baseDir -replace '\\', '/')
Host__ServersPathSegment=servers
Host__ProfilesPathSegment=profiles
Host__BackupsPathSegment=backups
Host__ArchivesPathSegment=archives
Host__ImportsPathSegment=imports
Host__OwnerUid=1000
Host__OwnerGid=1000

# Optional: CurseForge Integration
$(if ($curseforgeKey) { "CurseForge__ApiKey=$curseforgeKey" } else { "# CurseForge__ApiKey=" })

# Optional: Discord Integration
$(if ($discordWebhook) { "Discord__WebhookUrl=$discordWebhook" } else { "# Discord__WebhookUrl=" })

# Ports
API_PORT=$apiPort
WEB_PORT=$webPort

# Minecraft server ports (published from the API container)
MC_PORT_RANGE=$mcPortRange
MC_EXTRA_PORTS=$mcExtraPorts

# Logging
Logging__LogLevel__Default=Information
Logging__LogLevel__Microsoft.AspNetCore=Warning
"@

    $envContent | Out-File -FilePath ".env" -Encoding utf8
    Write-Success "Created .env file"
}

function New-Directories {
    Write-Header "Creating Directories"

    New-Item -ItemType Directory -Force -Path "$baseDir\servers" | Out-Null
    New-Item -ItemType Directory -Force -Path "$baseDir\profiles" | Out-Null
    New-Item -ItemType Directory -Force -Path "$baseDir\backups" | Out-Null
    New-Item -ItemType Directory -Force -Path "$baseDir\archives" | Out-Null
    New-Item -ItemType Directory -Force -Path "$baseDir\imports" | Out-Null
    New-Item -ItemType Directory -Force -Path ".\data" | Out-Null

    Write-Success "Created required directories"
}

function Get-ComposeServices {
    $r = Invoke-ComposeOrThrow -Args @("config", "--services") -What "reading compose services"
    return ($r.Output -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function Get-ContainerIdForService {
    param([string]$Service)
    $r = Invoke-Compose -Args @("ps", "-q", $Service)
    if ($r.ExitCode -ne 0) { return "" }
    return $r.Output.ToString().Trim()
}

function Get-ContainerState {
    param([string]$ContainerId)
    if ([string]::IsNullOrWhiteSpace($ContainerId)) { return $null }

    $json = (docker inspect -f "{{json .State}}" $ContainerId 2>$null).ToString().Trim()
    if ([string]::IsNullOrWhiteSpace($json)) { return $null }

    try {
        $state = $json | ConvertFrom-Json
    } catch {
        return $null
    }

    $healthStatus = $null
    if ($state.PSObject.Properties.Name -contains "Health") {
        $healthStatus = $state.Health.Status
    }

    return [pscustomobject]@{
        Status   = $state.Status
        Health   = $healthStatus
        ExitCode = $state.ExitCode
    }
}

function Show-ServiceFailureDetails {
    param([string]$Service)

    Write-Error-Custom "Service '$Service' failed to become ready."
    $cid = Get-ContainerIdForService -Service $Service
    if ($cid) {
        $state = Get-ContainerState -ContainerId $cid
        if ($state) {
            Write-Info "Container state: status=$($state.Status) health=$($state.Health) exitCode=$($state.ExitCode)"
        }
        Write-Info "Last 200 log lines for '$Service':"
        [void](Invoke-Compose -Args @("logs", "--no-color", "--tail", "200", $Service))
    } else {
        Write-Info "No container id found for service '$Service'. Compose might not have created it."
        [void](Invoke-Compose -Args @("ps"))
    }
}

function Wait-For-ServicesReady {
    param(
        [int]$TimeoutSeconds = 120,
        [int]$PollSeconds = 2
    )

    $services = Get-ComposeServices
    if (-not $services -or $services.Count -eq 0) {
        Write-Warn "Could not determine services from compose config; skipping readiness wait."
        return $true
    }

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)

    while ((Get-Date) -lt $deadline) {
        $allReady = $true
        $badService = $null

        foreach ($svc in $services) {
            $cid = Get-ContainerIdForService -Service $svc
            $state = Get-ContainerState -ContainerId $cid

            if (-not $state) {
                $allReady = $false
                continue
            }

            if ($state.Status -eq "exited") {
                $badService = $svc
                $allReady = $false
                break
            }

            $hasHealth = ($state.Health -and $state.Health -ne "<no value>" -and $state.Health -ne "")
            if ($hasHealth) {
                if ($state.Health -eq "unhealthy") {
                    $badService = $svc
                    $allReady = $false
                    break
                }
                if ($state.Health -ne "healthy") {
                    $allReady = $false
                }
            } else {
                if ($state.Status -ne "running") {
                    $allReady = $false
                }
            }
        }

        if ($badService) {
            Show-ServiceFailureDetails -Service $badService
            return $false
        }

        if ($allReady) {
            return $true
        }

        Start-Sleep -Seconds $PollSeconds
    }

    Write-Error-Custom "Timed out after $TimeoutSeconds seconds waiting for services to start."
    foreach ($svc in (Get-ComposeServices)) {
        $cid = Get-ContainerIdForService -Service $svc
        $state = Get-ContainerState -ContainerId $cid
        if (-not $state) {
            Write-Warn "Service '$svc' has no container yet."
            continue
        }

        $hasHealth = ($state.Health -and $state.Health -ne "<no value>" -and $state.Health -ne "")
        $ready = if ($hasHealth) { $state.Health -eq "healthy" } else { $state.Status -eq "running" }

        if (-not $ready) {
            Show-ServiceFailureDetails -Service $svc
        }
    }

    return $false
}

function Start-Services {
    Write-Header "Starting Services"

    Write-Info "Starting Docker Compose services..."
    $r = Invoke-Compose -Args @("up", "-d")
    if ($r.ExitCode -ne 0) {
        Write-Error-Custom "Compose up failed (exit code $($r.ExitCode))."
        Write-Host $r.Output
        exit 1
    }

    Write-Info "Waiting for services to become ready (timeout: 120s)..."
    $ok = Wait-For-ServicesReady -TimeoutSeconds 120 -PollSeconds 2

    if (-not $ok) {
        Write-Error-Custom "One or more services failed to start."
        Write-Info "You can inspect full status with: $composeCmdText ps"
        Write-Info "And logs with: $composeCmdText logs -f"
        exit 1
    }

    Write-Success "Services started and look ready!"
}

function Show-Guide {
    Write-Header "Setup Complete!"

    Write-Host "MineOS is now running!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Access URLs:"
    Write-Host "  Web UI:    " -NoNewline
    Write-Host "http://localhost:$webPort" -ForegroundColor Cyan
    Write-Host "  API:       " -NoNewline
    Write-Host "http://localhost:$apiPort" -ForegroundColor Cyan
    Write-Host "  API Docs:  " -NoNewline
    Write-Host "http://localhost:$apiPort/swagger" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Minecraft ports exposed:"
    Write-Host "  Range:     $mcPortRange (TCP/UDP)" -ForegroundColor Cyan
    if (-not [string]::IsNullOrWhiteSpace($mcExtraPorts)) {
        Write-Host "  Extra:     $mcExtraPorts (note: only used if you wire them into docker-compose.yml)" -ForegroundColor Yellow
    }
    Write-Host ""
    Write-Host "Admin Credentials:"
    Write-Host "  Username:  $adminUser" -ForegroundColor Cyan
    Write-Host "  Password:  $adminPass" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "API Key (for programmatic access):"
    Write-Host "  $apiKey" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Useful Commands:"
    Write-Host "  View logs:        $composeCmdText logs -f"
    Write-Host "  Status:           $composeCmdText ps"
    Write-Host "  Stop services:    $composeCmdText down"
    Write-Host "  Restart services: $composeCmdText restart"
    Write-Host "  Update services:  $composeCmdText pull; $composeCmdText up -d"
    Write-Host ""
    Write-Success "Setup completed successfully!"
}

function Main {
    Clear-Host
    Write-Header "MineOS Setup"

    Write-Info "Welcome to MineOS! This script will help you get started."
    Write-Host ""

    Test-Dependencies

    if (Test-Path ".env") {
        Write-Warn ".env file already exists!"
        $reconfigure = Read-Host "Do you want to reconfigure? This will overwrite existing settings. (y/N)"
        if ($reconfigure -ne "y" -and $reconfigure -ne "Y") {
            Write-Info "Using existing configuration"
            Load-ExistingConfig
            Start-Services
            Show-Guide
            return
        }
    }

    Start-ConfigWizard
    New-EnvFile
    New-Directories
    Start-Services
    Show-Guide
}

Main
