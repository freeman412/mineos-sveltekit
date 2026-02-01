# MineOS CLI

A cross-platform command-line interface for managing MineOS Minecraft server installations.

## Features

- **Interactive Installer**: Easy setup with guided prompts or fully automated via flags
- **TUI Dashboard**: Full-screen terminal interface for server management
- **Server Control**: Start, stop, restart, and monitor Minecraft servers
- **Log Streaming**: Real-time log viewing from servers
- **Stack Management**: Docker Compose control for the MineOS stack

## Installation

### From Release (Recommended)

Download the latest release for your platform from the [releases page](https://github.com/freemancraft/mineos-sveltekit/releases).

### From Source

```bash
cd tools/mineos-cli
go build -o mineos ./cmd/mineos
```

## Quick Start

### Interactive Installation

Run the installer with guided prompts:

```bash
./mineos install
```

### Automated Installation

For scripted/CI deployments, use quiet mode with all required flags:

```bash
./mineos install --quiet \
  --admin myuser \
  --password mysecurepassword \
  --web-origin http://localhost:3000 \
  --minecraft-host localhost
```

### Launch the TUI

After installation, start the terminal dashboard:

```bash
mineos tui
# or simply:
mineos
```

## Commands

### Core Commands

| Command | Description |
|---------|-------------|
| `mineos` | Launch the TUI dashboard (default) |
| `mineos tui` | Full-screen terminal dashboard |
| `mineos interactive` | REPL-style command shell |
| `mineos install` | Interactive installer |
| `mineos uninstall` | Remove MineOS installation |
| `mineos version` | Show CLI version |

### Server Management

| Command | Description |
|---------|-------------|
| `mineos servers list` | List all servers |
| `mineos servers start <name>` | Start a server |
| `mineos servers stop <name>` | Stop a server |
| `mineos servers restart <name>` | Restart a server |
| `mineos servers kill <name>` | Force kill a server |
| `mineos servers stop-all` | Stop all running servers |
| `mineos servers logs <server>` | Stream Minecraft server logs |

### Stack Management

| Command | Description |
|---------|-------------|
| `mineos stack up` | Start MineOS services |
| `mineos stack stop` | Stop services |
| `mineos stack restart` | Restart services |
| `mineos stack down` | Stop and remove containers |
| `mineos stack pull` | Pull latest images |
| `mineos stack build` | Build images from source |
| `mineos stack ps` | Show container status |
| `mineos stack logs` | View Docker logs |
| `mineos stack update` | Pull and recreate services |

Shortcuts (same as `stack`):
- `mineos start` / `mineos stop` / `mineos restart`
- `mineos logs [service]` (Docker compose logs)
- `mineos pull` / `mineos ps` / `mineos down`

### Status & Configuration

| Command | Description |
|---------|-------------|
| `mineos status` | Show installation status |
| `mineos health` | Check API health |
| `mineos config` | Show resolved configuration |
| `mineos reconfigure` | Update .env interactively |
| `mineos api-key refresh` | Regenerate API key |

## Install Command Options

### Interactive Mode (Default)

```bash
mineos install
```

Guides you through setup with explanations for each option.

### Quiet Mode (Scripted)

```bash
mineos install --quiet --admin <user> --password <pass> [options]
```

Required flags in quiet mode:
- `--admin` - Admin username
- `--password` - Admin password

Optional flags (with defaults):
- `--host-dir` - Server storage directory (default: `./minecraft`)
- `--data-dir` - Database directory (default: `./data`)
- `--api-port` - API port (default: `5078`)
- `--web-port` - Web UI port (default: `3000`)
- `--web-origin` - Web UI URL (default: `http://localhost:3000`)
- `--minecraft-host` - Minecraft server address (default: `localhost`)
- `--body-size-limit` - Upload size limit (default: `Infinity`)
- `--network-mode` - Docker network mode: `bridge` or `host` (default: `bridge`)
- `--build` - Build from source instead of pulling images
- `--image-tag` - Image tag to pull (default: `latest`)
- `--skip-path-install` - Skip PATH installation prompt
- `--api-key` - Custom API key (auto-generated if not provided)

### Examples

Basic automated install:
```bash
mineos install -q --admin admin --password secretpass
```

Custom ports and origin:
```bash
mineos install -q \
  --admin admin \
  --password secretpass \
  --web-port 8080 \
  --web-origin http://192.168.1.100:8080 \
  --minecraft-host 192.168.1.100
```

Build from source (developers):
```bash
mineos install -q \
  --admin admin \
  --password secretpass \
  --build
```

## Minecraft Logs Command

Stream real-time logs from a Minecraft server:

```bash
# Default (combined logs)
mineos servers logs myserver

# Specific log source
mineos servers logs myserver --source server
mineos servers logs myserver --source java
mineos servers logs myserver --source crash
```

Press Ctrl+C to stop streaming.

## Docker Logs Command

Stream real-time Docker Compose logs:

```bash
# All services (default)
mineos logs

# A single service (example)
mineos logs api
```

## Uninstall Command

Remove MineOS installation:

```bash
mineos uninstall
```

Options:
- **Light** - Stop services only
- **Standard** - Remove containers and images
- **Full** - Remove containers, images, and volumes (data)
- **Complete** - Remove everything including CLI and installation directory

Use `--yes` to skip confirmation prompts (for scripted uninstall).

## TUI Keybindings

| Key | Action |
|-----|--------|
| `d` | Dashboard view |
| `s` | Servers view |
| `t` | Stack view |
| `l` | Logs view |
| `m` | Menu |
| `e` | Settings |
| `q` | Quit |
| `r` | Refresh data |
| `?` | Help |
| `j/k` or arrows | Navigate |
| `Enter` | Select |

## Configuration

The CLI reads configuration from `.env` in the current directory. Key variables:

- `MINEOS_API_KEY` - API authentication key
- `API_PORT` - Backend API port
- `WEB_PORT` - Web UI port
- `WEB_ORIGIN_PROD` - Web UI URL
- `PUBLIC_MINECRAFT_HOST` - Minecraft server address

Use `mineos config` to view resolved configuration.

## Architecture

```
tools/mineos-cli/
├── cmd/mineos/          # Main entry point
├── internal/
│   ├── app/             # Application bootstrap
│   ├── application/     # Use cases
│   ├── domain/          # Core types and interfaces
│   ├── infrastructure/  # API client, env loading
│   └── presentation/    # CLI commands and TUI
```

## Requirements

- Docker and Docker Compose
- Go 1.21+ (for building from source)

## Troubleshooting

### Docker not running
```
Error: docker is not running - please start Docker Desktop or the Docker daemon
```
Start Docker Desktop or the Docker service before running install.

### API key issues
```
Error: api key missing
```
Run `mineos api-key refresh` to regenerate from the database, or check your `.env` file.

### Port conflicts
If default ports are in use, specify alternatives:
```bash
mineos install --api-port 5079 --web-port 3001
```
