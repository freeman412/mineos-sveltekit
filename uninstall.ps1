# MineOS Uninstall Script for Windows
# Removes containers and optionally data/volumes.

$ErrorActionPreference = "Stop"

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
    $null -ne (Get-Command $Command -ErrorAction SilentlyContinue)
}

function Set-ComposeCommand {
    if (Test-CommandExists "docker") {
        try {
            docker compose version | Out-Null
            $script:composeCmd = @("docker", "compose")
            $script:composeCmdText = "docker compose"
            return
        } catch {
        }
    }

    if (Test-CommandExists "docker-compose") {
        $script:composeCmd = @("docker-compose")
        $script:composeCmdText = "docker-compose"
        return
    }

    throw "Docker Compose not found"
}

function Compose-DownKeep {
    & $script:composeCmd down --remove-orphans
}

function Compose-DownRemove {
    & $script:composeCmd down --remove-orphans
}

function Backup-Data {
    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $backupRoot = Join-Path -Path (Get-Location) -ChildPath "backups\mineos-uninstall-$timestamp"
    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
    $backupRootResolved = (Resolve-Path $backupRoot).Path

    if (Test-Path ".\data") {
        Write-Info "Backing up local data folder..."
        Compress-Archive -Path ".\data\*" -DestinationPath (Join-Path $backupRootResolved "sqlite-data.zip") -Force
    }

    if (Test-Path ".env") {
        Copy-Item ".env" (Join-Path $backupRootResolved "env.backup")
    }

    Write-Success "Backup saved to $backupRootResolved"
}

function Confirm-Destructive {
    Write-Warn "This will permanently delete database files and local data."
    $confirmation = Read-Host "Type DELETE to continue"
    if ($confirmation -ne "DELETE") {
        Write-Info "Uninstall cancelled."
        exit 0
    }
}

function Remove-LocalData {
    if (Test-Path ".\data") {
        Remove-Item -Recurse -Force ".\data"
    }
}

function Main {
    Clear-Host
    Write-Header "MineOS Uninstall"

    if (-not (Test-CommandExists "docker")) {
        Write-Error-Custom "Docker is not installed."
        exit 1
    }

    try {
        Set-ComposeCommand
    } catch {
        Write-Error-Custom "Docker Compose is not available."
        exit 1
    }

    Write-Warn "This will stop and remove MineOS containers."
    Write-Warn "You can keep or remove database data in the next step."
    Write-Host ""
    Write-Host "Choose an uninstall option:"
    Write-Host "  1) Remove containers only (keep all data) [default]"
    Write-Host "  2) Backup data then remove containers and data"
    Write-Host "  3) Remove containers and data without backup"
    $choice = Read-Host "Enter choice [1-3]"
    if ([string]::IsNullOrWhiteSpace($choice)) { $choice = "1" }

    switch ($choice) {
        "1" {
            Compose-DownKeep
            Write-Success "Containers removed. Data preserved."
        }
        "2" {
            Confirm-Destructive
            Compose-DownKeep
            Backup-Data
            Compose-DownRemove
            Remove-LocalData
            Write-Success "Containers and data removed. Backup created."
        }
        "3" {
            Confirm-Destructive
            Compose-DownRemove
            Remove-LocalData
            Write-Success "Containers and data removed."
        }
        default {
            Write-Error-Custom "Invalid choice"
            exit 1
        }
    }

    Write-Info "If you want to remove Docker images too, run: docker image prune -a"
}

Main
