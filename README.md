# MineOS - Minecraft Server Manager

A simple web interface to create and manage Minecraft servers. Run as many servers as you want, install mods with one click, and manage everything from your browser.

## MineOS Script (Setup + Management)

**What you need:**
- [Docker Desktop](https://www.docker.com/products/docker-desktop) (Windows/Mac) or Docker + Docker Compose (Linux)
- That's it!

**Run the script:**

**Windows:** Right-click `MineOS.ps1` -> Run with PowerShell

**Mac/Linux:** Open Terminal, run `./MineOS.sh`

The script can:
- Check Docker + Docker Compose
- Create all necessary files and folders
- Start/stop/restart MineOS
- Rebuild or update when needed

**Access MineOS:** Open [http://localhost:3000](http://localhost:3000) in your browser

## Script Usage

Once configured, use the MineOS script to manage services:

**Windows (PowerShell):**
- `MineOS.ps1` (interactive menu)
- `MineOS.ps1 -Dev` (API only + web dev env setup)

**Mac/Linux:**
- `./MineOS.sh` (interactive menu)
- `./MineOS.sh --dev` (API only + web dev env setup)

## That's It!

MineOS will start automatically whenever Docker starts. Create servers, install mods, and manage everything from the web interface.

## Uninstall

**Windows:** Run `uninstall.ps1`

**Mac/Linux:** Run `./uninstall.sh`

The uninstall script will ask if you want to keep your server data before removing anything.

## Where Are My Files?

- **Server data:** `./minecraft/` folder (all your Minecraft servers)
- **Database:** `./data/mineos.db` (MineOS settings)

## Troubleshooting

**Script won't run on Windows?**
Open PowerShell as Administrator and run:
```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
```
Then try running `MineOS.ps1` again.

**Script won't run on Mac/Linux?**
```bash
chmod +x MineOS.sh uninstall.sh
```

**Need to change ports?**
Edit `.env` and run `docker compose restart`

## Screenshots

![Server List](docs/screenshots/servers.png)

![Create Server](docs/screenshots/create-server.png)

![Server Configuration](docs/screenshots/create-server-2.png)

![Profile Selection](docs/screenshots/create-server-3.png)

![Advanced Settings](docs/screenshots/create-server-4.png)

![Installing Forge](docs/screenshots/installing-forge.png)

![Server Dashboard](docs/screenshots/server-page.png)
