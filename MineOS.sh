#!/bin/bash

# MineOS Setup & Management Script
# Interactive setup and management for Linux/macOS

if [ -z "${BASH_VERSION:-}" ]; then
    echo "[ERR] This script requires bash. Re-running with bash..."
    exec /usr/bin/env bash "$0" "$@"
fi

# If stdin isn't a TTY (e.g., piped), reopen /dev/tty for prompts.
if [ ! -t 0 ] && [ -e /dev/tty ] && [ -f "$0" ]; then
    exec </dev/tty
elif [ ! -t 0 ] && [ ! -e /dev/tty ]; then
    echo "[ERR] Interactive setup requires a TTY."
    exit 1
fi

set -e

DEV_MODE=false
FORCE_MODE=false
BUILD_MODE=false
for arg in "$@"; do
    case "$arg" in
        --dev) DEV_MODE=true ;;
        --force) FORCE_MODE=true ;;
        --build) BUILD_MODE=true ;;
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
DEFAULT_NETWORK_MODE="bridge"
DEFAULT_SHUTDOWN_TIMEOUT=300

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

# JSON-escape string for simple payloads
json_escape() {
    printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g'
}

reset_admin_via_api() {
    local api_port="$1"
    local username="$2"
    local password="$3"
    local rotate_api="$4"

    local api_key
    api_key=$(get_env_value ApiKey__SeedKey 2>/dev/null || echo "")
    if [ -z "$api_key" ]; then
        warn "ApiKey__SeedKey is missing; cannot reset admin via API."
        return 1
    fi

    local payload
    payload=$(printf '{"username":"%s","password":"%s","rotateApiKey":%s}' \
        "$(json_escape "$username")" \
        "$(json_escape "$password")" \
        "$rotate_api")

    local response
    if ! response=$(curl -fsSL -X POST \
        -H "Content-Type: application/json" \
        -H "X-Api-Key: ${api_key}" \
        -d "$payload" \
        "http://localhost:${api_port}/api/v1/auth/seed/reset"); then
        warn "Failed to reset admin user via API. Check that the API is running."
        return 1
    fi

    if [ "$rotate_api" = "true" ]; then
        local new_key
        if command_exists python3; then
            new_key=$(python3 - <<'PY' "$response"
import json, sys
data = json.loads(sys.argv[1])
print(data.get("apiKey") or "")
PY
)
        else
            new_key=$(echo "$response" | sed -n 's/.*"apiKey":"\\([^"]*\\)".*/\\1/p')
        fi

        if [ -n "$new_key" ]; then
            set_env_file_value ".env" "ApiKey__SeedKey" "$new_key"
            success "API key rotated and saved to .env"
        else
            warn "API key rotation requested but no key returned."
        fi
    fi

    success "Admin credentials updated in database."
    return 0
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

derive_caddy_site() {
    local origin="$1"
    if [ -z "$origin" ]; then
        echo "http://localhost"
        return
    fi

    local trimmed="$origin"
    local scheme=""
    if [[ "$trimmed" =~ ^https?:// ]]; then
        scheme="${trimmed%%://*}"
        trimmed="${trimmed#*://}"
    fi
    trimmed="${trimmed%%/*}"
    local host="${trimmed%%:*}"
    if [ -z "$host" ]; then
        echo "http://localhost"
        return
    fi

    if [ "$scheme" = "https" ]; then
        echo "$host"
    else
        echo "http://$host"
    fi
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

is_truthy() {
    case "$1" in
        [Tt][Rr][Uu][Ee]|1|[Yy][Ee][Ss]|[Yy]|[Oo][Nn]) return 0 ;;
    esac
    return 1
}

is_build_enabled() {
    if [ "$BUILD_MODE" = "true" ]; then
        return 0
    fi

    local value
    value=$(get_env_value MINEOS_BUILD_FROM_SOURCE 2>/dev/null || echo "")
    if is_truthy "$value"; then
        return 0
    fi

    return 1
}

set_compose_files() {
    local mode
    mode=$(get_env_value MINEOS_NETWORK_MODE 2>/dev/null || echo "$DEFAULT_NETWORK_MODE")

    COMPOSE_FILES=(-f docker-compose.yml)
    if [ "$mode" = "host" ]; then
        COMPOSE_FILES+=(-f docker-compose.host.yml)
    fi
    if is_build_enabled; then
        COMPOSE_FILES+=(-f docker-compose.build.yml)
    fi
}

get_shutdown_timeout() {
    local value
    value=$(get_env_value MINEOS_SHUTDOWN_TIMEOUT 2>/dev/null || echo "$DEFAULT_SHUTDOWN_TIMEOUT")
    if [[ ! "$value" =~ ^[0-9]+$ ]]; then
        value="$DEFAULT_SHUTDOWN_TIMEOUT"
    fi
    echo "$value"
}

get_api_base_url() {
    local api_port
    api_port=$(get_env_value API_PORT 2>/dev/null || echo "5078")
    echo "http://localhost:${api_port}/api/v1"
}

get_api_key() {
    get_env_value ApiKey__SeedKey 2>/dev/null || echo ""
}

urlencode() {
    local raw="$1"
    if command_exists python3; then
        python3 - <<'PY' "$raw"
import sys, urllib.parse
print(urllib.parse.quote(sys.argv[1]))
PY
        return
    fi
    if command_exists python; then
        python - <<'PY' "$raw"
import sys, urllib.parse
print(urllib.parse.quote(sys.argv[1]))
PY
        return
    fi
    echo "$raw" | sed 's/ /%20/g'
}

stop_minecraft_servers_individual() {
    local force_mode="${1:-false}"
    if ! command_exists curl; then
        warn "curl not found; skipping Minecraft server shutdown."
        return
    fi

    local api_key
    api_key=$(get_api_key)
    if [ -z "$api_key" ]; then
        warn "API key not found; skipping Minecraft server shutdown."
        return
    fi

    local api_base
    api_base=$(get_api_base_url)
    local server_json
    server_json=$(curl -s -H "X-Api-Key: ${api_key}" "${api_base}/servers/list")
    if [ -z "$server_json" ]; then
        warn "Unable to fetch server list; skipping Minecraft server shutdown."
        return
    fi

    local server_names
    server_names=$(echo "$server_json" | grep -o '"name":"[^"]*"' | sed 's/"name":"//;s/"//')
    if [ -z "$server_names" ]; then
        info "No servers found to stop."
        return
    fi

    local timeout
    timeout=$(get_shutdown_timeout)

    local action="stop"
    if [ "$force_mode" = "true" ]; then
        action="kill"
    fi

    while IFS= read -r server_name; do
        [ -z "$server_name" ] && continue
        local encoded
        encoded=$(urlencode "$server_name")
        if [ "$action" = "kill" ]; then
            info "Force stopping Minecraft server: ${server_name}"
        else
            info "Stopping Minecraft server: ${server_name}"
        fi
        curl -s -X POST -H "X-Api-Key: ${api_key}" \
            "${api_base}/servers/${encoded}/actions/${action}" >/dev/null 2>&1 || \
            warn "Failed to stop server ${server_name}"
        if [ "$action" = "stop" ]; then
            wait_for_server_stop "$api_base" "$api_key" "$server_name" "$timeout"
        fi
    done <<< "$server_names"
}

stop_minecraft_servers() {
    local force_mode="${1:-false}"
    if ! command_exists curl; then
        warn "curl not found; skipping Minecraft server shutdown."
        return
    fi

    local api_key
    api_key=$(get_api_key)
    if [ -z "$api_key" ]; then
        warn "API key not found; skipping Minecraft server shutdown."
        return
    fi

    local api_base
    api_base=$(get_api_base_url)
    if [ "$force_mode" = "true" ]; then
        stop_minecraft_servers_individual true
        return
    fi

    local timeout
    timeout=$(get_shutdown_timeout)

    local response
    response=$(curl -s -X POST -H "X-Api-Key: ${api_key}" \
        "${api_base}/servers/actions/stop-all?timeoutSeconds=${timeout}")

    if [ -z "$response" ]; then
        warn "Stop-all endpoint did not respond; falling back to per-server shutdown."
        stop_minecraft_servers_individual
        return
    fi

    info "Stop-all request complete."
}

wait_for_server_stop() {
    local api_base="$1"
    local api_key="$2"
    local server_name="$3"
    local timeout="$4"
    local encoded
    encoded=$(urlencode "$server_name")
    local start
    start=$(date +%s)

    while true; do
        local status_json
        status_json=$(curl -s -H "X-Api-Key: ${api_key}" \
            "${api_base}/servers/${encoded}/status")
        if [ -z "$status_json" ]; then
            warn "Unable to check status for ${server_name}."
            return 1
        fi
        if ! echo "$status_json" | grep -q '"status":"running"'; then
            return 0
        fi
        local now
        now=$(date +%s)
        local elapsed=$((now - start))
        if [ "$elapsed" -ge "$timeout" ]; then
            warn "Timed out waiting for ${server_name} to stop."
            return 1
        fi
        sleep 2
    done
}

wait_for_services_stop() {
    local timeout="$1"
    local start
    start=$(date +%s)
    while true; do
        local running
        running=$("${COMPOSE_CMD[@]}" "${COMPOSE_FILES[@]}" ps --status running -q 2>/dev/null | wc -l | tr -d ' ')
        if [ "$running" -eq 0 ]; then
            return 0
        fi
        local now
        now=$(date +%s)
        local elapsed=$((now - start))
        if [ "$elapsed" -ge "$timeout" ]; then
            warn "Timed out waiting for services to stop."
            return 1
        fi
        sleep 2
    done
}

graceful_stop_services() {
    local force_mode="${1:-$FORCE_MODE}"
    if ! set_compose_cmd; then
        error "Docker Compose not found"
        exit 1
    fi

    set_compose_files
    local timeout
    timeout=$(get_shutdown_timeout)
    if [ "$force_mode" = "true" ]; then
        info "Force stop enabled; killing servers and stopping containers immediately."
        stop_minecraft_servers true
        "${COMPOSE_CMD[@]}" "${COMPOSE_FILES[@]}" stop -t 0 >/dev/null 2>&1 || true
        return
    fi

    stop_minecraft_servers
    info "Stopping services (timeout: ${timeout}s)..."
    "${COMPOSE_CMD[@]}" "${COMPOSE_FILES[@]}" stop -t "$timeout" >/dev/null 2>&1 || true
    wait_for_services_stop "$timeout" || true
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

    set_compose_files
    local running
    running=$("${COMPOSE_CMD[@]}" "${COMPOSE_FILES[@]}" ps --status running -q 2>/dev/null | wc -l)
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
    local mc_host
    api_port=$(get_env_value API_PORT 2>/dev/null || echo "5078")
    api_key=$(get_env_value ApiKey__SeedKey 2>/dev/null || echo "")
    mc_host=$(get_env_value PUBLIC_MINECRAFT_HOST 2>/dev/null || echo "localhost")

    mkdir -p apps/web
    local env_path="apps/web/.env.local"
    set_env_file_value "$env_path" "PUBLIC_API_BASE_URL" "http://localhost:${api_port}"
    set_env_file_value "$env_path" "PRIVATE_API_BASE_URL" "http://localhost:${api_port}"
    if [ -n "$api_key" ]; then
        set_env_file_value "$env_path" "PRIVATE_API_KEY" "$api_key"
    fi
    set_env_file_value "$env_path" "PUBLIC_MINECRAFT_HOST" "$mc_host"
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
    caddy_site=$(derive_caddy_site "$web_origin")

    read -p "Public Minecraft host (default: localhost): " mc_public_host
    mc_public_host=${mc_public_host:-localhost}

    read -p "Web UI upload body size limit (default: Infinity): " body_size_limit
    body_size_limit=${body_size_limit:-Infinity}

    echo ""

    info "LAN discovery (Minecraft LAN list) requires host networking on Linux."
    info "Host networking removes Docker network isolation and binds ports directly on the host."
    info "When enabled, API_PORT/WEB_PORT become the actual listening ports."
    read -p "Enable host networking for LAN discovery? (y/N): " host_network_choice
    if [[ "$host_network_choice" =~ ^[Yy]$ ]]; then
        if [ "$(uname -s)" != "Linux" ]; then
            warn "Host networking is only supported on Linux. Using bridge mode instead."
            network_mode="$DEFAULT_NETWORK_MODE"
        else
            network_mode="host"
        fi
    else
        network_mode="$DEFAULT_NETWORK_MODE"
    fi

    echo ""

    read -p "Build images from source instead of pulling prebuilt images? (y/N): " build_choice
    if [[ "$build_choice" =~ ^[Yy]$ ]]; then
        build_from_source="true"
    else
        build_from_source="false"
    fi

    read -p "Image tag to pull (default: latest): " image_tag
    image_tag=${image_tag:-latest}

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

# Docker networking (bridge = isolated, host = required for LAN discovery)
MINEOS_NETWORK_MODE=${network_mode:-$DEFAULT_NETWORK_MODE}
MINEOS_BUILD_FROM_SOURCE=${build_from_source:-false}
MINEOS_IMAGE_TAG=${image_tag:-latest}

# Optional: CurseForge Integration
$([ -n "$curseforge_key" ] && echo "CurseForge__ApiKey=${curseforge_key}" || echo "# CurseForge__ApiKey=")

# Optional: Discord Integration
$([ -n "$discord_webhook" ] && echo "Discord__WebhookUrl=${discord_webhook}" || echo "# Discord__WebhookUrl=")

# Ports
API_PORT=${api_port}
WEB_PORT=${web_port}

# Web Origins
#CORS Backend
WEB_ORIGIN_PROD=${web_origin}
# Browser API base should match the public web origin
PUBLIC_API_BASE_URL=${web_origin}
# CSRF / Absolute URLs
ORIGIN=${web_origin}
# Reverse proxy site address (host only for HTTPS, http:// for plain)
CADDY_SITE=${caddy_site}

# Minecraft Server Address
PUBLIC_MINECRAFT_HOST=${mc_public_host}

# Web UI Upload Limits
BODY_SIZE_LIMIT=${body_size_limit}

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
    if ! is_build_enabled; then
        info "Build-from-source disabled; skipping image build."
        return
    fi

    if [ ! -d "./apps" ]; then
        error "Source files not found. Use the install script with --build or clone the repo."
        exit 1
    fi

    if ! set_compose_cmd; then
        error "Docker Compose not found"
        exit 1
    fi

    set_compose_files
    info "Building Docker images (this may take a few minutes)..."
    PUBLIC_BUILD_ID=$(date +%Y%m%d%H%M%S)
    export PUBLIC_BUILD_ID
    info "Build ID: ${PUBLIC_BUILD_ID}"
    if [ "${COMPOSE_CMD[0]}" = "docker" ]; then
        "${COMPOSE_CMD[@]}" "${COMPOSE_FILES[@]}" build --progress plain
    else
        "${COMPOSE_CMD[@]}" "${COMPOSE_FILES[@]}" build
    fi

    success "Build complete"
}

pull_services() {
    if ! set_compose_cmd; then
        error "Docker Compose not found"
        exit 1
    fi

    set_compose_files
    info "Pulling Docker images..."
    "${COMPOSE_CMD[@]}" "${COMPOSE_FILES[@]}" pull || true
}

# Start services (without rebuild)
start_services() {
    if ! set_compose_cmd; then
        error "Docker Compose not found"
        exit 1
    fi

    set_compose_files
    info "Starting services..."
    "${COMPOSE_CMD[@]}" "${COMPOSE_FILES[@]}" up -d

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

    set_compose_files
    info "Stopping web service (if running)..."
    "${COMPOSE_CMD[@]}" "${COMPOSE_FILES[@]}" stop web >/dev/null 2>&1 || true

    info "Starting API service..."
    set +e
    "${COMPOSE_CMD[@]}" "${COMPOSE_FILES[@]}" up -d api
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

start_web_dev_container() {
    echo -e "${CYAN}Web Dev Container${NC}"
    echo ""

    if ! is_installed; then
        error "No installation found. Run fresh install first."
        read -p "Press Enter to continue..."
        return
    fi

    check_dependencies

    if ! set_compose_cmd; then
        error "Docker Compose not found"
        exit 1
    fi

    set_compose_files
    local api_port
    local dev_origin
    api_port=$(get_env_value API_PORT 2>/dev/null || echo "5078")
    dev_origin=$(get_env_value WEB_ORIGIN_DEV 2>/dev/null || echo "http://localhost:5174")

    read -p "Dev web origin (default: ${dev_origin}): " dev_origin_input
    dev_origin_input=${dev_origin_input:-$dev_origin}

    local dev_host
    dev_host=$(echo "$dev_origin_input" | sed -E 's#^https?://##' | cut -d/ -f1 | cut -d: -f1)
    dev_host=${dev_host:-localhost}
    local public_api_input="${dev_origin_input}"

    set_env_file_value ".env" "WEB_ORIGIN_DEV" "$dev_origin_input"
    set_env_file_value ".env" "PUBLIC_API_BASE_URL" "$public_api_input"
    set_env_file_value ".env" "VITE_ALLOWED_HOSTS" "$dev_host"

    if command_exists git; then
        local git_name
        local git_email
        git_name=$(get_env_value GIT_USER_NAME 2>/dev/null || echo "")
        git_email=$(get_env_value GIT_USER_EMAIL 2>/dev/null || echo "")
        if [ -z "$git_name" ]; then
            git_name=$(git config --global user.name 2>/dev/null || echo "")
        fi
        if [ -z "$git_email" ]; then
            git_email=$(git config --global user.email 2>/dev/null || echo "")
        fi
        if [ -n "$git_name" ]; then
            set_env_file_value ".env" "GIT_USER_NAME" "$git_name"
        fi
        if [ -n "$git_email" ]; then
            set_env_file_value ".env" "GIT_USER_EMAIL" "$git_email"
        fi
    fi

    info "Stopping web service (if running)..."
    "${COMPOSE_CMD[@]}" "${COMPOSE_FILES[@]}" stop web >/dev/null 2>&1 || true

    info "Starting API service..."
    set +e
    "${COMPOSE_CMD[@]}" "${COMPOSE_FILES[@]}" up -d api
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

    info "Starting web dev container..."
    "${COMPOSE_CMD[@]}" "${COMPOSE_FILES[@]}" -f docker-compose.dev.yml up -d --force-recreate web-dev

    success "Web dev container started"
    echo -e "${CYAN}Web UI (dev):${NC} ${dev_origin_input}"
    echo -e "${CYAN}API (proxy):${NC} ${dev_origin_input}/api"
    echo ""
    read -p "Press Enter to continue..."
}

# Stop services
stop_services() {
    graceful_stop_services
    info "Removing containers..."
    "${COMPOSE_CMD[@]}" "${COMPOSE_FILES[@]}" down
    success "Services stopped"
}

# Restart services
restart_services() {
    graceful_stop_services
    start_services
    success "Services restarted"
}

# View logs
view_logs() {
    if ! set_compose_cmd; then
        error "Docker Compose not found"
        exit 1
    fi

    set_compose_files
    info "Showing logs (press Q to return)..."
    "${COMPOSE_CMD[@]}" "${COMPOSE_FILES[@]}" logs -f &
    local log_pid=$!

    while kill -0 "$log_pid" 2>/dev/null; do
        if read -r -n1 -s -t 1 key; then
            case "$key" in
                [Qq])
                    kill "$log_pid" >/dev/null 2>&1 || true
                    wait "$log_pid" 2>/dev/null || true
                    break
                    ;;
            esac
        fi
    done
}

# Show detailed status
show_detailed_status() {
    if ! set_compose_cmd; then
        error "Docker Compose not found"
        exit 1
    fi

    set_compose_files
    echo -e "${CYAN}Service Status:${NC}"
    echo ""
    "${COMPOSE_CMD[@]}" "${COMPOSE_FILES[@]}" ps
    echo ""

    if is_installed; then
        local api_port
        local web_port
        local web_origin
        api_port=$(get_env_value API_PORT 2>/dev/null || echo "5078")
        web_port=$(get_env_value WEB_PORT 2>/dev/null || echo "3000")
        web_origin=$(get_env_value WEB_ORIGIN_PROD 2>/dev/null || echo "http://localhost:${web_port}")

        echo -e "${CYAN}Access URLs:${NC}"
        echo -e "  Web UI:    ${GREEN}${web_origin}${NC}"
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
    if is_build_enabled; then
        build_services
    else
        pull_services
    fi
    start_services

    echo ""
    echo -e "${GREEN}========================================${NC}"
    echo -e "${GREEN}  Installation Complete!${NC}"
    echo -e "${GREEN}========================================${NC}"
    echo ""

    local api_port
    local web_port
    local web_origin
    api_port=$(get_env_value API_PORT 2>/dev/null || echo "5078")
    web_port=$(get_env_value WEB_PORT 2>/dev/null || echo "3000")
    web_origin=$(get_env_value WEB_ORIGIN_PROD 2>/dev/null || echo "http://localhost:${web_port}")

    echo -e "${CYAN}Access URLs:${NC}"
    echo -e "  Web UI:    ${GREEN}${web_origin}${NC}"
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

    graceful_stop_services
    "${COMPOSE_CMD[@]}" "${COMPOSE_FILES[@]}" down
    if is_build_enabled; then
        build_services
    else
        pull_services
    fi
    "${COMPOSE_CMD[@]}" "${COMPOSE_FILES[@]}" up -d --force-recreate

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

reconfigure() {
    echo -e "${CYAN}Reconfigure${NC}"
    echo ""

    if ! is_installed; then
        error "No installation found. Run fresh install first."
        read -p "Press Enter to continue..."
        return
    fi

    local admin_user_current
    local admin_pass_current
    local host_base_dir_current
    local data_dir_current
    local api_port_current
    local web_port_current
    local web_origin_current
    local mc_public_host_current
    local body_size_limit_current
    local curseforge_current
    local discord_current
    local network_mode_current
    local build_from_source_current
    local image_tag_current

    admin_user_current=$(get_env_value Auth__SeedUsername 2>/dev/null || echo "admin")
    admin_pass_current=$(get_env_value Auth__SeedPassword 2>/dev/null || echo "")
    host_base_dir_current=$(get_env_value HOST_BASE_DIRECTORY 2>/dev/null || echo "$DEFAULT_HOST_BASE_DIR")
    data_dir_current=$(get_env_value Data__Directory 2>/dev/null || echo "$DEFAULT_DATA_DIR")
    api_port_current=$(get_env_value API_PORT 2>/dev/null || echo "5078")
    web_port_current=$(get_env_value WEB_PORT 2>/dev/null || echo "3000")
    web_origin_current=$(get_env_value WEB_ORIGIN_PROD 2>/dev/null || echo "http://localhost:${web_port_current}")
    mc_public_host_current=$(get_env_value PUBLIC_MINECRAFT_HOST 2>/dev/null || echo "localhost")
    body_size_limit_current=$(get_env_value BODY_SIZE_LIMIT 2>/dev/null || echo "Infinity")
    curseforge_current=$(get_env_value CurseForge__ApiKey 2>/dev/null || echo "")
    discord_current=$(get_env_value Discord__WebhookUrl 2>/dev/null || echo "")
    network_mode_current=$(get_env_value MINEOS_NETWORK_MODE 2>/dev/null || echo "$DEFAULT_NETWORK_MODE")
    build_from_source_current=$(get_env_value MINEOS_BUILD_FROM_SOURCE 2>/dev/null || echo "false")
    image_tag_current=$(get_env_value MINEOS_IMAGE_TAG 2>/dev/null || echo "latest")

    read -p "Admin username (default: ${admin_user_current}): " admin_user
    admin_user=${admin_user:-$admin_user_current}

    read -sp "Admin password (leave blank to keep current): " admin_pass
    echo ""
    if [ -z "$admin_pass" ]; then
        admin_pass="$admin_pass_current"
    fi

    read -p "Local storage directory (default: ${host_base_dir_current}): " host_base_dir_input
    host_base_dir_input=${host_base_dir_input:-$host_base_dir_current}
    host_base_dir=$(normalize_relative_path "$host_base_dir_input")
    if [ -z "$host_base_dir" ]; then
        host_base_dir="$host_base_dir_current"
    fi

    read -p "Database directory (default: ${data_dir_current}): " data_dir_input
    data_dir_input=${data_dir_input:-$data_dir_current}
    data_dir=$(normalize_relative_path "$data_dir_input")
    if [ -z "$data_dir" ]; then
        data_dir="$data_dir_current"
    fi

    read -p "API port (default: ${api_port_current}): " api_port
    api_port=${api_port:-$api_port_current}

    read -p "Web UI port (default: ${web_port_current}): " web_port
    web_port=${web_port:-$web_port_current}

    read -p "Web UI origin (default: ${web_origin_current}): " web_origin
    web_origin=${web_origin:-$web_origin_current}
    caddy_site=$(derive_caddy_site "$web_origin")

    read -p "Public Minecraft host (default: ${mc_public_host_current}): " mc_public_host
    mc_public_host=${mc_public_host:-$mc_public_host_current}

    read -p "Web UI upload body size limit (default: ${body_size_limit_current}): " body_size_limit
    body_size_limit=${body_size_limit:-$body_size_limit_current}

    echo ""
    info "LAN discovery (Minecraft LAN list) requires host networking on Linux."
    info "Host networking removes Docker network isolation and binds ports directly on the host."
    info "When enabled, API_PORT/WEB_PORT become the actual listening ports."
    read -p "Enable host networking for LAN discovery? (current: ${network_mode_current}) (y/N): " host_network_choice
    if [ -z "$host_network_choice" ]; then
        network_mode="$network_mode_current"
    elif [[ "$host_network_choice" =~ ^[Yy]$ ]]; then
        if [ "$(uname -s)" != "Linux" ]; then
            warn "Host networking is only supported on Linux. Keeping bridge mode."
            network_mode="$DEFAULT_NETWORK_MODE"
        else
            network_mode="host"
        fi
    else
        network_mode="$DEFAULT_NETWORK_MODE"
    fi

    read -p "Build images from source instead of pulling prebuilt images? (current: ${build_from_source_current}) (y/N): " build_choice
    if [ -z "$build_choice" ]; then
        build_from_source="$build_from_source_current"
    elif [[ "$build_choice" =~ ^[Yy]$ ]]; then
        build_from_source="true"
    else
        build_from_source="false"
    fi

    read -p "Image tag to pull (default: ${image_tag_current}): " image_tag
    image_tag=${image_tag:-$image_tag_current}

    read -p "CurseForge API key (leave blank to keep current): " curseforge_key
    if [ -z "$curseforge_key" ]; then
        curseforge_key="$curseforge_current"
    fi

    read -p "Discord webhook URL (leave blank to keep current): " discord_webhook
    if [ -z "$discord_webhook" ]; then
        discord_webhook="$discord_current"
    fi

    echo ""
    read -p "Reset admin user in database now? (y/N): " reset_admin_choice
    reset_admin_choice=${reset_admin_choice:-N}
    if [[ "$reset_admin_choice" =~ ^[Yy]$ ]]; then
        if [ -z "$admin_pass" ]; then
            read -sp "New admin password (required): " admin_pass
            echo ""
        fi
        if [ -z "$admin_pass" ]; then
            warn "Admin password is required to reset."
        else
            read -p "Rotate API key as well? (y/N): " rotate_api_choice
            rotate_api_choice=${rotate_api_choice:-N}
            if [[ "$rotate_api_choice" =~ ^[Yy]$ ]]; then
                reset_admin_via_api "$api_port" "$admin_user" "$admin_pass" "true"
            else
                reset_admin_via_api "$api_port" "$admin_user" "$admin_pass" "false"
            fi
        fi
    fi

    set_env_file_value ".env" "Auth__SeedUsername" "$admin_user"
    if [ -n "$admin_pass" ]; then
        set_env_file_value ".env" "Auth__SeedPassword" "$admin_pass"
    fi
    set_env_file_value ".env" "HOST_BASE_DIRECTORY" "$host_base_dir"
    set_env_file_value ".env" "Data__Directory" "$data_dir"
    set_env_file_value ".env" "API_PORT" "$api_port"
    set_env_file_value ".env" "WEB_PORT" "$web_port"
    set_env_file_value ".env" "WEB_ORIGIN_PROD" "$web_origin"
    set_env_file_value ".env" "PUBLIC_API_BASE_URL" "$web_origin"
    set_env_file_value ".env" "ORIGIN" "$web_origin"
    set_env_file_value ".env" "CADDY_SITE" "$caddy_site"
    set_env_file_value ".env" "PUBLIC_MINECRAFT_HOST" "$mc_public_host"
    set_env_file_value ".env" "BODY_SIZE_LIMIT" "$body_size_limit"
    set_env_file_value ".env" "MINEOS_NETWORK_MODE" "${network_mode:-$DEFAULT_NETWORK_MODE}"
    set_env_file_value ".env" "MINEOS_BUILD_FROM_SOURCE" "${build_from_source:-false}"
    set_env_file_value ".env" "MINEOS_IMAGE_TAG" "${image_tag:-latest}"
    if [ -n "$curseforge_key" ]; then
        set_env_file_value ".env" "CurseForge__ApiKey" "$curseforge_key"
    fi
    if [ -n "$discord_webhook" ]; then
        set_env_file_value ".env" "Discord__WebhookUrl" "$discord_webhook"
    fi

    write_web_dev_env
    success "Configuration updated."
    info "Restart services to apply changes."
    read -p "Press Enter to continue..."
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
    echo "  [R] Reconfigure (update .env)"
    echo "  [W] Web Dev Container (Vite)"
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
                [Rr]) reconfigure ;;
                [Ww]) start_web_dev_container ;;
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
