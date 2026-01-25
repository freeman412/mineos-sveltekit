#!/usr/bin/env bash
set -euo pipefail

if [ "${DEV_INSTALL_GIT:-true}" != "false" ]; then
    if ! command -v git >/dev/null 2>&1; then
        apt-get update -y >/dev/null
        apt-get install -y git openssh-client >/dev/null
    fi
fi

if command -v git >/dev/null 2>&1; then
    if [ -n "${GIT_USER_NAME:-}" ]; then
        git config --global user.name "${GIT_USER_NAME}"
    fi
    if [ -n "${GIT_USER_EMAIL:-}" ]; then
        git config --global user.email "${GIT_USER_EMAIL}"
    fi
    if [ -n "${GIT_DEFAULT_BRANCH:-}" ]; then
        git config --global init.defaultBranch "${GIT_DEFAULT_BRANCH}"
    fi
    if [ -n "${GIT_SAFE_DIRECTORY:-}" ]; then
        git config --global --add safe.directory "${GIT_SAFE_DIRECTORY}"
    else
        git config --global --add safe.directory "/workspace"
    fi
fi
