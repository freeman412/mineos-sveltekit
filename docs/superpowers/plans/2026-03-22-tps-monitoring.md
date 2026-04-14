# TPS Monitoring Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add per-server TPS monitoring control with loader-aware command defaults and a Forge TPS regex. Fixes #76.

**Architecture:** Extend the existing `[monitoring]` INI section in `server.config` with `tps_enabled` and `tps_command`. `PerformanceService` reads these before sending commands. Auto-detect defaults from server type on creation. Surface toggle in three UI locations.

**Tech Stack:** .NET 8 Minimal API, SvelteKit 5 (Svelte 5 runes), xUnit

**Spec:** `docs/superpowers/specs/2026-03-22-tps-monitoring-design.md`

**Related issues:** Fixes #76

---

## File Map

### Backend — Modified

| File | Responsibility |
|------|----------------|
| `apps/MineOS.Application/Dtos/ServerDtos.cs` | Add `MonitoringConfigDto` record |
| `apps/MineOS.Infrastructure/Services/ServerService.cs` | Parse/write `[monitoring]` section; set defaults on create |
| `apps/MineOS.Infrastructure/Services/PerformanceService.cs` | Read monitoring config; use configured command; add Forge regex |
| `apps/web/src/lib/api/types.ts` | Add `MonitoringConfig` to `ServerConfig` |
| `apps/web/src/routes/(app)/servers/[name]/performance/+page.svelte` | TPS toggle next to chart |
| `apps/web/src/routes/(app)/servers/[name]/console/+page.svelte` | TPS toggle in toolbar |
| `apps/web/src/routes/(app)/servers/[name]/advanced/+page.svelte` | Monitoring section with toggle + command input |

### Tests

| File | Responsibility |
|------|----------------|
| `apps/MineOS.Tests/Unit/TpsParsingTests.cs` | Unit tests for both TPS regex patterns |

---

## Task 1: Add MonitoringConfigDto and Config Parsing

**Files:**
- Modify: `apps/MineOS.Application/Dtos/ServerDtos.cs` (add record after `AutoRestartConfigDto`)
- Modify: `apps/MineOS.Infrastructure/Services/ServerService.cs` (parse/write `[monitoring]` section)

- [ ] **Step 1: Add `MonitoringConfigDto` record**

In `apps/MineOS.Application/Dtos/ServerDtos.cs`, add after the `AutoRestartConfigDto` record:

```csharp
public record MonitoringConfigDto(
    bool TpsEnabled,
    string? TpsCommand);
```

- [ ] **Step 2: Add `Monitoring` to `ServerConfigDto`**

Update the existing `ServerConfigDto` record to include the new field:

```csharp
public record ServerConfigDto(
    JavaConfigDto Java,
    MinecraftConfigDto Minecraft,
    OnRebootConfigDto OnReboot,
    AutoRestartConfigDto AutoRestart,
    MonitoringConfigDto? Monitoring = null);
```

**Important:** The nullable default ensures the API doesn't reject payloads from clients that don't include the monitoring field. In `UpdateServerConfigAsync`, handle `null` by preserving the existing monitoring config from disk.

- [ ] **Step 3: Parse `[monitoring]` section in `GetServerConfigAsync`**

In `apps/MineOS.Infrastructure/Services/ServerService.cs`, find `GetServerConfigAsync` (around line 729). After the `autorestart` section parsing (around line 785), add:

```csharp
// [monitoring] section
var monitoringSection = sections.GetValueOrDefault("monitoring", new Dictionary<string, string>());
// Default to false when section is missing — avoids log spam on existing
// Vanilla/Bedrock servers. Users who want TPS monitoring can enable it.
var tpsEnabled = monitoringSection.GetValueOrDefault("tps_enabled", "false")
    .Equals("true", StringComparison.OrdinalIgnoreCase);
var tpsCommand = monitoringSection.GetValueOrDefault("tps_command", "");
var monitoring = new MonitoringConfigDto(
    tpsEnabled,
    string.IsNullOrWhiteSpace(tpsCommand) ? null : tpsCommand);
```

Update the return statement to include `monitoring`:

```csharp
return new ServerConfigDto(java, minecraft, onReboot, autoRestart, monitoring);
```

- [ ] **Step 4: Write `[monitoring]` section in `UpdateServerConfigAsync`**

In `UpdateServerConfigAsync` (around line 790), add the monitoring section to the sections dictionary being written:

```csharp
["monitoring"] = new Dictionary<string, string>
{
    ["tps_enabled"] = config.Monitoring.TpsEnabled.ToString().ToLower(),
    ["tps_command"] = config.Monitoring.TpsCommand ?? ""
}
```

- [ ] **Step 5: Build and run tests**

Run: `dotnet build && dotnet test --verbosity normal`
Expected: All pass. Some callers of `ServerConfigDto` may need updating if they construct it directly — check for compile errors and fix any that reference the old 4-parameter constructor.

- [ ] **Step 6: Commit**

```bash
git add apps/MineOS.Application/Dtos/ServerDtos.cs \
       apps/MineOS.Infrastructure/Services/ServerService.cs
git commit -m "feat: add [monitoring] config section with tps_enabled and tps_command

Extends server.config INI parsing to include a monitoring section.
Defaults to tps_enabled=true with no explicit command.

Fixes #76

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: Smart Defaults on Server Creation

**Files:**
- Modify: `apps/MineOS.Infrastructure/Services/ServerService.cs` (`CreateServerAsync` method)

- [ ] **Step 1: Add monitoring section to Java server creation**

In `CreateServerAsync`, find where `server.config` is written for Java servers (around line 248-268, the section that writes `[java]`, `[minecraft]`, `[onreboot]`). Add a `[monitoring]` section with defaults based on the profile:

```csharp
// Determine TPS defaults based on server type
var tpsEnabled = "false";
var tpsCommand = "";
if (!string.IsNullOrWhiteSpace(request.ServerType) && request.ServerType != "bedrock")
{
    var profile = request.ServerType?.ToLowerInvariant() ?? "";
    switch (profile)
    {
        case "forge":
            tpsEnabled = "true";
            tpsCommand = "/forge tps";
            break;
        case "neoforge":
            tpsEnabled = "true";
            tpsCommand = "/neoforge tps";
            break;
        case "paper":
        case "spigot":
        case "craftbukkit":
        case "purpur":
            tpsEnabled = "true";
            tpsCommand = "/tps";
            break;
        case "fabric":
        case "quilt":
        case "vanilla":
        default:
            tpsEnabled = "false";
            tpsCommand = "/tps";
            break;
    }
}
```

Append to the config content string:

```
[monitoring]
tps_enabled={tpsEnabled}
tps_command={tpsCommand}
```

- [ ] **Step 2: Ensure Bedrock servers get `tps_enabled=false`**

In the Bedrock server creation path (find where it writes `[bedrock]` section), add:

```
[monitoring]
tps_enabled=false
tps_command=
```

- [ ] **Step 3: Build and test**

Run: `dotnet build && dotnet test --verbosity normal`
Expected: All pass

- [ ] **Step 4: Commit**

```bash
git add apps/MineOS.Infrastructure/Services/ServerService.cs
git commit -m "feat: set TPS monitoring defaults based on server type

Forge: /forge tps, NeoForge: /neoforge tps, Paper/Spigot: /tps.
Vanilla, Fabric, Bedrock: disabled by default.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: PerformanceService — Use Config and Add Forge Regex

**Files:**
- Modify: `apps/MineOS.Infrastructure/Services/PerformanceService.cs`
- Create: `apps/MineOS.Tests/Unit/TpsParsingTests.cs`

- [ ] **Step 1: Write failing tests for TPS parsing**

```csharp
// apps/MineOS.Tests/Unit/TpsParsingTests.cs
using MineOS.Infrastructure.Services;

namespace MineOS.Tests.Unit;

public class TpsParsingTests
{
    [Theory]
    [InlineData("TPS from last 1m, 5m, 15m: 20.00, 19.98, 20.00", 20.00)]
    [InlineData("TPS from last 1m, 5m, 15m: 18.50, 19.00, 19.50", 18.50)]
    [InlineData("[09:30:00 INFO]: TPS from last 1m, 5m, 15m: 15.2, 16.0, 17.5", 15.2)]
    public void ParsePaperTps_Extracts_OneMinute_Value(string line, double expected)
    {
        var result = PerformanceService.TryParseTpsLine(line);
        Assert.NotNull(result);
        Assert.Equal(expected, result.Value, precision: 2);
    }

    [Theory]
    [InlineData("Dim 0 (overworld): Mean tick time: 12.3 ms. Mean TPS: 20.00", 20.00)]
    [InlineData("Dim 0 (overworld): Mean tick time: 45.0 ms. Mean TPS: 18.50", 18.50)]
    [InlineData("Overall: Mean tick time: 10.5 ms. Mean TPS: 19.95", 19.95)]
    public void ParseForgeTps_Extracts_MeanTps(string line, double expected)
    {
        var result = PerformanceService.TryParseTpsLine(line);
        Assert.NotNull(result);
        Assert.Equal(expected, result.Value, precision: 2);
    }

    [Theory]
    [InlineData("Server started.")]
    [InlineData("[09:30:00 INFO]: Player joined")]
    [InlineData("")]
    public void NonTpsLines_Return_Null(string line)
    {
        var result = PerformanceService.TryParseTpsLine(line);
        Assert.Null(result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~TpsParsingTests" --verbosity normal`
Expected: FAIL — `TryParseTpsLine` does not exist

- [ ] **Step 3: Add Forge regex and `TryParseTpsLine` method**

In `apps/MineOS.Infrastructure/Services/PerformanceService.cs`, add a second regex alongside the existing `TpsRegex` (around line 20):

```csharp
private static readonly Regex ForgeTpsRegex = new(
    @"Mean TPS:\s*(?<tps>[\d.]+)",
    RegexOptions.Compiled | RegexOptions.IgnoreCase);
```

Add a public static method for testability:

```csharp
public static double? TryParseTpsLine(string line)
{
    if (string.IsNullOrWhiteSpace(line))
        return null;

    // Try Paper/Spigot format first: "TPS from last 1m, 5m, 15m: X, Y, Z"
    var match = TpsRegex.Match(line);
    if (match.Success && double.TryParse(match.Groups["one"].Value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var paperTps))
        return paperTps;

    // Try Forge/NeoForge format: "Mean TPS: X.XX"
    var forgeMatch = ForgeTpsRegex.Match(line);
    if (forgeMatch.Success && double.TryParse(forgeMatch.Groups["tps"].Value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var forgeTps))
        return forgeTps;

    return null;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~TpsParsingTests" --verbosity normal`
Expected: All PASS

- [ ] **Step 5: Update `TryParseTpsFromLog` to use `TryParseTpsLine`**

In the existing `TryParseTpsFromLog` method (around line 243), replace the inline regex match with a call to `TryParseTpsLine`:

```csharp
private double? TryParseTpsFromLog(string logPath)
{
    // ... existing file reading code (read last 200 lines) ...

    foreach (var line in lines.AsEnumerable().Reverse())
    {
        var tps = TryParseTpsLine(line);
        if (tps.HasValue)
            return tps.Value;
    }

    return null;
}
```

- [ ] **Step 6: Update `TryGetTpsAsync` to read monitoring config**

In `TryGetTpsAsync` (around line 213), read the `[monitoring]` section directly from `server.config` (avoids adding an `IServerService` dependency and potential circular references):

```csharp
private async Task<double?> TryGetTpsAsync(string serverName, CancellationToken cancellationToken)
{
    // Read monitoring config directly from server.config INI file
    var serverPath = Path.Combine(_hostOptions.BaseDirectory, _hostOptions.ServersPathSegment, serverName);
    var configPath = Path.Combine(serverPath, "server.config");
    var tpsEnabled = false;
    var tpsCommand = "/tps";

    if (File.Exists(configPath))
    {
        var configContent = await File.ReadAllTextAsync(configPath, cancellationToken);
        var sections = IniParser.ParseWithSections(configContent);
        var monitoringSection = sections.GetValueOrDefault("monitoring", new Dictionary<string, string>());
        tpsEnabled = monitoringSection.GetValueOrDefault("tps_enabled", "false")
            .Equals("true", StringComparison.OrdinalIgnoreCase);
        var cmd = monitoringSection.GetValueOrDefault("tps_command", "");
        if (!string.IsNullOrWhiteSpace(cmd))
            tpsCommand = cmd;
    }

    if (!tpsEnabled)
        return null;
```

Replace the hardcoded `"/tps"` command (line 231) with the configured command:

```csharp
await _consoleService.SendCommandAsync(serverName, tpsCommand, cancellationToken);
```

Keep the existing rate-limiting and log-parsing logic. Add `using MineOS.Infrastructure.Utilities;` for `IniParser` if not already imported.

**Note:** No new dependencies needed — `PerformanceService` already has `_hostOptions` for path construction. Reading the INI file directly avoids coupling to `IServerService`.

- [ ] **Step 7: Build and run all tests**

Run: `dotnet build && dotnet test --verbosity normal`
Expected: All pass

- [ ] **Step 8: Commit**

```bash
git add apps/MineOS.Infrastructure/Services/PerformanceService.cs \
       apps/MineOS.Tests/Unit/TpsParsingTests.cs
git commit -m "feat: loader-aware TPS commands and Forge regex parsing

Reads [monitoring] config to determine if TPS is enabled and
which command to send. Adds Forge/NeoForge TPS regex pattern
alongside existing Paper/Spigot pattern.

Fixes #76

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: Frontend Types

**Files:**
- Modify: `apps/web/src/lib/api/types.ts`

- [ ] **Step 1: Add `MonitoringConfig` type and update `ServerConfig`**

In `apps/web/src/lib/api/types.ts`, add after `AutoRestartConfig`:

```typescript
export type MonitoringConfig = {
    tpsEnabled: boolean;
    tpsCommand: string | null;
};
```

Update `ServerConfig` to include it:

```typescript
export type ServerConfig = {
    java: JavaConfig;
    minecraft: MinecraftConfig;
    onReboot: OnRebootConfig;
    autoRestart: AutoRestartConfig;
    monitoring: MonitoringConfig;
};
```

- [ ] **Step 2: Commit**

```bash
git add apps/web/src/lib/api/types.ts
git commit -m "feat: add MonitoringConfig type to frontend

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: Performance Page — TPS Toggle

**Files:**
- Modify: `apps/web/src/routes/(app)/servers/[name]/performance/+page.svelte`

- [ ] **Step 1: Read the file to understand current structure**

Read the full file first. Find where TPS-related state and chart rendering happens.

- [ ] **Step 2: Add TPS toggle state and save function**

In the script block, add:

```typescript
async function toggleTps() {
    if (!data.server?.config) return;

    // Fetch fresh config to avoid overwriting stale data
    const freshRes = await fetch(`/api/servers/${data.server.name}/server-config`);
    if (!freshRes.ok) return;
    const freshConfig = await freshRes.json();

    freshConfig.monitoring = freshConfig.monitoring ?? { tpsEnabled: false, tpsCommand: null };
    freshConfig.monitoring.tpsEnabled = !freshConfig.monitoring.tpsEnabled;

    const saveRes = await fetch(`/api/servers/${data.server.name}/server-config`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(freshConfig)
    });

    if (saveRes.ok) {
        // Update local state
        data.server.config.monitoring = freshConfig.monitoring;
    }
}
```

- [ ] **Step 3: Add toggle next to the TPS chart**

Find where the TPS chart or TPS summary card is rendered. Add a toggle switch:

```svelte
<div class="tps-header">
    <h3>TPS</h3>
    <label class="tps-toggle" title={tpsEnabled ? 'Disable TPS monitoring' : 'Enable TPS monitoring'}>
        <input type="checkbox" checked={tpsEnabled} onchange={toggleTps} />
        <span class="toggle-slider"></span>
    </label>
</div>
{#if !tpsEnabled}
    <div class="tps-disabled-notice">TPS monitoring is disabled for this server</div>
{/if}
```

- [ ] **Step 4: Add CSS for toggle and disabled notice**

Use the same `.toggle-slider` pattern from the mods page, plus:

```css
.tps-header {
    display: flex;
    align-items: center;
    gap: 12px;
}

.tps-disabled-notice {
    padding: 20px;
    text-align: center;
    color: #8890b1;
    font-style: italic;
    background: rgba(22, 27, 46, 0.5);
    border-radius: 8px;
}
```

Reuse the `.tps-toggle` / `.toggle-slider` CSS (same as `.mod-toggle` pattern from mods page).

- [ ] **Step 5: Commit**

```bash
git add "apps/web/src/routes/(app)/servers/[name]/performance/+page.svelte"
git commit -m "feat: add TPS monitoring toggle to performance page

Shows toggle next to TPS chart. Displays notice when disabled.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 6: Console Page — TPS Toggle

**Files:**
- Modify: `apps/web/src/routes/(app)/servers/[name]/console/+page.svelte`

- [ ] **Step 1: Read the file to understand current structure**

Read the full file. Find the toolbar/header area above the terminal.

- [ ] **Step 2: Add TPS toggle in toolbar**

Add similar toggle logic as the performance page. The console page needs access to the server config — check if `data.server.config` is available from the layout data. If not, fetch it.

Add a small toggle in the console toolbar area:

```svelte
<label class="tps-toggle-inline" title={tpsEnabled ? 'TPS monitoring on' : 'TPS monitoring off'}>
    <span class="tps-label">TPS</span>
    <input type="checkbox" checked={tpsEnabled} onchange={toggleTps} />
    <span class="toggle-slider-sm"></span>
</label>
```

- [ ] **Step 3: Add CSS**

```css
.tps-toggle-inline {
    display: inline-flex;
    align-items: center;
    gap: 8px;
    cursor: pointer;
    font-size: 12px;
    color: #8890b1;
}

.tps-label {
    text-transform: uppercase;
    letter-spacing: 0.06em;
    font-weight: 600;
}
```

Reuse toggle slider CSS at smaller size.

- [ ] **Step 4: Commit**

```bash
git add "apps/web/src/routes/(app)/servers/[name]/console/+page.svelte"
git commit -m "feat: add TPS monitoring toggle to console page

Small toggle in console toolbar to stop TPS log spam.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 7: Config Page — Monitoring Section

**Files:**
- Modify: `apps/web/src/routes/(app)/servers/[name]/advanced/+page.svelte`

- [ ] **Step 1: Read the file to understand how config sections are rendered**

Read the existing sections (Java, Minecraft, OnReboot, AutoRestart) to follow the exact pattern.

- [ ] **Step 2: Add Monitoring section after Auto-Restart**

Follow the same card/section pattern used by the other config sections. Add:

```svelte
<div class="config-card">
    <h2>Monitoring</h2>

    <div class="form-group">
        <label for="tps-enabled">TPS Monitoring</label>
        <label class="toggle-label">
            <input
                type="checkbox"
                id="tps-enabled"
                bind:checked={config.monitoring.tpsEnabled}
            />
            <span>{config.monitoring.tpsEnabled ? 'Enabled' : 'Disabled'}</span>
        </label>
        <span class="help-text">Periodically sends a TPS command to measure server performance</span>
    </div>

    {#if config.monitoring.tpsEnabled}
        <div class="form-group">
            <label for="tps-command">TPS Command</label>
            <input
                type="text"
                id="tps-command"
                bind:value={config.monitoring.tpsCommand}
                placeholder="/tps"
            />
            <span class="help-text">
                Paper/Spigot: /tps · Forge: /forge tps · NeoForge: /neoforge tps · Spark: /spark tps
            </span>
        </div>
    {/if}
</div>
```

- [ ] **Step 3: Ensure monitoring config is initialized**

In the script block where `config` is initialized from `data.server.config`, ensure `monitoring` has defaults:

```typescript
if (!config.monitoring) {
    config.monitoring = { tpsEnabled: true, tpsCommand: null };
}
```

- [ ] **Step 4: Commit**

```bash
git add "apps/web/src/routes/(app)/servers/[name]/advanced/+page.svelte"
git commit -m "feat: add monitoring section to config page

Full TPS monitoring controls: enable/disable toggle and
custom command input with help text for each server type.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 8: Build, Test, Docker Smoke Test

- [ ] **Step 1: Run all backend tests**

Run: `dotnet test --verbosity normal`
Expected: All pass

- [ ] **Step 2: Run svelte-check**

Run: `cd apps/web && npm run check`
Expected: No new errors

- [ ] **Step 3: Rebuild Docker images**

```bash
docker compose -f docker-compose.yml -f docker-compose.build.yml build
```

- [ ] **Step 4: Start containers and verify**

```bash
docker compose up -d
sleep 10
docker compose ps
```

- [ ] **Step 5: Test in browser**

Open http://localhost:3000. Verify:
- Create a new Forge server → `server.config` has `tps_enabled=true` and `tps_command=/forge tps`
- Create a Vanilla server → `tps_enabled=false`
- Performance page shows TPS toggle, disabled notice when off
- Console page has TPS toggle in toolbar
- Config page has Monitoring section with command input
- Toggling TPS on/off persists across page reloads

- [ ] **Step 6: Stop containers**

```bash
docker compose down
```
