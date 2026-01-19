# MineOS Install & Management Script for Windows
# Interactive setup using PowerShell

$ErrorActionPreference = "Stop"

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

# Detect Docker Compose
function Set-ComposeCommand {
    if (Get-Command docker -ErrorAction SilentlyContinue) {
        $oldEap = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        try {
            $outLines = & docker compose version 2>&1
            $outText = ($outLines | Out-String).Trim()
        } finally {
            $ErrorActionPreference = $oldEap
        }
        if ($outText -match "Docker Compose version" -or $outText -match '\d+\.\d+\.\d+') {
            $script:composeExe = "docker"
            $script:composeBaseArgs = @("compose")
            $script:composeCmdText = "docker compose"
            return
        }
    }

    if (Get-Command docker-compose -ErrorAction SilentlyContinue) {
        $oldEap = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        try {
            $outLines = & docker-compose version 2>&1
            $outText = ($outLines | Out-String).Trim()
        } finally {
            $ErrorActionPreference = $oldEap
        }
        if ($outText -match "Docker Compose version" -or $outText -match '\d+\.\d+\.\d+') {
            $script:composeExe = "docker-compose"
            $script:composeBaseArgs = @()
            $script:composeCmdText = "docker-compose"
            return
        }
    }

    throw "Docker Compose not found."
}

function Invoke-Compose {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Args,
        [switch]$StreamOutput
    )

    if (-not $script:composeExe) { Set-ComposeCommand }

    $allArgs = @()
    if ($script:composeBaseArgs) { $allArgs += $script:composeBaseArgs }
    if ($Args) { $allArgs += $Args }

    $argString = ($allArgs | ForEach-Object {
        if ($_ -match '\s') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ }
    }) -join ' '

    $outFile = New-TemporaryFile
    $errFile = New-TemporaryFile

    try {
        $proc = Start-Process -FilePath $script:composeExe `
            -ArgumentList $argString `
            -NoNewWindow `
            -PassThru `
            -RedirectStandardOutput $outFile `
            -RedirectStandardError $errFile
        if ($StreamOutput) {
            $outOffset = 0L
            $errOffset = 0L
            while (-not $proc.HasExited) {
                Start-Sleep -Milliseconds 200
                $outOffset = Write-NewFileContent -Path $outFile -Offset $outOffset
                $errOffset = Write-NewFileContent -Path $errFile -Offset $errOffset
            }
            $outOffset = Write-NewFileContent -Path $outFile -Offset $outOffset
            $errOffset = Write-NewFileContent -Path $errFile -Offset $errOffset
        }
        $proc.WaitForExit()

        $out = ""
        if (Test-Path $outFile) { $out = Get-Content $outFile -Raw }
        $err = ""
        if (Test-Path $errFile) { $err = Get-Content $errFile -Raw }

        return [pscustomobject]@{ Output = ($out + $err); ExitCode = $proc.ExitCode }
    } finally {
        Remove-Item $outFile, $errFile -ErrorAction SilentlyContinue
    }
}

function Write-NewFileContent {
    param([string]$Path, [long]$Offset)
    if (-not (Test-Path $Path)) { return $Offset }
    $fs = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    try {
        if ($Offset -gt $fs.Length) { $Offset = $fs.Length }
        [void]$fs.Seek($Offset, [System.IO.SeekOrigin]::Begin)
        $reader = New-Object System.IO.StreamReader($fs)
        try {
            $text = $reader.ReadToEnd()
            $newOffset = $fs.Position
        } finally {
            $reader.Dispose()
        }
    } finally {
        $fs.Dispose()
    }
    if (-not [string]::IsNullOrEmpty($text)) { Write-Host -NoNewline $text }
    return $newOffset
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
    $script:apiKey = Get-EnvValue "ApiKey__SeedKey"
    $script:apiPort = Get-EnvValue "API_PORT"
    if ([string]::IsNullOrWhiteSpace($script:apiPort)) { $script:apiPort = "5078" }
    $script:webPort = Get-EnvValue "WEB_PORT"
    if ([string]::IsNullOrWhiteSpace($script:webPort)) { $script:webPort = "3000" }
    $script:mcPortRange = Get-EnvValue "MC_PORT_RANGE"
    if ([string]::IsNullOrWhiteSpace($script:mcPortRange)) { $script:mcPortRange = "25565-25570" }
    $script:baseDir = Get-EnvValue "HOST_BASE_DIRECTORY"
    if ([string]::IsNullOrWhiteSpace($script:baseDir)) { $script:baseDir = Get-EnvValue "Host__BaseDirectory" }
    if ([string]::IsNullOrWhiteSpace($script:baseDir)) { $script:baseDir = ".\\minecraft" }
    $script:dataDir = Get-EnvValue "Data__Directory"
    if ([string]::IsNullOrWhiteSpace($script:dataDir)) { $script:dataDir = ".\\data" }
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
    if (-not ($Value -match '^(\d+)\-(\d+)$')) { throw "$Name must be like 25565-25570." }
    $start = [int]$Matches[1]
    $end   = [int]$Matches[2]
    if ($start -lt 1 -or $start -gt 65535 -or $end -lt 1 -or $end -gt 65535) { throw "$Name ports must be between 1 and 65535." }
    if ($end -lt $start) { throw "$Name end port must be >= start port." }
    return "$start-$end"
}

function Test-Dependencies {
    Write-Info "Checking dependencies..."
    $allOk = $true

    if (Test-CommandExists "docker") {
        $dockerOut = docker --version 2>&1
        $dockerVersion = ([regex]'\d+\.\d+\.\d+').Match([string]$dockerOut).Value
        Write-Success "Docker $dockerVersion"
    } else {
        Write-Error-Custom "Docker is not installed"
        Write-Info "Please install Docker Desktop: https://docs.docker.com/desktop/install/windows-install/"
        $allOk = $false
    }

    try {
        Set-ComposeCommand
        Write-Success "Docker Compose found"
    } catch {
        Write-Error-Custom "Docker Compose is not installed"
        $allOk = $false
    }

    if (-not $allOk) {
        Write-Error-Custom "Required dependencies are missing."
        exit 1
    }
}

function Get-ServiceStatus {
    $r = Invoke-Compose -Args @("ps", "--format", "json")
    if ($r.ExitCode -ne 0) { return @() }
    try {
        $services = $r.Output | ConvertFrom-Json
        return $services
    } catch {
        return @()
    }
}

function Show-Status {
    Write-Header "Service Status"

    if (-not (Test-Path ".env")) {
        Write-Warn "MineOS is not installed. Run fresh install first."
        return
    }

    Load-ExistingConfig
    $r = Invoke-Compose -Args @("ps")
    Write-Host $r.Output

    Write-Host ""
    Write-Host "URLs (when running):"
    Write-Host "  Web UI:   http://localhost:$webPort" -ForegroundColor Cyan
    Write-Host "  API:      http://localhost:$apiPort" -ForegroundColor Cyan
}

function Start-ConfigWizard {
    Write-Header "Configuration Wizard"

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

    $script:baseDir = Read-Host "Local storage directory for Minecraft servers (relative, default: .\\minecraft)"
    if ([string]::IsNullOrWhiteSpace($script:baseDir)) { $script:baseDir = ".\\minecraft" }

    $script:dataDir = Read-Host "Database directory (relative, default: .\\data)"
    if ([string]::IsNullOrWhiteSpace($script:dataDir)) { $script:dataDir = ".\\data" }

    $script:apiPort = Read-Host "API port (default: 5078)"
    if ([string]::IsNullOrWhiteSpace($script:apiPort)) { $script:apiPort = "5078" }
    [void](Assert-PortNumber -Value $script:apiPort -Name "API port")

    $script:webPort = Read-Host "Web UI port (default: 3000)"
    if ([string]::IsNullOrWhiteSpace($script:webPort)) { $script:webPort = "3000" }
    [void](Assert-PortNumber -Value $script:webPort -Name "Web UI port")

    $script:mcPortRange = Read-Host "Minecraft server port range (default: 25565-25570)"
    if ([string]::IsNullOrWhiteSpace($script:mcPortRange)) { $script:mcPortRange = "25565-25570" }
    $script:mcPortRange = Assert-PortRange -Value $script:mcPortRange -Name "Minecraft port range"

    $script:mcExtraPorts = ""
    $script:curseforgeKey = Read-Host "CurseForge API key (optional, press Enter to skip)"
    $script:discordWebhook = Read-Host "Discord webhook URL (optional, press Enter to skip)"

    $script:jwtSecret = -join ((65..90) + (97..122) + (48..57) | Get-Random -Count 32 | ForEach-Object { [char]$_ })
    $script:apiKey = -join ((65..90) + (97..122) + (48..57) | Get-Random -Count 32 | ForEach-Object { [char]$_ })
}

function New-EnvFile {
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
HOST_BASE_DIRECTORY=$($baseDir -replace '\\', '/')
Host__BaseDirectory=/var/games/minecraft
Data__Directory=$($dataDir -replace '\\', '/')
Host__ServersPathSegment=servers
Host__ProfilesPathSegment=profiles
Host__BackupsPathSegment=backups
Host__ArchivesPathSegment=archives
Host__ImportsPathSegment=imports
Host__OwnerUid=1000
Host__OwnerGid=1000

# Optional Integrations
$(if ($curseforgeKey) { "CurseForge__ApiKey=$curseforgeKey" } else { "# CurseForge__ApiKey=" })
$(if ($discordWebhook) { "Discord__WebhookUrl=$discordWebhook" } else { "# Discord__WebhookUrl=" })

# Ports
API_PORT=$apiPort
WEB_PORT=$webPort
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
    New-Item -ItemType Directory -Force -Path "$baseDir\servers" | Out-Null
    New-Item -ItemType Directory -Force -Path "$baseDir\profiles" | Out-Null
    New-Item -ItemType Directory -Force -Path "$baseDir\backups" | Out-Null
    New-Item -ItemType Directory -Force -Path "$baseDir\archives" | Out-Null
    New-Item -ItemType Directory -Force -Path "$baseDir\imports" | Out-Null
    New-Item -ItemType Directory -Force -Path "$dataDir" | Out-Null
    Write-Success "Created directories"
}

function Start-Services {
    param([switch]$Rebuild)

    Write-Info "Starting services..."

    if ($Rebuild) {
        Write-Info "Building images (this may take a few minutes)..."
        $build = Invoke-Compose -Args @("build", "--progress", "plain") -StreamOutput
        if ($build.ExitCode -ne 0) {
            Write-Error-Custom "Build failed"
            exit 1
        }
        $r = Invoke-Compose -Args @("up", "-d", "--force-recreate")
    } else {
        $r = Invoke-Compose -Args @("up", "-d")
    }

    if ($r.ExitCode -ne 0) {
        Write-Error-Custom "Failed to start services"
        Write-Host $r.Output
        exit 1
    }

    Write-Info "Waiting for services to be ready..."
    Start-Sleep -Seconds 5

    Write-Success "Services started!"
}

function Stop-Services {
    Write-Info "Stopping services..."
    $r = Invoke-Compose -Args @("down")
    if ($r.ExitCode -eq 0) {
        Write-Success "Services stopped"
    } else {
        Write-Error-Custom "Failed to stop services"
        Write-Host $r.Output
    }
}

function Restart-Services {
    Write-Info "Restarting services..."
    $r = Invoke-Compose -Args @("restart")
    if ($r.ExitCode -eq 0) {
        Write-Success "Services restarted"
    } else {
        Write-Error-Custom "Failed to restart services"
    }
}

function Show-Logs {
    Write-Info "Showing logs (Ctrl+C to exit)..."
    & $script:composeExe @script:composeBaseArgs "logs" "-f" "--tail" "100"
}

function Do-FreshInstall {
    Write-Header "Fresh Install"

    if (Test-Path ".env") {
        Write-Warn "Existing installation detected!"
        $confirm = Read-Host "This will OVERWRITE your configuration. Continue? (y/N)"
        if ($confirm -ne "y" -and $confirm -ne "Y") {
            Write-Info "Cancelled"
            return
        }
    }

    Test-Dependencies
    Start-ConfigWizard
    New-EnvFile
    New-Directories
    Start-Services -Rebuild

    Write-Header "Installation Complete!"
    Write-Host ""
    Write-Host "Web UI:    http://localhost:$webPort" -ForegroundColor Green
    Write-Host "Username:  $adminUser" -ForegroundColor Cyan
    Write-Host "Password:  $adminPass" -ForegroundColor Cyan
    Write-Host ""
}

function Do-Rebuild {
    Write-Header "Rebuild"

    if (-not (Test-Path ".env")) {
        Write-Error-Custom "No installation found. Run fresh install first."
        return
    }

    Write-Info "This will rebuild the Docker images and restart services."
    Write-Info "Your configuration and data will be preserved."
    $confirm = Read-Host "Continue? (Y/n)"
    if ($confirm -eq "n" -or $confirm -eq "N") {
        Write-Info "Cancelled"
        return
    }

    Load-ExistingConfig
    Start-Services -Rebuild

    Write-Success "Rebuild complete!"
    Write-Host "Web UI: http://localhost:$webPort" -ForegroundColor Green
}

function Do-Update {
    Write-Header "Update"

    if (-not (Test-Path ".env")) {
        Write-Error-Custom "No installation found. Run fresh install first."
        return
    }

    Write-Info "Pulling latest code and rebuilding..."

    # Pull latest if in git repo
    if (Test-Path ".git") {
        Write-Info "Pulling latest changes..."
        git pull
    }

    Load-ExistingConfig
    Start-Services -Rebuild

    Write-Success "Update complete!"
    Write-Host "Web UI: http://localhost:$webPort" -ForegroundColor Green
}

function Show-Menu {
    Clear-Host
    Write-Host ""
    Write-Host "  __  __ _            ___  ____  " -ForegroundColor Green
    Write-Host " |  \/  (_)_ __   ___|_ _|/ ___| " -ForegroundColor Green
    Write-Host " | |\/| | | '_ \ / _ \| | \___ \ " -ForegroundColor Green
    Write-Host " | |  | | | | | |  __/| |  ___) |" -ForegroundColor Green
    Write-Host " |_|  |_|_|_| |_|\___|___||____/ " -ForegroundColor Green
    Write-Host ""
    Write-Host " Minecraft Server Management" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "================================" -ForegroundColor DarkGray

    $installed = Test-Path ".env"
    if ($installed) {
        Load-ExistingConfig
        Write-Host " Status: " -NoNewline
        Write-Host "Installed" -ForegroundColor Green
    } else {
        Write-Host " Status: " -NoNewline
        Write-Host "Not Installed" -ForegroundColor Yellow
    }

    Write-Host "================================" -ForegroundColor DarkGray
    Write-Host ""

    if (-not $installed) {
        Write-Host " [1] Fresh Install" -ForegroundColor White
        Write-Host ""
        Write-Host " [Q] Quit" -ForegroundColor DarkGray
    } else {
        Write-Host " [1] Start Services" -ForegroundColor White
        Write-Host " [2] Stop Services" -ForegroundColor White
        Write-Host " [3] Restart Services" -ForegroundColor White
        Write-Host " [4] View Logs" -ForegroundColor White
        Write-Host " [5] Show Status" -ForegroundColor White
        Write-Host ""
        Write-Host " [6] Rebuild (keep config)" -ForegroundColor Yellow
        Write-Host " [7] Update (git pull + rebuild)" -ForegroundColor Yellow
        Write-Host " [8] Fresh Install (reset everything)" -ForegroundColor Red
        Write-Host ""
        Write-Host " [Q] Quit" -ForegroundColor DarkGray
    }

    Write-Host ""
    $choice = Read-Host "Select option"
    return $choice
}

function Main {
    Test-Dependencies

    while ($true) {
        $choice = Show-Menu
        $installed = Test-Path ".env"

        if (-not $installed) {
            switch ($choice.ToUpper()) {
                "1" { Do-FreshInstall; Read-Host "Press Enter to continue" }
                "Q" { Write-Host "Goodbye!"; exit 0 }
                default { Write-Warn "Invalid option" }
            }
        } else {
            switch ($choice.ToUpper()) {
                "1" { Load-ExistingConfig; Start-Services; Read-Host "Press Enter to continue" }
                "2" { Stop-Services; Read-Host "Press Enter to continue" }
                "3" { Restart-Services; Read-Host "Press Enter to continue" }
                "4" { Show-Logs }
                "5" { Show-Status; Read-Host "Press Enter to continue" }
                "6" { Do-Rebuild; Read-Host "Press Enter to continue" }
                "7" { Do-Update; Read-Host "Press Enter to continue" }
                "8" { Do-FreshInstall; Read-Host "Press Enter to continue" }
                "Q" { Write-Host "Goodbye!"; exit 0 }
                default { Write-Warn "Invalid option" }
            }
        }
    }
}

Main
