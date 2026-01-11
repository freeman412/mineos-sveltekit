# Quick Vanilla Server Test Guide

## TL;DR Test Steps

1. **Create Server**
   - Go to `/dashboard`
   - Click "+ Create Server"
   - Enter name: `test-vanilla`
   - Select Vanilla profile (e.g., `vanilla-1.20.4`)
   - Click "Create Server"

2. **Accept EULA**
   - Go to `/servers/test-vanilla`
   - Click "📜 Accept EULA" button
   - See success alert

3. **Start Server**
   - Click "▶️ Start Server" button
   - Wait ~30 seconds for startup
   - Status changes to "Running"

4. **View Console**
   - Go to `/servers/test-vanilla/console`
   - See real-time logs in XTerm terminal
   - Wait for "Done! For help, type 'help'" message

5. **Send Commands**
   - Type `list` in console input
   - Press Enter
   - See response in terminal

6. **Stop Server**
   - Return to `/servers/test-vanilla`
   - Click "⏹️ Stop Server"
   - Status changes to "Stopped"

## What Gets Created

```
/var/games/minecraft/
├── profiles/
│   └── vanilla-1.20.4/
│       └── vanilla-1.20.4.jar          # Downloaded automatically
├── servers/
│   └── test-vanilla/
│       ├── server.config                # MineOS config
│       ├── eula.txt                     # Created by EULA button
│       ├── vanilla-1.20.4.jar          # Copied from profiles
│       ├── server.properties           # Generated on first start
│       ├── logs/
│       │   └── latest.log              # Streamed to console
│       └── world/                      # Created on first start
```

## Verification Commands

```bash
# Check server created
ls -la /var/games/minecraft/servers/test-vanilla/

# Check profile downloaded
ls -la /var/games/minecraft/profiles/vanilla-1.20.4/

# Check EULA
cat /var/games/minecraft/servers/test-vanilla/eula.txt

# Check if server is running
screen -ls  # Should see: mc-test-vanilla
ps aux | grep java | grep test-vanilla

# Watch logs
tail -f /var/games/minecraft/servers/test-vanilla/logs/latest.log

# Test console streaming endpoint
curl http://localhost:5078/api/servers/test-vanilla/console/stream
```

## Common Issues

### Server Won't Start
- **EULA not accepted**: Click "Accept EULA" button
- **Port in use**: Another server using 25565
- **Out of memory**: Server defaults to 256M, check if enough RAM

### Console Not Showing Logs
- Wait 5-10 seconds for log file to be created
- Refresh page
- Check Network tab for SSE connection errors

### Profile Download Failed
- Check internet connection
- Verify profile URL is valid
- Check disk space

## Expected Timeline

- **Server Creation**: 5-10 seconds (first time downloads profile)
- **EULA Acceptance**: Instant
- **Server Startup**: 20-40 seconds (depending on world size)
- **Console Streaming**: Immediate (once log file exists)
- **Server Stop**: 5-10 seconds (graceful shutdown)

## Success Indicators

✅ Wizard completes without errors
✅ Profile JAR appears in `/var/games/minecraft/profiles/`
✅ Server JAR copied to server directory
✅ EULA button creates `eula.txt` with `eula=true`
✅ Start button creates screen session `mc-test-vanilla`
✅ Console shows real-time log streaming
✅ Commands execute and show responses
✅ Stop button terminates Java process cleanly

---

For detailed testing instructions, see [TESTING-VANILLA-SERVER.md](./TESTING-VANILLA-SERVER.md)
