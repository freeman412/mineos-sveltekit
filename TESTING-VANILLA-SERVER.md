# Testing Vanilla Minecraft Server Setup

This guide walks through creating and managing a vanilla Minecraft server using the MineOS UI.

## Prerequisites

1. Backend API running on port 5078
2. Frontend running on port 5173
3. Logged in to the UI

## Test Workflow

### Step 1: Create a Vanilla Server

1. Navigate to Dashboard (`http://localhost:5173/dashboard`)
2. Click **"+ Create Server"** button
3. **Step 1 - Server Name:**
   - Enter server name: `test-vanilla`
   - Click **"Next"**
4. **Step 2 - Profile Selection:**
   - Select a **Vanilla** profile (e.g., `vanilla-1.20.4`)
   - Notice "Will download" badge if profile not cached
   - Click **"Create Server"**
5. **Step 3 - Automatic Creation:**
   - Watch spinner as profile downloads (if needed)
   - Server created and JAR copied
   - Redirected to `/servers`

**Expected Results:**
- Server appears in server list with "Stopped" status
- Profile JAR downloaded to `/var/games/minecraft/profiles/vanilla-1.20.4/`
- Server directory created at `/var/games/minecraft/servers/test-vanilla/`
- JAR file copied to server directory
- `server.config` created with JAR file reference

**Verify on Disk:**
```bash
# Check server directory
ls -la /var/games/minecraft/servers/test-vanilla/

# Should see:
# - server.config (with jarfile=vanilla-1.20.4.jar)
# - vanilla-1.20.4.jar (the server JAR)
```

---

### Step 2: Accept EULA

Before starting a Minecraft server for the first time, you must accept the EULA.

**Option A: Via API (curl)**
```bash
curl -X POST http://localhost:5078/api/servers/test-vanilla/eula \
  -H "Authorization: Bearer YOUR_TOKEN"
```

**Option B: Manually**
```bash
echo "eula=true" > /var/games/minecraft/servers/test-vanilla/eula.txt
```

**Expected Results:**
- `eula.txt` file created with `eula=true`

**Verify:**
```bash
cat /var/games/minecraft/servers/test-vanilla/eula.txt
# Should output: eula=true
```

---

### Step 3: Start the Server

1. Navigate to server detail page: `/servers/test-vanilla`
2. Click **"Start"** button
3. Wait for status to change from "Stopped" to "Running"

**Expected Results:**
- Server status badge changes to green "Running"
- Java PID and Screen PID displayed
- Screen session created (named `mc-test-vanilla`)
- Java process running

**Verify:**
```bash
# Check screen sessions
screen -ls
# Should see: mc-test-vanilla

# Check Java process
ps aux | grep java
# Should see: java -Xmx256M -Xms256M -jar vanilla-1.20.4.jar nogui

# Check server log
tail -f /var/games/minecraft/servers/test-vanilla/logs/latest.log
# Should see server startup messages
```

---

### Step 4: View Console Logs

1. Navigate to console page: `/servers/test-vanilla/console`
2. XTerm.js terminal should display with dark theme
3. Logs should stream in real-time via SSE

**Expected Results:**
- Console shows server startup messages
- New log lines appear automatically
- Logs include:
  - "Starting minecraft server version 1.20.4"
  - "Done! For help, type 'help'"
  - Player join/leave messages (if players connect)

**Terminal Features:**
- Scrollback buffer (10,000 lines)
- Command input at bottom
- Auto-scroll to bottom on new messages

---

### Step 5: Send Console Commands

1. In console page, type command at bottom: `list`
2. Press Enter or click "Send"
3. Server response appears in terminal

**Expected Results:**
- Command sent to screen session
- Server responds with player list
- Output appears in console terminal

**Test Commands:**
```
list              # List online players
say Hello!        # Broadcast message
help              # Show available commands
seed              # Show world seed
```

**Verify:**
```bash
# Commands are sent to the screen session
screen -r mc-test-vanilla
# You should see the commands and responses in the screen session
```

---

### Step 6: Monitor Server Status

1. Return to server detail page: `/servers/test-vanilla`
2. Observe real-time status indicators

**Expected Results:**
- Status: "Running" (green badge)
- Java PID: displays process ID
- Screen PID: displays screen session ID
- Port: 25565 (default)
- Players Online: 0 / 20 (if server fully started)

**Note:** Ping/Query data may not appear until:
- Server is fully started (after "Done!" message)
- server.properties has `enable-query=true` (for query protocol)

---

### Step 7: Stop the Server

1. Click **"Stop"** button on server detail page
2. Wait for graceful shutdown (30 second timeout)
3. Status changes to "Stopped"

**Expected Results:**
- Server sends stop command via screen
- Java process terminates gracefully
- Screen session closes
- Status badge changes to "Stopped"
- PIDs removed from UI

**Verify:**
```bash
# Screen session should be gone
screen -ls
# Should NOT see mc-test-vanilla

# Java process should be gone
ps aux | grep java | grep test-vanilla
# Should return nothing

# Server log shows shutdown
tail /var/games/minecraft/servers/test-vanilla/logs/latest.log
# Should see: "Closing Server"
```

---

## Troubleshooting

### Server Won't Start

**Check logs:**
```bash
# Backend API logs
# Check console where MineOS.Api is running

# Server logs
tail -100 /var/games/minecraft/servers/test-vanilla/logs/latest.log

# System logs
journalctl -u mineos -n 100
```

**Common issues:**
1. **EULA not accepted** - See Step 2
2. **Port already in use** - Check if another server is using port 25565
3. **Insufficient memory** - Increase Xmx in server.config
4. **Missing JAR file** - Verify JAR was copied successfully

### Console Not Streaming

**Check:**
1. SSE connection established (Network tab in DevTools)
2. Log file exists and is being written to
3. Backend has read permissions on log file

**Verify SSE endpoint:**
```bash
curl http://localhost:5078/api/servers/test-vanilla/console/stream
# Should stream log lines in SSE format
```

### Profile Download Failed

**Check:**
1. Internet connection
2. Profile URL is valid (check ProfileService defaults)
3. Disk space available
4. Write permissions on profiles directory

---

## Expected File Structure

After successful setup, you should have:

```
/var/games/minecraft/
├── profiles/
│   └── vanilla-1.20.4/
│       └── vanilla-1.20.4.jar
├── servers/
│   └── test-vanilla/
│       ├── server.config          # MineOS config
│       ├── server.properties      # Minecraft config
│       ├── eula.txt               # EULA acceptance
│       ├── vanilla-1.20.4.jar     # Server JAR
│       ├── logs/
│       │   └── latest.log         # Server logs
│       ├── world/                 # World data (after first start)
│       ├── usercache.json
│       ├── banned-players.json
│       └── ops.json
```

---

## Success Criteria

- ✅ Server created through wizard UI
- ✅ Profile downloaded automatically
- ✅ EULA accepted
- ✅ Server starts successfully
- ✅ Console shows real-time logs
- ✅ Commands can be sent via UI
- ✅ Server stops gracefully
- ✅ All files created with correct ownership
- ✅ No errors in backend logs

---

## Next Steps

Once vanilla server is working:
1. Test backup creation
2. Test server properties editing
3. Test archive creation
4. Move on to Phase 6: Mod Management
