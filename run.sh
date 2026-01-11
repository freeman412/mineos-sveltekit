#!/bin/bash

# MineOS Setup Script
# Interactive setup for Linux/Mac

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Print colored messages
info() {
    echo -e "${BLUE}ℹ${NC} $1"
}

success() {
    echo -e "${GREEN}✓${NC} $1"
}

warn() {
    echo -e "${YELLOW}⚠${NC} $1"
}

error() {
    echo -e "${RED}✗${NC} $1"
}

header() {
    echo ""
    echo -e "${GREEN}================================${NC}"
    echo -e "${GREEN}$1${NC}"
    echo -e "${GREEN}================================${NC}"
    echo ""
}

# Check if command exists
command_exists() {
    command -v "$1" >/dev/null 2>&1
}

# Dependency checking
check_dependencies() {
    header "Checking Dependencies"

    local all_ok=true

    # Check Docker
    if command_exists docker; then
        local docker_version=$(docker --version | grep -oP '\d+\.\d+\.\d+' | head -1)
        success "Docker is installed (version $docker_version)"
    else
        error "Docker is not installed"
        info "Please install Docker: https://docs.docker.com/get-docker/"
        all_ok=false
    fi

    # Check Docker Compose
    if command_exists docker-compose || docker compose version >/dev/null 2>&1; then
        if command_exists docker-compose; then
            local compose_version=$(docker-compose --version | grep -oP '\d+\.\d+\.\d+' | head -1)
        else
            local compose_version=$(docker compose version | grep -oP '\d+\.\d+\.\d+' | head -1)
        fi
        success "Docker Compose is installed (version $compose_version)"
    else
        error "Docker Compose is not installed"
        info "Please install Docker Compose: https://docs.docker.com/compose/install/"
        all_ok=false
    fi

    # Optional: Check .NET SDK (for development)
    if command_exists dotnet; then
        local dotnet_version=$(dotnet --version)
        success ".NET SDK is installed (version $dotnet_version) - optional"
    else
        warn ".NET SDK not found (optional, only needed for development)"
    fi

    # Optional: Check Node.js (for development)
    if command_exists node; then
        local node_version=$(node --version)
        success "Node.js is installed (version $node_version) - optional"
    else
        warn "Node.js not found (optional, only needed for development)"
    fi

    if [ "$all_ok" = false ]; then
        error "Required dependencies are missing. Please install them and try again."
        exit 1
    fi

    success "All required dependencies are installed!"
}

# Configuration wizard
run_config_wizard() {
    header "Configuration Wizard"

    info "This wizard will help you configure MineOS"
    echo ""

    # Database type
    echo "Select database type:"
    echo "  1) SQLite (recommended for testing, single file)"
    echo "  2) PostgreSQL (recommended for production)"
    echo "  3) MySQL (alternative for production)"
    read -p "Enter choice [1-3] (default: 1): " db_choice
    db_choice=${db_choice:-1}

    case $db_choice in
        1)
            DB_TYPE="sqlite"
            DB_CONNECTION="Data Source=/app/data/mineos.db"
            ;;
        2)
            DB_TYPE="postgresql"
            read -p "PostgreSQL host (default: postgres): " pg_host
            pg_host=${pg_host:-postgres}
            read -p "PostgreSQL port (default: 5432): " pg_port
            pg_port=${pg_port:-5432}
            read -p "PostgreSQL database (default: mineos): " pg_db
            pg_db=${pg_db:-mineos}
            read -p "PostgreSQL user (default: mineos): " pg_user
            pg_user=${pg_user:-mineos}
            read -sp "PostgreSQL password: " pg_pass
            echo ""
            DB_CONNECTION="Host=$pg_host;Port=$pg_port;Database=$pg_db;Username=$pg_user;Password=$pg_pass"
            ;;
        3)
            DB_TYPE="mysql"
            read -p "MySQL host (default: mysql): " mysql_host
            mysql_host=${mysql_host:-mysql}
            read -p "MySQL port (default: 3306): " mysql_port
            mysql_port=${mysql_port:-3306}
            read -p "MySQL database (default: mineos): " mysql_db
            mysql_db=${mysql_db:-mineos}
            read -p "MySQL user (default: mineos): " mysql_user
            mysql_user=${mysql_user:-mineos}
            read -sp "MySQL password: " mysql_pass
            echo ""
            DB_CONNECTION="Server=$mysql_host;Port=$mysql_port;Database=$mysql_db;Uid=$mysql_user;Pwd=$mysql_pass"
            ;;
        *)
            error "Invalid choice"
            exit 1
            ;;
    esac

    echo ""

    # Admin credentials
    read -p "Admin username (default: admin): " admin_user
    admin_user=${admin_user:-admin}

    read -sp "Admin password: " admin_pass
    echo ""
    while [ -z "$admin_pass" ]; do
        error "Password cannot be empty"
        read -sp "Admin password: " admin_pass
        echo ""
    done

    # Server directories
    read -p "Base directory for Minecraft servers (default: /var/games/minecraft): " base_dir
    base_dir=${base_dir:-/var/games/minecraft}

    # Port configuration
    read -p "API port (default: 5078): " api_port
    api_port=${api_port:-5078}

    read -p "Web UI port (default: 3000): " web_port
    web_port=${web_port:-3000}

    echo ""

    # Optional: CurseForge API key
    read -p "CurseForge API key (optional, press Enter to skip): " curseforge_key

    # Optional: Discord webhook
    read -p "Discord webhook URL (optional, press Enter to skip): " discord_webhook

    # Generate JWT secret
    JWT_SECRET=$(openssl rand -base64 32 2>/dev/null || cat /dev/urandom | tr -dc 'a-zA-Z0-9' | fold -w 32 | head -n 1)

    # Generate API key
    API_KEY=$(openssl rand -base64 32 2>/dev/null || cat /dev/urandom | tr -dc 'a-zA-Z0-9' | fold -w 32 | head -n 1)
}

# Create environment file
create_env_file() {
    header "Creating Environment File"

    cat > .env << EOF
# Database Configuration
DB_TYPE=${DB_TYPE}
ConnectionStrings__DefaultConnection=${DB_CONNECTION}

# Authentication
Auth__SeedUsername=${admin_user}
Auth__SeedPassword=${admin_pass}
Auth__JwtSecret=${JWT_SECRET}
Auth__JwtIssuer=mineos
Auth__JwtAudience=mineos
Auth__JwtExpiryHours=24

# API Configuration
ApiKey__SeedKey=${API_KEY}

# Host Configuration
Host__BaseDirectory=${base_dir}
Host__ServersPathSegment=servers
Host__ProfilesPathSegment=profiles
Host__BackupsPathSegment=backups
Host__ArchivesPathSegment=archives
Host__ImportsPathSegment=imports
Host__OwnerUid=1000
Host__OwnerGid=1000

# Optional: CurseForge Integration
$([ -n "$curseforge_key" ] && echo "CurseForge__ApiKey=${curseforge_key}" || echo "# CurseForge__ApiKey=")

# Optional: Discord Integration
$([ -n "$discord_webhook" ] && echo "Discord__WebhookUrl=${discord_webhook}" || echo "# Discord__WebhookUrl=")

# Ports
API_PORT=${api_port}
WEB_PORT=${web_port}

# Logging
Logging__LogLevel__Default=Information
Logging__LogLevel__Microsoft.AspNetCore=Warning
EOF

    success "Created .env file"
}

# Create required directories
create_directories() {
    header "Creating Directories"

    mkdir -p "${base_dir}/servers"
    mkdir -p "${base_dir}/profiles"
    mkdir -p "${base_dir}/backups"
    mkdir -p "${base_dir}/archives"
    mkdir -p "${base_dir}/imports"
    mkdir -p "./data"

    success "Created required directories"

    # Set permissions
    if [ "$EUID" -eq 0 ]; then
        chown -R 1000:1000 "${base_dir}"
        success "Set directory ownership to 1000:1000"
    else
        warn "Not running as root, skipping ownership change"
        warn "You may need to run: sudo chown -R 1000:1000 ${base_dir}"
    fi
}

# Start services
start_services() {
    header "Starting Services"

    info "Starting Docker Compose services..."

    if command_exists docker-compose; then
        docker-compose up -d
    else
        docker compose up -d
    fi

    success "Services started!"

    # Wait for API to be ready
    info "Waiting for API to be ready..."
    local max_attempts=30
    local attempt=0

    while [ $attempt -lt $max_attempts ]; do
        if curl -s "http://localhost:${api_port}/health" >/dev/null 2>&1; then
            success "API is ready!"
            break
        fi
        attempt=$((attempt + 1))
        sleep 2
    done

    if [ $attempt -eq $max_attempts ]; then
        warn "API may not be ready yet. Check logs with: docker-compose logs api"
    fi
}

# Show first-run guide
show_guide() {
    header "Setup Complete!"

    echo -e "${GREEN}MineOS is now running!${NC}"
    echo ""
    echo "Access URLs:"
    echo -e "  ${BLUE}Web UI:${NC}    http://localhost:${web_port}"
    echo -e "  ${BLUE}API:${NC}       http://localhost:${api_port}"
    echo -e "  ${BLUE}API Docs:${NC}  http://localhost:${api_port}/swagger"
    echo ""
    echo "Admin Credentials:"
    echo -e "  ${BLUE}Username:${NC}  ${admin_user}"
    echo -e "  ${BLUE}Password:${NC}  ${admin_pass}"
    echo ""
    echo "API Key (for programmatic access):"
    echo -e "  ${BLUE}${API_KEY}${NC}"
    echo ""
    echo "Useful Commands:"
    echo "  View logs:        docker-compose logs -f"
    echo "  Stop services:    docker-compose down"
    echo "  Restart services: docker-compose restart"
    echo "  Update services:  docker-compose pull && docker-compose up -d"
    echo ""
    echo "Documentation:"
    echo "  Testing Guide:    ./QUICK-TEST-GUIDE.md"
    echo "  Roadmap:          ./IMPLEMENTATION-ROADMAP.md"
    echo ""
    success "Setup completed successfully!"
}

# Main execution
main() {
    clear
    header "MineOS Setup"

    info "Welcome to MineOS! This script will help you get started."
    echo ""

    # Check if .env already exists
    if [ -f .env ]; then
        warn ".env file already exists!"
        read -p "Do you want to reconfigure? This will overwrite existing settings. (y/N): " reconfigure
        if [ "$reconfigure" != "y" ] && [ "$reconfigure" != "Y" ]; then
            info "Using existing configuration"
            start_services
            show_guide
            exit 0
        fi
    fi

    # Run setup steps
    check_dependencies
    run_config_wizard
    create_env_file
    create_directories
    start_services
    show_guide
}

# Run main function
main "$@"
