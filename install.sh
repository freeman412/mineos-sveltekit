#!/usr/bin/env bash

set -euo pipefail

REPO_URL="https://github.com/freeman412/mineos-sveltekit.git"
INSTALL_DIR="${MINEOS_INSTALL_DIR:-mineos}"
REF="main"
BUILD=false
BUNDLE_URL=""
FORWARD_ARGS=()

usage() {
    cat <<'EOF'
MineOS installer

Usage:
  curl -fsSL https://mineos.net/install.sh | bash
  curl -fsSL https://mineos.net/install.sh | bash -s -- --build

Options:
  --build           Clone repo and build from source
  --ref <ref>       Git ref for --build (default: main)
  --dir <path>      Install directory (default: ./mineos)
  --repo <url>      Git repo for --build
  --bundle-url <u>  Override bundle download URL
  -h, --help        Show this help
EOF
}

command_exists() {
    command -v "$1" >/dev/null 2>&1
}

get_latest_bundle_url() {
    local asset_name="$1"
    local api="https://api.github.com/repos/freeman412/mineos-sveltekit/releases/latest"

    if command_exists python3; then
        python3 - <<'PY' "$api" "$asset_name"
import json, sys, urllib.request
api = sys.argv[1]
asset = sys.argv[2]
with urllib.request.urlopen(api) as resp:
    data = json.load(resp)
for item in data.get("assets", []):
    if item.get("name") == asset:
        print(item.get("browser_download_url", ""))
        break
PY
        return
    fi

    if command_exists python; then
        python - <<'PY' "$api" "$asset_name"
import json, sys
try:
    import urllib2 as urllib
except ImportError:
    import urllib.request as urllib
api = sys.argv[1]
asset = sys.argv[2]
resp = urllib.urlopen(api)
data = json.loads(resp.read().decode("utf-8"))
for item in data.get("assets", []):
    if item.get("name") == asset:
        print(item.get("browser_download_url", ""))
        break
PY
        return
    fi

    curl -fsSL "$api" | grep -m 1 "\"browser_download_url\": \".*${asset_name}\"" | \
        sed -E 's/.*"([^"]+)".*/\1/'
}

while [ $# -gt 0 ]; do
    case "$1" in
        --build) BUILD=true ;;
        --ref) REF="${2:-}"; shift ;;
        --dir) INSTALL_DIR="${2:-}"; shift ;;
        --repo) REPO_URL="${2:-}"; shift ;;
        --bundle-url) BUNDLE_URL="${2:-}"; shift ;;
        -h|--help) usage; exit 0 ;;
        *) FORWARD_ARGS+=("$1") ;;
    esac
    shift
done

if [ -z "$INSTALL_DIR" ]; then
    echo "[ERR] Install directory is required."
    exit 1
fi

if [ "$BUILD" = true ]; then
    if ! command_exists git; then
        echo "[ERR] git is required for --build."
        exit 1
    fi

    if [ -d "$INSTALL_DIR/.git" ]; then
        echo "[INFO] Using existing repo at $INSTALL_DIR"
    elif [ -d "$INSTALL_DIR" ] && [ -n "$(ls -A "$INSTALL_DIR" 2>/dev/null)" ]; then
        echo "[ERR] Directory $INSTALL_DIR exists and is not empty."
        exit 1
    else
        echo "[INFO] Cloning repo..."
        git clone --depth 1 --branch "$REF" "$REPO_URL" "$INSTALL_DIR"
    fi

    cd "$INSTALL_DIR"
    chmod +x MineOS.sh
    exec ./MineOS.sh --build "${FORWARD_ARGS[@]}"
fi

if ! command_exists curl; then
    echo "[ERR] curl is required to download the install bundle."
    exit 1
fi
if ! command_exists tar; then
    echo "[ERR] tar is required to extract the install bundle."
    exit 1
fi

if [ -z "$BUNDLE_URL" ]; then
    BUNDLE_URL=$(get_latest_bundle_url "mineos-install-bundle.tar.gz")
fi

if [ -z "$BUNDLE_URL" ]; then
    echo "[ERR] Unable to locate install bundle URL."
    exit 1
fi

tmp_dir=$(mktemp -d)
bundle_path="${tmp_dir}/mineos-install-bundle.tar.gz"
echo "[INFO] Downloading install bundle..."
curl -fsSL "$BUNDLE_URL" -o "$bundle_path"

mkdir -p "$INSTALL_DIR"
tar -xzf "$bundle_path" -C "$INSTALL_DIR"

cd "$INSTALL_DIR"
chmod +x MineOS.sh
exec ./MineOS.sh "${FORWARD_ARGS[@]}"
