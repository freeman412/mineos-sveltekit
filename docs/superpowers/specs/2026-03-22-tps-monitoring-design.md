# TPS Monitoring: Per-Server Control and Loader-Aware Commands

## Problem

TPS monitoring sends `/tps` to every server every 60 seconds, regardless of server type. This causes:

- Log spam on servers that don't support `/tps` (Vanilla, Bedrock)
- Wrong command for Forge servers (needs `/forge tps`)
- No way to disable TPS monitoring per-server
- Users report it clutters the console (GitHub #76)

## Design

### 1. Server Config Extension

Add a `[monitoring]` section to `server.config` (INI file, per-server):

```ini
[monitoring]
tps_enabled=true
tps_command=/tps
```

**Auto-defaults based on server type:**

| Server Type | `tps_enabled` | `tps_command` |
|-------------|--------------|---------------|
| Paper/Spigot/Purpur/Bukkit | `true` | `/tps` |
| Forge | `true` | `/forge tps` |
| NeoForge | `true` | `/neoforge tps` |
| Fabric | `false` | `/tps` |
| Vanilla | `false` | |
| Bedrock | `false` | |

Defaults are applied when the section doesn't exist. Users can override both fields. Setting `tps_command` to a custom value (e.g. `/spark tps`) is supported for modded servers with TPS mods.

### 2. Backend Changes

**PerformanceService.TryGetTpsAsync:** Read the server's `[monitoring]` config before sending a command:
- If `tps_enabled=false`, skip the command and return `null` TPS
- If `tps_enabled=true`, send the configured `tps_command` instead of hardcoded `/tps`

**TPS output parsing:** Support two regex formats:
1. Paper/Spigot: `TPS from last 1m, 5m, 15m: 20.0, 19.9, 20.0`
2. Forge/NeoForge: `Mean TPS: 20.0` (extract from Forge's dimension-based output)

Try both patterns when parsing logs — the first match wins.

**ServerService.CreateServerAsync:** When creating a new server, set smart defaults in the `[monitoring]` section based on the detected server type/profile.

**ServerConfigDto:** Add `Monitoring` section with `TpsEnabled` (bool) and `TpsCommand` (string?) fields to expose through the API.

### 3. UI — Three Locations

**Performance page** (`servers/[name]/performance/+page.svelte`):
- Toggle switch next to the TPS chart area: "TPS Monitoring" on/off
- When disabled, TPS chart shows "TPS monitoring is disabled" placeholder
- Toggle calls the config update API

**Console page** (`servers/[name]/console/+page.svelte` or equivalent):
- Small toggle in the console header/toolbar area: "TPS Monitoring" on/off
- Same API call as performance page

**Config (advanced) page** (`servers/[name]/advanced/+page.svelte`):
- Full monitoring section with:
  - TPS Monitoring toggle (enabled/disabled)
  - TPS Command text input (editable, shows default based on server type)
  - Help text explaining which command to use for different server types

All three locations read/write the same `[monitoring]` section in `server.config`.

### 4. Auto-Detection on Server Creation

When a server is created, the `[monitoring]` section is populated based on the server type:
- Bedrock servers: `tps_enabled=false` (no command set)
- Vanilla Java: `tps_enabled=false`
- Paper/Spigot/Purpur: `tps_enabled=true`, `tps_command=/tps`
- Forge: `tps_enabled=true`, `tps_command=/forge tps`
- NeoForge: `tps_enabled=true`, `tps_command=/neoforge tps`
- Fabric: `tps_enabled=false`, `tps_command=/tps`

For existing servers without a `[monitoring]` section, the system auto-detects defaults from the server's profile/JAR filename (same detection used for mod loader) rather than forcing TPS on all servers.

## Files to Modify

### Backend

| File | Change |
|------|--------|
| `ServerDtos.cs` | Add `MonitoringConfig` record with `TpsEnabled`, `TpsCommand` |
| `ServerService.cs` | Parse/write `[monitoring]` section in server.config; set defaults on create |
| `PerformanceService.cs` | Read monitoring config; use configured command; add Forge TPS regex |
| `IPerformanceService.cs` | Update method signatures if needed |

### Frontend

| File | Change |
|------|--------|
| `types.ts` | Add `MonitoringConfig` to `ServerConfig` type |
| `servers/[name]/performance/+page.svelte` | Add TPS toggle next to chart |
| `servers/[name]/console/+page.svelte` | Add TPS toggle in toolbar |
| `servers/[name]/advanced/+page.svelte` | Add monitoring section with toggle + command input |

## Fixes

Fixes #76
