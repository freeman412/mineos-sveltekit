#!/usr/bin/env bash
set -euo pipefail

REMOTE="${1:-https://github.com/freeman412/mineos-sveltekit-truenas-catalog.git}"
BRANCH="${2:-main}"
CATALOG_PATH="deployments/truenas/catalog"
SPLIT_BRANCH="truenas-catalog"

if [[ ! -d "$CATALOG_PATH" ]]; then
  echo "Catalog path not found: $CATALOG_PATH" >&2
  exit 1
fi

git subtree split -P "$CATALOG_PATH" -b "$SPLIT_BRANCH"
git push "$REMOTE" "$SPLIT_BRANCH:$BRANCH"
