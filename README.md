# MineOS - Minecraft Server Manager

A simple web interface to create and manage Minecraft servers. Run as many servers as you want, install mods with one click, and manage everything from your browser.

## One-Click Install

**What you need:**
- [Docker Desktop](https://www.docker.com/products/docker-desktop) (Windows/Mac) or Docker + Docker Compose (Linux)
- That's it!

**Installation:**

**Windows:** Right-click `install.ps1` → Run with PowerShell

**Mac/Linux:** Open Terminal, run `./install.sh`

The installer does everything automatically:
- ✓ Checks that Docker is installed
- ✓ Creates all necessary files and folders
- ✓ Starts MineOS in the background
- ✓ Sets up automatic startup with Docker

**Access MineOS:** Open [http://localhost:3000](http://localhost:3000) in your browser

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
Then try running `install.ps1` again.

**Script won't run on Mac/Linux?**
```bash
chmod +x install.sh uninstall.sh
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
