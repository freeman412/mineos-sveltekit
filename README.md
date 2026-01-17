MineOS SvelteKit
================

MineOS is a web UI and API for managing Minecraft servers. This project runs fully in Docker and is intended to be beginner-friendly.

Quick Start
-----------

Requirements:
- Docker Desktop (Windows/macOS) or Docker Engine + Docker Compose (Linux)
- Node.js and .NET SDK are only needed if you plan to build/run without Docker.

Install:

Linux/macOS:
    ./install.sh

Windows (PowerShell):
    .\install.ps1

The installer:
- Checks Docker and Docker Compose.
- Creates a `.env` file with everything needed to start.
- Creates the required folders.
- Starts Docker Compose in the background.

Open the UI at:
- Web UI: http://localhost:3000
- API: http://localhost:5078

Containers are configured with `restart: unless-stopped` and will start automatically when Docker starts.

If your shell blocks scripts:
- Linux/macOS: `chmod +x install.sh uninstall.sh`
- Windows: `Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass`

Manual Docker Compose (optional)
--------------------------------

If you prefer to manage `.env` yourself:

    cp .env.template .env
    docker compose up -d

Uninstall
---------

Linux/macOS:
    ./uninstall.sh

Windows (PowerShell):
    .\uninstall.ps1

The uninstall script includes warnings and lets you keep data or back it up before removal.

Data Locations
--------------

- SQLite database: `./data/mineos.db`
- Minecraft files: path configured by `Host__BaseDirectory` in `.env`

Notes
-----

- The older `run.sh` and `run.ps1` are kept as wrappers and now call the installers.
- If you change ports or DB settings later, re-run the installer to regenerate `.env`.
