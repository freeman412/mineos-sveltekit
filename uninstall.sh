#!/bin/bash

# MineOS Uninstall Script
# Removes containers and optionally data/volumes.

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

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

header() {
    echo ""
    echo -e "${GREEN}================================${NC}"
    echo -e "${GREEN}$1${NC}"
    echo -e "${GREEN}================================${NC}"
    echo ""
}

command_exists() {
    command -v "$1" >/dev/null 2>&1
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

compose_down_keep() {
    "${COMPOSE_CMD[@]}" down --remove-orphans
}

compose_down_remove() {
    "${COMPOSE_CMD[@]}" down --remove-orphans
}

backup_data() {
    local timestamp
    local backup_root_rel
    local backup_root_abs

    timestamp=$(date +"%Y%m%d-%H%M%S")
    backup_root_rel="backups/mineos-uninstall-${timestamp}"
    backup_root_abs="$(pwd)/${backup_root_rel}"

    mkdir -p "${backup_root_abs}"

    if [ -d "./data" ]; then
        info "Backing up local data folder..."
        tar -czf "${backup_root_abs}/sqlite-data.tgz" -C "./data" .
    fi

    if [ -f ".env" ]; then
        cp ".env" "${backup_root_abs}/env.backup"
    fi

    success "Backup saved to ${backup_root_rel}"
}

confirm_destructive() {
    warn "This will permanently delete database files and local data."
    read -p "Type DELETE to continue: " confirmation
    if [ "$confirmation" != "DELETE" ]; then
        info "Uninstall cancelled."
        exit 0
    fi
}

remove_local_data() {
    if [ -d "./data" ]; then
        rm -rf "./data"
    fi
}

main() {
    clear
    header "MineOS Uninstall"

    if ! command_exists docker; then
        error "Docker is not installed."
        exit 1
    fi

    if ! set_compose_cmd; then
        error "Docker Compose is not available."
        exit 1
    fi

    warn "This will stop and remove MineOS containers."
    warn "You can keep or remove database data in the next step."
    echo ""
    echo "Choose an uninstall option:"
    echo "  1) Remove containers only (keep all data) [default]"
    echo "  2) Backup data then remove containers and data"
    echo "  3) Remove containers and data without backup"
    read -p "Enter choice [1-3]: " choice
    choice=${choice:-1}

    case "$choice" in
        1)
            compose_down_keep
            success "Containers removed. Data preserved."
            ;;
        2)
            confirm_destructive
            compose_down_keep
            backup_data
            compose_down_remove
            remove_local_data
            success "Containers and data removed. Backup created."
            ;;
        3)
            confirm_destructive
            compose_down_remove
            remove_local_data
            success "Containers and data removed."
            ;;
        *)
            error "Invalid choice"
            exit 1
            ;;
    esac

    info "If you want to remove Docker images too, run: docker image prune -a"
}

main "$@"
