# MineOS CLI

This directory contains the scaffold for the MineOS management CLI.

## Goals
- Single cross-platform binary (Go)
- Clean architecture (domain, application, infrastructure, presentation)
- Minimal bootstrap scripts (install.sh / install.ps1)

## Commands (scaffolded)
- `mineos` - launch the TUI dashboard (default)
- `mineos api-key refresh` - refresh MINEOS_API_KEY from local sqlite
- `mineos config` - show resolved configuration
- `mineos health` - API health check
- `mineos interactive` - REPL-style shell (aliases: shell, repl)
- `mineos install` - interactive installer (creates .env and starts services)
- `mineos reconfigure` - update .env interactively
- `mineos servers list` - list servers
- `mineos servers stop-all --timeout 300` - stop all servers
- `mineos servers start <name>` / `stop` / `restart` / `kill`
- `mineos stack up|stop|restart|down|pull|build|recreate|rebuild|rebuild-source|ps|logs`
- `mineos stack update` / `mineos stack update-source`
- `mineos status` - basic API + env status
- `mineos tui` - full-screen dashboard (alias: ui)
- `mineos uninstall` - remove containers, optionally data (interactive)
- `mineos version`

## Development
```bash
cd tools/mineos-cli
# go build ./cmd/mineos
```
