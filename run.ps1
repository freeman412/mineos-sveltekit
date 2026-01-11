# MineOS Setup Script for Windows
# Interactive setup using PowerShell

$ErrorActionPreference = "Stop"

# Colors
function Write-Info { Write-Host "ℹ $args" -ForegroundColor Cyan }
function Write-Success { Write-Host "✓ $args" -ForegroundColor Green }
function Write-Warn { Write-Host "⚠ $args" -ForegroundColor Yellow }
function Write-Error-Custom { Write-Host "✗ $args" -ForegroundColor Red }

function Write-Header {
    param([string]$Message)
    Write-Host ""
    Write-Host "================================" -ForegroundColor Green
    Write-Host $Message -ForegroundColor Green
    Write-Host "================================" -ForegroundColor Green
    Write-Host ""
}

# Check if command exists
function Test-CommandExists {
    param([string]$Command)
    $null -ne (Get-Command $Command -ErrorAction SilentlyContinue)
}

# Check dependencies
function Test-Dependencies {
    Write-Header "Checking Dependencies"

    $allOk = $true

    # Check Docker
    if (Test-CommandExists "docker") {
        $dockerVersion = (docker --version | Select-String -Pattern '\d+\.\d+\.\d+').Matches.Value
        Write-Success "Docker is installed (version $dockerVersion)"
    } else {
        Write-Error-Custom "Docker is not installed"
        Write-Info "Please install Docker Desktop: https://docs.docker.com/desktop/install/windows-install/"
        $allOk = $false
    }

    # Check Docker Compose
    try {
        $composeVersion = (docker compose version | Select-String -Pattern '\d+\.\d+\.\d+').Matches.Value
        Write-Success "Docker Compose is installed (version $composeVersion)"
    } catch {
        Write-Error-Custom "Docker Compose is not installed"
        Write-Info "Please install Docker Desktop (includes Docker Compose)"
        $allOk = $false
    }

    # Optional: Check .NET SDK
    if (Test-CommandExists "dotnet") {
        $dotnetVersion = dotnet --version
        Write-Success ".NET SDK is installed (version $dotnetVersion) - optional"
    } else {
        Write-Warn ".NET SDK not found (optional, only needed for development)"
    }

    # Optional: Check Node.js
    if (Test-CommandExists "node") {
        $nodeVersion = node --version
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

# Configuration wizard
function Start-ConfigWizard {
    Write-Header "Configuration Wizard"

    Write-Info "This wizard will help you configure MineOS"
    Write-Host ""

    # Database type
    Write-Host "Select database type:"
    Write-Host "  1) SQLite (recommended for testing, single file)"
    Write-Host "  2) PostgreSQL (recommended for production)"
    Write-Host "  3) MySQL (alternative for production)"
    $dbChoice = Read-Host "Enter choice [1-3] (default: 1)"
    if ([string]::IsNullOrWhiteSpace($dbChoice)) { $dbChoice = "1" }

    switch ($dbChoice) {
        "1" {
            $script:dbType = "sqlite"
            $script:dbConnection = "Data Source=/app/data/mineos.db"
        }
        "2" {
            $script:dbType = "postgresql"
            $pgHost = Read-Host "PostgreSQL host (default: postgres)"
            if ([string]::IsNullOrWhiteSpace($pgHost)) { $pgHost = "postgres" }
            $pgPort = Read-Host "PostgreSQL port (default: 5432)"
            if ([string]::IsNullOrWhiteSpace($pgPort)) { $pgPort = "5432" }
            $pgDb = Read-Host "PostgreSQL database (default: mineos)"
            if ([string]::IsNullOrWhiteSpace($pgDb)) { $pgDb = "mineos" }
            $pgUser = Read-Host "PostgreSQL user (default: mineos)"
            if ([string]::IsNullOrWhiteSpace($pgUser)) { $pgUser = "mineos" }
            $pgPassSecure = Read-Host "PostgreSQL password" -AsSecureString
            $pgPass = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($pgPassSecure))
            $script:dbConnection = "Host=$pgHost;Port=$pgPort;Database=$pgDb;Username=$pgUser;Password=$pgPass"
        }
        "3" {
            $script:dbType = "mysql"
            $mysqlHost = Read-Host "MySQL host (default: mysql)"
            if ([string]::IsNullOrWhiteSpace($mysqlHost)) { $mysqlHost = "mysql" }
            $mysqlPort = Read-Host "MySQL port (default: 3306)"
            if ([string]::IsNullOrWhiteSpace($mysqlPort)) { $mysqlPort = "3306" }
            $mysqlDb = Read-Host "MySQL database (default: mineos)"
            if ([string]::IsNullOrWhiteSpace($mysqlDb)) { $mysqlDb = "mineos" }
            $mysqlUser = Read-Host "MySQL user (default: mineos)"
            if ([string]::IsNullOrWhiteSpace($mysqlUser)) { $mysqlUser = "mineos" }
            $mysqlPassSecure = Read-Host "MySQL password" -AsSecureString
            $mysqlPass = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($mysqlPassSecure))
            $script:dbConnection = "Server=$mysqlHost;Port=$mysqlPort;Database=$mysqlDb;Uid=$mysqlUser;Pwd=$mysqlPass"
        }
        default {
            Write-Error-Custom "Invalid choice"
            exit 1
        }
    }

    Write-Host ""

    # Admin credentials
    $script:adminUser = Read-Host "Admin username (default: admin)"
    if ([string]::IsNullOrWhiteSpace($script:adminUser)) { $script:adminUser = "admin" }

    $adminPassSecure = Read-Host "Admin password" -AsSecureString
    $script:adminPass = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($adminPassSecure))
    while ([string]::IsNullOrWhiteSpace($script:adminPass)) {
        Write-Error-Custom "Password cannot be empty"
        $adminPassSecure = Read-Host "Admin password" -AsSecureString
        $script:adminPass = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($adminPassSecure))
    }

    # Server directories
    $script:baseDir = Read-Host "Base directory for Minecraft servers (default: C:\minecraft)"
    if ([string]::IsNullOrWhiteSpace($script:baseDir)) { $script:baseDir = "C:\minecraft" }

    # Port configuration
    $script:apiPort = Read-Host "API port (default: 5078)"
    if ([string]::IsNullOrWhiteSpace($script:apiPort)) { $script:apiPort = "5078" }

    $script:webPort = Read-Host "Web UI port (default: 3000)"
    if ([string]::IsNullOrWhiteSpace($script:webPort)) { $script:webPort = "3000" }

    Write-Host ""

    # Optional: CurseForge API key
    $script:curseforgeKey = Read-Host "CurseForge API key (optional, press Enter to skip)"

    # Optional: Discord webhook
    $script:discordWebhook = Read-Host "Discord webhook URL (optional, press Enter to skip)"

    # Generate JWT secret
    $script:jwtSecret = -join ((65..90) + (97..122) + (48..57) | Get-Random -Count 32 | ForEach-Object { [char]$_ })

    # Generate API key
    $script:apiKey = -join ((65..90) + (97..122) + (48..57) | Get-Random -Count 32 | ForEach-Object { [char]$_ })
}

# Create environment file
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

# Logging
Logging__LogLevel__Default=Information
Logging__LogLevel__Microsoft.AspNetCore=Warning
"@

    $envContent | Out-File -FilePath ".env" -Encoding utf8

    Write-Success "Created .env file"
}

# Create required directories
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

# Start services
function Start-Services {
    Write-Header "Starting Services"

    Write-Info "Starting Docker Compose services..."

    docker compose up -d

    Write-Success "Services started!"

    # Wait for API to be ready
    Write-Info "Waiting for API to be ready..."
    $maxAttempts = 30
    $attempt = 0

    while ($attempt -lt $maxAttempts) {
        try {
            $response = Invoke-WebRequest -Uri "http://localhost:$apiPort/health" -UseBasicParsing -TimeoutSec 1 -ErrorAction SilentlyContinue
            if ($response.StatusCode -eq 200) {
                Write-Success "API is ready!"
                break
            }
        } catch {
            # Continue waiting
        }
        $attempt++
        Start-Sleep -Seconds 2
    }

    if ($attempt -eq $maxAttempts) {
        Write-Warn "API may not be ready yet. Check logs with: docker compose logs api"
    }
}

# Show first-run guide
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
    Write-Host "Admin Credentials:"
    Write-Host "  Username:  $adminUser" -ForegroundColor Cyan
    Write-Host "  Password:  $adminPass" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "API Key (for programmatic access):"
    Write-Host "  $apiKey" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Useful Commands:"
    Write-Host "  View logs:        docker compose logs -f"
    Write-Host "  Stop services:    docker compose down"
    Write-Host "  Restart services: docker compose restart"
    Write-Host "  Update services:  docker compose pull; docker compose up -d"
    Write-Host ""
    Write-Host "Documentation:"
    Write-Host "  Testing Guide:    .\QUICK-TEST-GUIDE.md"
    Write-Host "  Roadmap:          .\IMPLEMENTATION-ROADMAP.md"
    Write-Host ""
    Write-Success "Setup completed successfully!"
}

# Main execution
function Main {
    Clear-Host
    Write-Header "MineOS Setup"

    Write-Info "Welcome to MineOS! This script will help you get started."
    Write-Host ""

    # Check if .env already exists
    if (Test-Path ".env") {
        Write-Warn ".env file already exists!"
        $reconfigure = Read-Host "Do you want to reconfigure? This will overwrite existing settings. (y/N)"
        if ($reconfigure -ne "y" -and $reconfigure -ne "Y") {
            Write-Info "Using existing configuration"
            Start-Services
            Show-Guide
            return
        }
    }

    # Run setup steps
    Test-Dependencies
    Start-ConfigWizard
    New-EnvFile
    New-Directories
    Start-Services
    Show-Guide
}

# Run main function
Main
