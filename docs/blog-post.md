# I got tired of my kid asking me to "install mods" so I built a whole Minecraft server manager. It's free. You're welcome.

**TL;DR:** I'm a dad who hosts Minecraft servers for my kid and their friends. I got sick of SSH-ing into a box every time someone wanted a new modpack, so I built [MineOS](https://github.com/freeman412/mineos-sveltekit) — a free, open-source web UI that lets you create, manage, and mod Minecraft servers from your browser. One-command install, Docker-based, runs on basically anything. My 13-year-old can use it. Your 13-year-old can probably use it too.

---

## Standing on the Shoulders of Giants

Before I get into my version, I want to give a huge shoutout to **hexparrot** (William Dizon), who created the [original MineOS](https://github.com/hexparrot/mineos-node). That project was a lifesaver for the self-hosting community for *years* and is honestly what inspired me to go down this road in the first place. Hexparrot built the foundation that proved a web-based Minecraft server manager was not only possible but genuinely useful. This project carries the MineOS name forward with his blessing and builds on that legacy with a modern tech stack. So — thank you, hexparrot. Seriously.

---

## The Origin Story (a.k.a. "Dad, can you add Forge?")

It started the way these things always start. My kid wanted to play Minecraft with friends. Cool, I'll spin up a server. No problem.

Then it was "can you install Forge?" Then "can you add this mod?" Then "actually can you make a SECOND server with a different modpack?" Then their friend's parent messaged me asking if I could host one for *their* kid too.

Before I knew it, I was managing multiple Minecraft servers from the command line like some kind of mass-producing block game sysadmin. Editing `server.properties` in vim at 10pm on a Tuesday because someone wanted to change the difficulty to Hard.

There had to be a better way.

So I built one. Well — me and a very patient AI.

---

## What Is MineOS?

MineOS is a **web-based Minecraft server manager**. You open it in your browser, and you can:

- **Create servers** in a few clicks (Vanilla, Forge, Paper, Fabric, Spigot, and more — with Bedrock support coming soon)
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

### Multiple Server Types
Vanilla, Forge, Fabric, Paper, Spigot, BungeeCord, FTB modpacks... if it runs Java Minecraft, MineOS probably supports it. Bedrock support is coming soon too!

---

## "But I'm Not Technical"

That's kind of the whole point. I built this because I *am* technical and I was still annoyed by how much work it was. If you can install Docker Desktop (it's a normal app installer, next-next-finish style) and double-click a script, you can run MineOS.

And if you ARE technical — the whole thing is open source. SvelteKit frontend, ASP.NET Core API, Docker Compose orchestration. Fork it, hack on it, submit a PR. It's all on GitHub.

---

## What It Runs On

Basically anything that runs Docker:
- **An old laptop** sitting in the corner
- **A NAS** (TrueNAS support coming soon!)
- **A Raspberry Pi** (if you're feeling adventurous)
- **A cloud VM** (AWS, Azure, whatever — just open the ports)
- **Your main PC** (if you don't mind it running in the background)

I personally run it on an old desktop that was collecting dust. Handles 3-4 servers with mods no problem.

---

## The Secret Weapon: AI-Assisted Development

Okay, here's the part that might raise some eyebrows, but I think it's worth being transparent about: **a massive portion of this project was built with AI.**

I'm not a professional software developer. I'm a dad with some tech skills and a problem to solve. What I *do* have is a clear vision of what I want this thing to do, and the ability to describe it, test it, and steer the ship. The actual code — the SvelteKit frontend, the C# backend, the Docker orchestration — a lot of that was written by Claude (Anthropic's AI) through tools like Claude Code.

And honestly? I think that's the future and I'm not embarrassed about it. Here's why:

**I'm spending real money (tokens) on this project.** Every feature, every bug fix, every refactor — those are API calls that cost actual dollars. I'm investing my own money into making this tool better for everyone. When I say this project is a labor of love, I mean it literally — both my time *and* my wallet.

**AI doesn't replace the human.** I still have to:
- Decide what to build and why
- Test everything (AI writes bugs too, trust me)
- Understand the architecture well enough to know when something is wrong
- Write the prompts, review the output, iterate
- Make the judgment calls about UX, priorities, and what "done" looks like

Think of it like this: AI is the power tool, but I'm still the one building the house. You wouldn't say a carpenter "didn't really build it" because they used a nail gun instead of a hammer.

**The result speaks for itself.** The app works. It's well-structured. It has real features that solve real problems. I care more about whether it helps you manage your kid's Minecraft servers than whether every line of code was hand-typed by a human.

If you're a parent or hobbyist who's been intimidated by open source because you're "not a real developer" — this project is proof that you don't have to be. You just need a clear idea, the right tools, and stubbornness. Lots of stubbornness.

**Want to contribute?** You don't need to know how to code either. File issues describing what you want. Write detailed bug reports. The AI can help with the implementation — what it *can't* do is tell me what parents actually need. That's where you come in.

---

## The Roadmap (a.k.a. Things I'm Working On)

This is actively maintained. Some things coming up:

- **Bedrock server support** (for the mobile/console Minecraft crowd)
- **TrueNAS deployment** (for the NAS-hosting community)
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

If you try it out, I'd genuinely love to hear how it goes. File an issue if something breaks, or just drop a comment. I'm one dad building this in my free time with the help of AI and the community, so every bit of feedback helps.

---

## FAQ

**Q: Is this the same as the old MineOS?**
A: Same name, completely different project. This is a ground-up rewrite with modern tech. The old MineOS was great for its time, but this is built for 2025 and beyond.

**Q: Does it cost anything?**
A: Nope. Free. Open source. No premium tier, no "pay to unlock more than 2 servers," none of that.

**Q: Can my kid break anything important?**
A: With the right permission settings, the worst they can do is crash their own Minecraft server. Which they will. And that's fine. That's how they learn.

**Q: Does it work with Bedrock / mobile Minecraft?**
A: Not yet, but Bedrock support is actively being worked on and is coming soon!

**Q: How many servers can I run?**
A: As many as your hardware can handle. Each Minecraft server wants ~1-2GB of RAM minimum, so plan accordingly.

**Q: Can I access it from outside my house?**
A: Yes — set up port forwarding on your router (or use a cloud VM) and you can manage your servers from anywhere. You can put a reverse proxy like Nginx or Caddy in front of it for HTTPS too.

---

*If this helps even one other parent avoid the "dad can you SSH into the server and install OptiFine" phone call at dinner, it was worth it.*

**[GitHub Link](https://github.com/freeman412/mineos-sveltekit)** | **Free & Open Source** | **Star it if you like it** ⭐
