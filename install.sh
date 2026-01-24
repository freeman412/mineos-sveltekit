#!/bin/bash

# MineOS Install Script
# Interactive setup for Linux/macOS

set -e

DEV_MODE=false
for arg in "$@"; do
    case "$arg" in
        --dev) DEV_MODE=true ;;
    esac
done

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m'

DEFAULT_HOST_BASE_DIR="./minecraft"
DEFAULT_DATA_DIR="./data"
CONTAINER_BASE_DIR="/var/games/minecraft"

# Print colored messages
info() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

success() {
    echo -e "${GREEN}[OK]${NC} $1"
}

warn() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

error() {
    echo -e "${RED}[ERR]${NC} $1"
}

# Check if command exists
command_exists() {
    command -v "$1" >/dev/null 2>&1
}

normalize_relative_path() {
    local path="$1"

    if [ -z "$path" ]; then
        return 1
    fi

    case "$path" in
        /*|~*) return 1 ;;
        "."|"./"| ".."| "../"*| *"/../"*| *"/.." ) return 1 ;;
    esac

    if [[ "$path" != ./* ]]; then
        path="./$path"
    fi

    echo "$path"
}

set_compose_cmd() {
    if command_exists docker && docker compose version >/dev/null 2>&1; then
        COMPOSE_CMD=(docker compose)
        COMPOSE_CMD_DISPLAY="docker compose"
        return 0
    fi

    if command_exists docker-compose; then
        COMPOSE_CMD=(docker-compose)
        COMPOSE_CMD_DISPLAY="docker-compose"
        return 0
    fi

    return 1
}

# Show banner
show_banner() {
    clear
    echo -e "${GREEN}"
    echo "  __  __ _             ___  ____  "
    echo " |  \/  (_)_ __   ___ / _ \/ ___| "
    echo " | |\/| | | '_ \ / _ \ | | \___ \ "
    echo " | |  | | | | | |  __/ |_| |___) |"
    echo " |_|  |_|_|_| |_|\___|\___/|____/ "
    echo -e "${NC}"
    echo -e "${CYAN}Minecraft Server Management${NC}"
    echo ""
}

# Check if MineOS is installed
is_installed() {
    [ -f .env ] && [ -f docker-compose.yml ]
}

# Check if services are running
services_running() {
    if ! set_compose_cmd; then
        return 1
    fi

    local running
    running=$("${COMPOSE_CMD[@]}" ps --status running -q 2>/dev/null | wc -l)
    [ "$running" -gt 0 ]
}

# Show current status
show_status() {
    echo -e "${CYAN}Installation Status:${NC}"

    if is_installed; then
        echo -e "  Config:   ${GREEN}Configured${NC}"
    else
        echo -e "  Config:   ${YELLOW}Not configured${NC}"
    fi

    if set_compose_cmd && services_running; then
        echo -e "  Services: ${GREEN}Running${NC}"
    else
        echo -e "  Services: ${YELLOW}Stopped${NC}"
    fi
    echo ""
}

# Check dependencies
check_dependencies() {
    info "Checking dependencies..."

    local all_ok=true

    # Check Docker
    if command_exists docker; then
        local docker_version
        docker_version=$(docker --version | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -1)
        success "Docker is installed (version $docker_version)"
    else
        error "Docker is not installed"
        info "Please install Docker: https://docs.docker.com/get-docker/"
        all_ok=false
    fi

    # Check Docker Compose
    if set_compose_cmd; then
        local compose_version
        if [ "${COMPOSE_CMD[0]}" = "docker" ]; then
            compose_version=$(docker compose version --short 2>/dev/null || docker compose version | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -1)
        else
            compose_version=$(docker-compose --version | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -1)
        fi
        success "Docker Compose is installed (version $compose_version)"
    else
        error "Docker Compose is not installed"
        info "Please install Docker Compose: https://docs.docker.com/compose/install/"
        all_ok=false
    fi

    if [ "$all_ok" = false ]; then
        error "Required dependencies are missing. Please install them and try again."
        exit 1
    fi

    echo ""
}

get_env_value() {
    local key="$1"
    if [ ! -f .env ]; then
        return 1
    fi

    local line
    line=$(grep -E "^${key}=" .env | head -n 1)
    if [ -z "$line" ]; then
        return 1
    fi

    echo "${line#*=}"
}

set_env_file_value() {
    local path="$1"
    local key="$2"
    local value="$3"

    if [ ! -f "$path" ]; then
        echo "${key}=${value}" > "$path"
        return
    fi

    local tmp
    tmp=$(mktemp)
    local found=false

    while IFS= read -r line || [ -n "$line" ]; do
        if [[ "$line" == "${key}="* ]]; then
            echo "${key}=${value}" >> "$tmp"
            found=true
        else
            echo "$line" >> "$tmp"
        fi
    done < "$path"

    if [ "$found" = false ]; then
        echo "${key}=${value}" >> "$tmp"
    fi

    mv "$tmp" "$path"
}

write_web_dev_env() {
    local api_port
    local api_key
    api_port=$(get_env_value API_PORT 2>/dev/null || echo "5078")
    api_key=$(get_env_value ApiKey__SeedKey 2>/dev/null || echo "")

    mkdir -p apps/web
    local env_path="apps/web/.env.local"
    set_env_file_value "$env_path" "PUBLIC_API_BASE_URL" "http://localhost:${api_port}"
    set_env_file_value "$env_path" "PRIVATE_API_BASE_URL" "http://localhost:${api_port}"
    if [ -n "$api_key" ]; then
        set_env_file_value "$env_path" "PRIVATE_API_KEY" "$api_key"
    fi
    set_env_file_value "$env_path" "ORIGIN" "http://localhost:5174"

    success "Updated apps/web/.env.local for dev"
}

# Configuration wizard
run_config_wizard() {
    echo -e "${CYAN}Configuration Wizard${NC}"
    echo ""

    info "This wizard will help you configure MineOS"
    echo ""

    DB_TYPE="sqlite"
    DB_CONNECTION="Data Source=/app/data/mineos.db"

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
    while true; do
        read -p "Local storage directory for Minecraft servers (relative, default: ${DEFAULT_HOST_BASE_DIR}): " host_base_dir_input
        host_base_dir_input=${host_base_dir_input:-$DEFAULT_HOST_BASE_DIR}
        host_base_dir=$(normalize_relative_path "$host_base_dir_input")
        if [ -z "$host_base_dir" ]; then
            error "Path must be relative to the current directory (no leading /, ~, or ..)."
            continue
        fi
        break
    done

    # Database directory
    while true; do
        read -p "Database directory (relative, default: ${DEFAULT_DATA_DIR}): " data_dir_input
        data_dir_input=${data_dir_input:-$DEFAULT_DATA_DIR}
        data_dir=$(normalize_relative_path "$data_dir_input")
        if [ -z "$data_dir" ]; then
            error "Path must be relative to the current directory (no leading /, ~, or ..)."
            continue
        fi
        break
    done

    base_dir="$CONTAINER_BASE_DIR"

    # Port configuration
    read -p "API port (default: 5078): " api_port
    api_port=${api_port:-5078}

    read -p "Web UI port (default: 3000): " web_port
    web_port=${web_port:-3000}

    read -p "Web UI origin (default: http://localhost:${web_port}): " web_origin
    web_origin=${web_origin:-http://localhost:${web_port}}

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
    info "Creating environment file..."

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
HOST_BASE_DIRECTORY=${host_base_dir}
Host__BaseDirectory=${base_dir}
Data__Directory=${data_dir}
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
WEB_ORIGIN_PROD=${web_origin}

# Logging
Logging__LogLevel__Default=Information
Logging__LogLevel__Microsoft.AspNetCore=Warning
EOF

    success "Created .env file"
}

# Create required directories
create_directories() {
    info "Creating directories..."

    mkdir -p "${host_base_dir}/servers"
    mkdir -p "${host_base_dir}/profiles"
    mkdir -p "${host_base_dir}/backups"
    mkdir -p "${host_base_dir}/archives"
    mkdir -p "${host_base_dir}/imports"
    mkdir -p "${data_dir}"

    # Set permissions
    if [ "$EUID" -eq 0 ]; then
        chown -R 1000:1000 "${host_base_dir}"
        chown -R 1000:1000 "${data_dir}"
        success "Set directory ownership to 1000:1000"
    else
        warn "Not running as root, skipping ownership change"
        warn "You may need to run: sudo chown -R 1000:1000 ${host_base_dir} ${data_dir}"
    fi

    success "Created required directories"
}

# Build and start services
build_services() {
    if ! set_compose_cmd; then
        error "Docker Compose not found"
        exit 1
    fi

    info "Building Docker images..."
    PUBLIC_BUILD_ID=$(date +%Y%m%d%H%M%S)
    export PUBLIC_BUILD_ID
    info "Build ID: ${PUBLIC_BUILD_ID}"
    if [ "${COMPOSE_CMD[0]}" = "docker" ]; then
        "${COMPOSE_CMD[@]}" build --progress plain
    else
        "${COMPOSE_CMD[@]}" build
    fi

    success "Build complete"
}

# Start services (without rebuild)
start_services() {
    if ! set_compose_cmd; then
        error "Docker Compose not found"
        exit 1
    fi

    info "Starting services..."
    "${COMPOSE_CMD[@]}" up -d

    # Wait for API
    if command_exists curl; then
        local api_port
        api_port=$(get_env_value API_PORT 2>/dev/null || echo "5078")

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
            warn "API may not be ready yet. Check logs with: ${COMPOSE_CMD_DISPLAY} logs api"
        fi
    fi

    success "Services started"
}

start_dev_mode() {
    echo -e "${CYAN}Dev Mode${NC}"
    echo ""

    if ! is_installed; then
        error "No installation found. Run fresh install first."
        return
    fi

    check_dependencies

    if ! set_compose_cmd; then
        error "Docker Compose not found"
        exit 1
    fi

    info "Stopping web service (if running)..."
    "${COMPOSE_CMD[@]}" stop web >/dev/null 2>&1 || true

    info "Starting API service..."
    set +e
    "${COMPOSE_CMD[@]}" up -d api
    local up_code=$?
    set -e

    if [ "$up_code" -ne 0 ]; then
        local running
        running=$("${COMPOSE_CMD[@]}" ps --status running -q api 2>/dev/null | wc -l | tr -d ' ')
        if [ "$running" -eq 0 ]; then
            error "Failed to start API service"
            exit 1
        else
            warn "Compose returned a non-zero exit code but the API container is running."
        fi
    fi

    write_web_dev_env

    echo ""
    echo -e "${CYAN}Web dev server:${NC}"
    echo "  cd apps/web"
    echo "  npm install"
    echo "  npm run dev -- --host 0.0.0.0 --port 5174"
    echo ""
    echo -e "${CYAN}Web UI (dev):${NC} http://localhost:5174"
    echo -e "${CYAN}API:${NC} http://localhost:$(get_env_value API_PORT 2>/dev/null || echo "5078")"
    echo ""
}

# Stop services
stop_services() {
    if ! set_compose_cmd; then
        error "Docker Compose not found"
        exit 1
    fi

    info "Stopping services..."
    "${COMPOSE_CMD[@]}" down
    success "Services stopped"
}

# Restart services
restart_services() {
    if ! set_compose_cmd; then
        error "Docker Compose not found"
        exit 1
    fi

    info "Restarting services..."
    "${COMPOSE_CMD[@]}" restart
    success "Services restarted"
}

# View logs
view_logs() {
    if ! set_compose_cmd; then
        error "Docker Compose not found"
        exit 1
    fi

    info "Showing logs (Ctrl+C to exit)..."
    "${COMPOSE_CMD[@]}" logs -f
}

# Show detailed status
show_detailed_status() {
    if ! set_compose_cmd; then
        error "Docker Compose not found"
        exit 1
    fi

    echo -e "${CYAN}Service Status:${NC}"
    echo ""
    "${COMPOSE_CMD[@]}" ps
    echo ""

    if is_installed; then
        local api_port
        local web_port
        api_port=$(get_env_value API_PORT 2>/dev/null || echo "5078")
        web_port=$(get_env_value WEB_PORT 2>/dev/null || echo "3000")

        echo -e "${CYAN}Access URLs:${NC}"
        echo -e "  Web UI:    ${GREEN}http://localhost:${web_port}${NC}"
        echo -e "  API:       ${GREEN}http://localhost:${api_port}${NC}"
        echo -e "  API Docs:  ${GREEN}http://localhost:${api_port}/swagger${NC}"
    fi

    echo ""
    read -p "Press Enter to continue..."
}

# Fresh install
fresh_install() {
    echo -e "${CYAN}Fresh Install${NC}"
    echo ""

    check_dependencies
    run_config_wizard
    create_env_file
    create_directories
    build_services
    start_services

    echo ""
    echo -e "${GREEN}========================================${NC}"
    echo -e "${GREEN}  Installation Complete!${NC}"
    echo -e "${GREEN}========================================${NC}"
    echo ""

    local api_port
    local web_port
    api_port=$(get_env_value API_PORT 2>/dev/null || echo "5078")
    web_port=$(get_env_value WEB_PORT 2>/dev/null || echo "3000")

    echo -e "${CYAN}Access URLs:${NC}"
    echo -e "  Web UI:    ${GREEN}http://localhost:${web_port}${NC}"
    echo -e "  API:       ${GREEN}http://localhost:${api_port}${NC}"
    echo -e "  API Docs:  ${GREEN}http://localhost:${api_port}/swagger${NC}"
    echo ""
    echo -e "${CYAN}Admin Credentials:${NC}"
    echo -e "  Username:  ${GREEN}${admin_user}${NC}"
    echo -e "  Password:  ${GREEN}${admin_pass}${NC}"
    echo ""
    echo -e "${CYAN}API Key:${NC}"
    echo -e "  ${GREEN}${API_KEY}${NC}"
    echo ""

    read -p "Press Enter to continue..."
}

# Rebuild (keep config)
rebuild() {
    echo -e "${CYAN}Rebuild${NC}"
    echo ""

    info "Rebuilding containers (keeping configuration)..."

    if ! set_compose_cmd; then
        error "Docker Compose not found"
        exit 1
    fi

    "${COMPOSE_CMD[@]}" down
    build_services
    "${COMPOSE_CMD[@]}" up -d --force-recreate

    success "Rebuild complete"
    echo ""
    read -p "Press Enter to continue..."
}

# Update (git pull + rebuild)
update() {
    echo -e "${CYAN}Update${NC}"
    echo ""

    if ! command_exists git; then
        error "Git is not installed"
        read -p "Press Enter to continue..."
        return
    fi

    info "Pulling latest changes..."
    git pull

    info "Rebuilding..."
    rebuild
}

# Show menu for not installed state
show_menu_not_installed() {
    echo -e "${CYAN}Options:${NC}"
    echo "  [1] Fresh Install"
    echo "  [Q] Quit"
    echo ""
}

# Show menu for installed state
show_menu_installed() {
    echo -e "${CYAN}Options:${NC}"
    echo "  [1] Start Services"
    echo "  [2] Stop Services"
    echo "  [3] Restart Services"
    echo "  [4] View Logs"
    echo "  [5] Show Status"
    echo ""
    echo "  [6] Rebuild (keep config)"
    echo "  [7] Update (git pull + rebuild)"
    echo "  [8] Fresh Install (reset everything)"
    echo "  [9] Dev Mode (API only + web dev env)"
    echo ""
    echo "  [Q] Quit"
    echo ""
}

# Main menu loop
main() {
    if [ "$DEV_MODE" = true ]; then
        start_dev_mode
        exit 0
    fi

    while true; do
        show_banner
        show_status

        if is_installed; then
            show_menu_installed
            read -p "Select option: " choice

            case $choice in
                1) start_services; read -p "Press Enter to continue..." ;;
                2) stop_services; read -p "Press Enter to continue..." ;;
                3) restart_services; read -p "Press Enter to continue..." ;;
                4) view_logs ;;
                5) show_detailed_status ;;
                6) rebuild ;;
                7) update ;;
                8) fresh_install ;;
                9) start_dev_mode; read -p "Press Enter to continue..." ;;
                [Qq]) echo "Goodbye!"; exit 0 ;;
                *) warn "Invalid option" ;;
            esac
        else
            show_menu_not_installed
            read -p "Select option: " choice

            case $choice in
                1) fresh_install ;;
                [Qq]) echo "Goodbye!"; exit 0 ;;
                *) warn "Invalid option" ;;
            esac
        fi
    done
}

# Run main function
main "$@"
