# MineOS CLI

This directory contains the scaffold for the MineOS management CLI.

## Goals
- Single cross-platform binary (Go)
- Clean architecture (domain, application, infrastructure, presentation)
- Minimal bootstrap scripts (install.sh / install.ps1)

## Commands (scaffolded)
- `mineos config` - show resolved configuration
- `mineos health` - API health check
- `mineos interactive` - REPL-style shell (aliases: shell, repl)
- `mineos install` - interactive installer (creates .env and starts services)
- `mineos servers list` - list servers
- `mineos servers stop-all --timeout 300` - stop all servers
- `mineos servers start <name>` / `stop` / `restart` / `kill`
- `mineos status` - basic API + env status
- `mineos tui` - full-screen dashboard (alias: ui)
- `mineos uninstall` - remove containers, optionally data (interactive)
- `mineos version`

## Development
```bash
cd tools/mineos-cli
# go build ./cmd/mineos
```
