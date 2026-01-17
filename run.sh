#!/bin/bash

set -e

echo "[WARN] run.sh is deprecated; use install.sh instead."

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec "${SCRIPT_DIR}/install.sh" "$@"
