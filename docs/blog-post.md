# I got tired of my kid asking me to "install mods" so I built a whole Minecraft server manager. It's free. You're welcome.

**TL;DR:** I'm a dad who hosts Minecraft servers for my kid and their friends. I got sick of SSH-ing into a box every time someone wanted a new modpack, so I built [MineOS](https://github.com/freeman412/mineos-sveltekit) — a free, open-source web UI that lets you create, manage, and mod Minecraft servers from your browser. One-command install, Docker-based, runs on basically anything. My 13-year-old can use it. Your 13-year-old can probably use it too.

---

## The Origin Story (a.k.a. "Dad, can you add Forge?")

It started the way these things always start. My kid wanted to play Minecraft with friends. Cool, I'll spin up a server. No problem.

Then it was "can you install Forge?" Then "can you add this mod?" Then "actually can you make a SECOND server with a different modpack?" Then their friend's parent messaged me asking if I could host one for *their* kid too.

Before I knew it, I was managing multiple Minecraft servers from the command line like some kind of mass-producing block game sysadmin. Editing `server.properties` in vim at 10pm on a Tuesday because someone wanted to change the difficulty to Hard.

There had to be a better way.

So I built one.

---

## What Is MineOS?

MineOS is a **web-based Minecraft server manager**. You open it in your browser, and you can:

- **Create servers** in a few clicks (Vanilla, Forge, Paper, Fabric, Spigot, Bedrock — 19 server types total)
- **Install mods** directly from CurseForge without touching a file system
- **Start, stop, restart** servers with buttons instead of terminal commands
- **Manage players** — whitelist, ban, OP, the works
- **Back up worlds** and restore them when things go sideways (and they will)
- **Monitor performance** — see TPS, memory usage, player counts in real time
- **Give your kids (or their friends' parents) their own accounts** with limited permissions

It looks like this:

![Screenshot: Server List — your servers at a glance with status, player counts, and quick actions](docs/screenshots/servers.png)

*All your servers in one place. Green means running. Red means someone crashed it with too many TNT cannons.*

---

## The "My Kid Can Do It" Test

Here's what I'm most proud of: **my teenager can actually use this thing.**

Creating a new server is a guided process. Pick a name, pick a server type, pick a version, done.

![Screenshot: Create Server wizard — step-by-step server creation](docs/screenshots/create-server.png)

![Screenshot: Server configuration — setting up game properties](docs/screenshots/create-server-2.png)

![Screenshot: Profile selection — choose from Vanilla, Forge, Paper, Fabric, and more](docs/screenshots/create-server-3.png)

Want Forge mods? There's a built-in CurseForge browser. Search for a mod, click install. That's it. No downloading JARs, no dragging files into folders, no "dad it says ClassNotFoundException."

![Screenshot: Installing Forge — one-click mod installation from CurseForge](docs/screenshots/installing-forge.png)

I've actually been using this as a teaching tool. My older kid and a couple of their friends are learning:
- Basic server administration
- How mods and plugins work
- What ports are (and why you can't run two things on the same one)
- How to read logs when something breaks
- The ancient art of "have you tried restarting it"

It's genuinely been a great way to sneak in some tech education while they think they're just playing Minecraft.

---

## The Setup (It's Stupid Easy)

You need one thing: **Docker**. That's it.

**Mac/Linux:**
```bash
./MineOS.sh
```

**Windows:**
Right-click `MineOS.ps1` → Run with PowerShell

The script handles everything — checks Docker, creates config files, pulls containers, sets up the database. When it's done, open `http://localhost:3000` and you're in.

No Java to install. No PATH variables to set. No "which JDK version do I need." Docker handles all of that inside the container.

It also starts automatically when Docker starts, so after a reboot your servers just... come back. Like magic. Parent-friendly magic.

---

## Features That Actually Matter (To Parents)

### User Accounts With Permissions
You can create accounts for other people and control exactly what they can do. Want your kid to be able to start/stop the server but not delete it? Done. Want another parent to be able to manage the whitelist but not touch the console? Done.

### Backups
One-click backups. One-click restore. Because *someone* is going to "accidentally" blow up the spawn point with 400 blocks of TNT and then look at you with puppy eyes asking if you can fix it.

### Real-Time Console
Full server console in the browser. Send commands, watch logs, see what's happening. Useful for debugging, or for catching who *actually* griefed the house.

![Screenshot: Server Dashboard — console, players, performance monitoring all in one place](docs/screenshots/server-page.png)

### Whitelist Management
Add and remove players right from the UI. No more editing JSON files.

### Discord Notifications
Get webhook notifications when servers start, stop, or when players join. Because sometimes you just want to know your kid is playing Minecraft instead of doing homework. (I'm kidding. Mostly.)

### 19 Server Types
Vanilla, Forge, Fabric, Paper, Spigot, BungeeCord, Bedrock, FTB modpacks... if it runs Minecraft, MineOS probably supports it.

---

## "But I'm Not Technical"

That's kind of the whole point. I built this because I *am* technical and I was still annoyed by how much work it was. If you can install Docker Desktop (it's a normal app installer, next-next-finish style) and double-click a script, you can run MineOS.

And if you ARE technical — the whole thing is open source. SvelteKit frontend, ASP.NET Core API, Docker Compose orchestration, Caddy reverse proxy with automatic HTTPS. Fork it, hack on it, submit a PR. It's all on GitHub.

---

## What It Runs On

Basically anything that runs Docker:
- **An old laptop** sitting in the corner
- **A NAS** (TrueNAS support included!)
- **A Raspberry Pi** (if you're feeling adventurous)
- **A cloud VM** (AWS, Azure, whatever — just open the ports)
- **Your main PC** (if you don't mind it running in the background)

I personally run it on an old desktop that was collecting dust. Handles 3-4 servers with mods no problem.

---

## The Roadmap (a.k.a. Things I'm Working On)

This is actively maintained. Some things coming up:

- **OAuth login** (sign in with Discord/Google)
- **Invite links** with auto-whitelist (send a link, friend joins, automatically whitelisted)
- **Mod dependency auto-resolution** (no more "you need Library X for this mod to work")
- **Performance graphs** over time
- **Scheduled backups** via cron
- **Crash detection and auto-restart**

---

## Can I See It / Try It?

- **GitHub:** [github.com/freeman412/mineos-sveltekit](https://github.com/freeman412/mineos-sveltekit)
- **License:** Open source and free

If you try it out, I'd genuinely love to hear how it goes. File an issue if something breaks, or just drop a comment. I'm one dad building this in my free time (with some help from the community), so every bit of feedback helps.

---

## FAQ

**Q: Is this the same as the old MineOS?**
A: Same name, completely different project. This is a ground-up rewrite with modern tech. The old MineOS was great for its time, but this is built for 2025 and beyond.

**Q: Does it cost anything?**
A: Nope. Free. Open source. No premium tier, no "pay to unlock more than 2 servers," none of that.

**Q: Can my kid break anything important?**
A: With the right permission settings, the worst they can do is crash their own Minecraft server. Which they will. And that's fine. That's how they learn.

**Q: Does it work with Bedrock / mobile Minecraft?**
A: Yes! Bedrock server support is included.

**Q: How many servers can I run?**
A: As many as your hardware can handle. Each Minecraft server wants ~1-2GB of RAM minimum, so plan accordingly.

**Q: Can I access it from outside my house?**
A: Yes — MineOS includes a Caddy reverse proxy that supports automatic HTTPS. Set up port forwarding on your router (or use a cloud VM) and you can manage your servers from anywhere.

---

*If this helps even one other parent avoid the "dad can you SSH into the server and install OptiFine" phone call at dinner, it was worth it.*

**[GitHub Link](https://github.com/freeman412/mineos-sveltekit)** | **Free & Open Source** | **Star it if you like it** ⭐
