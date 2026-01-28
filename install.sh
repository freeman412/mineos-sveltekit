#!/usr/bin/env bash

set -euo pipefail

REPO_URL="https://github.com/freeman412/mineos-sveltekit.git"
INSTALL_DIR="${MINEOS_INSTALL_DIR:-mineos}"
REF="main"
BUILD=false
BUNDLE_URL=""
CLI_URL=""
INSTALL_CLI=true
VERSION=""
FORWARD_ARGS=()

usage() {
    cat <<'EOF'
MineOS installer

Usage:
  curl -fsSL https://mineos.net/install.sh | bash
  curl -fsSL https://mineos.net/install.sh | bash -s -- --build
  curl -fsSL https://mineos.net/install.sh | bash -s -- --version v1.0.0

Options:
  --build           Clone repo and build from source
  --ref <ref>       Git ref for --build (default: main)
  --dir <path>      Install directory (default: ./mineos)
  --repo <url>      Git repo for --build
  --version <tag>   Download specific release version (e.g., v1.0.0)
  --bundle-url <u>  Override bundle download URL
  --cli-url <u>     Override mineos-cli download URL
  --no-cli          Skip mineos-cli download
  -h, --help        Show this help
EOF
}

command_exists() {
    command -v "$1" >/dev/null 2>&1
}

get_latest_bundle_url() {
    local asset_name="$1"
    local version="${2:-}"
    local api
    if [ -n "$version" ]; then
        api="https://api.github.com/repos/freeman412/mineos-sveltekit/releases/tags/$version"
    else
        api="https://api.github.com/repos/freeman412/mineos-sveltekit/releases/latest"
    fi

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

detect_platform() {
    local os
    local arch

    os=$(uname -s 2>/dev/null | tr '[:upper:]' '[:lower:]')
    case "$os" in
        linux) os="linux" ;;
        darwin) os="darwin" ;;
        *) return 1 ;;
    esac

    arch=$(uname -m 2>/dev/null)
    case "$arch" in
        x86_64|amd64) arch="amd64" ;;
        arm64|aarch64) arch="arm64" ;;
        *) return 1 ;;
    esac

    echo "${os}:${arch}"
}

extract_zip() {
    local zip_path="$1"
    local dest_dir="$2"

    mkdir -p "$dest_dir"
    if command_exists unzip; then
        unzip -o -q "$zip_path" -d "$dest_dir"
        return
    fi
    if command_exists python3; then
        python3 - <<'PY' "$zip_path" "$dest_dir"
import sys, zipfile
zip_path = sys.argv[1]
dest = sys.argv[2]
with zipfile.ZipFile(zip_path, 'r') as zf:
    zf.extractall(dest)
PY
        return
    fi
    if command_exists python; then
        python - <<'PY' "$zip_path" "$dest_dir"
import sys, zipfile
zip_path = sys.argv[1]
dest = sys.argv[2]
with zipfile.ZipFile(zip_path, 'r') as zf:
    zf.extractall(dest)
PY
        return
    fi
    return 1
}

install_cli() {
    if [ "$INSTALL_CLI" != true ]; then
        return 0
    fi

    platform=$(detect_platform || true)
    if [ -z "$platform" ]; then
        echo "[WARN] Unsupported platform for mineos-cli (expected linux/darwin amd64/arm64)."
        return 0
    fi

    os="${platform%%:*}"
    arch="${platform##*:}"
    asset="mineos-cli_${os}_${arch}.zip"
    local_bundle_zip="./cli/${asset}"
    cli_zip="${tmp_dir}/${asset}"
    if [ -f "$local_bundle_zip" ]; then
        echo "[INFO] Using bundled mineos-cli (${os}/${arch})..."
        cp "$local_bundle_zip" "$cli_zip"
    else
        if [ -z "$CLI_URL" ]; then
            CLI_URL=$(get_latest_bundle_url "$asset" "$VERSION")
        fi
        if [ -z "$CLI_URL" ]; then
            echo "[WARN] Unable to locate mineos-cli asset for ${os}/${arch}. Skipping."
            return 0
        fi
        echo "[INFO] Downloading mineos-cli (${os}/${arch})..."
        curl -fsSL "$CLI_URL" -o "$cli_zip"
    fi
    if extract_zip "$cli_zip" "$tmp_dir/cli"; then
        bin_name="mineos-${os}-${arch}"
        if [ -f "$tmp_dir/cli/$bin_name" ]; then
            mv "$tmp_dir/cli/$bin_name" "./mineos"
            chmod +x "./mineos"
            echo "[INFO] Installed mineos-cli to ${INSTALL_DIR}/mineos"
        else
            echo "[WARN] mineos-cli binary not found in archive."
        fi
    else
        echo "[WARN] unzip/python not available; skipping mineos-cli install."
    fi
}

run_cli_installer() {
    local cli="./mineos"
    if [ ! -x "$cli" ]; then
        if command_exists mineos; then
            cli="mineos"
        else
            echo "[ERR] mineos-cli not found. Re-run without --no-cli."
            exit 1
        fi
    fi

    "$cli" install "$@"
}

while [ $# -gt 0 ]; do
    case "$1" in
        --build) BUILD=true ;;
        --ref) REF="${2:-}"; shift ;;
        --dir) INSTALL_DIR="${2:-}"; shift ;;
        --repo) REPO_URL="${2:-}"; shift ;;
        --version) VERSION="${2:-}"; shift ;;
        --bundle-url) BUNDLE_URL="${2:-}"; shift ;;
        --cli-url) CLI_URL="${2:-}"; shift ;;
        --no-cli) INSTALL_CLI=false ;;
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
    tmp_dir=$(mktemp -d)
    install_cli
    run_cli_installer --build "${FORWARD_ARGS[@]}"
    exit 0
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
    BUNDLE_URL=$(get_latest_bundle_url "mineos-install-bundle.tar.gz" "$VERSION")
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
install_cli
run_cli_installer "${FORWARD_ARGS[@]}"
