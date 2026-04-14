# Mod Loader UX Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Surface the server's mod loader prominently, filter all mod searches by it, and add per-mod enable/disable toggles. Fixes #74.

**Architecture:** Add a loader detection endpoint that reuses existing `ResolveServerDefaultsAsync` logic. Thread the detected loader through all search components as a prop. Add mod enable/disable endpoints mirroring the existing plugin pattern. Store loader logos as static assets.

**Tech Stack:** .NET 8 Minimal API, SvelteKit 5 (Svelte 5 runes), xUnit

**Spec:** `docs/superpowers/specs/2026-03-22-mod-loader-ux-design.md`

**Related issues:** Fixes #74 (enable/disable mods)

---

## File Map

### Backend — New/Modified

| File | Responsibility |
|------|----------------|
| `apps/MineOS.Api/Endpoints/ModEndpoints.cs` | Add `GET /loader`, `POST .../enable`, `POST .../disable` |
| `apps/MineOS.Application/Interfaces/IModService.cs` | Add `SetModEnabledAsync` method |
| `apps/MineOS.Infrastructure/Services/ModService.cs` | Implement enable/disable via file rename |
| `apps/MineOS.Application/Interfaces/ICurseForgeService.cs` | Add `string? loader` param to `SearchModsAsync` |
| `apps/MineOS.Infrastructure/Services/CurseForgeService.cs` | Map loader to `modLoaderType`, include in query |
| `apps/MineOS.Api/Endpoints/CurseForgeEndpoints.cs` | Accept `loader` query param |

### Frontend — New/Modified

| File | Responsibility |
|------|----------------|
| `apps/web/src/routes/(app)/servers/[name]/mods/+page.svelte` | Loader banner, toggle switches, pass loader prop |
| `apps/web/src/lib/components/ModrinthSearch.svelte` | Accept + thread `loader` prop |
| `apps/web/src/lib/components/ModrinthModSearch.svelte` | Use `loader` prop, remove dropdown |
| `apps/web/src/lib/components/ModrinthModpackSearch.svelte` | Use `loader` prop, remove dropdown |
| `apps/web/src/lib/components/ModrinthResourcePackSearch.svelte` | Accept `loader` prop for interface consistency (no dropdown to remove) |
| `apps/web/src/lib/components/CurseForgeSearch.svelte` | Accept `loader` prop, pass to API |
| `apps/web/src/routes/api/curseforge/[...path]/+server.ts` | No changes needed — already forwards all query params |
| `apps/web/static/images/loaders/` | Forge, Fabric, NeoForge, Quilt SVG logos |

### Tests

| File | Responsibility |
|------|----------------|
| `apps/MineOS.Tests/Unit/ModToggleTests.cs` | Unit tests for mod enable/disable rename logic |
| `apps/MineOS.Tests/Integration/ModEndpointTests.cs` | Integration tests for loader + toggle endpoints |

---

## Task 1: Mod Enable/Disable Backend

**Files:**
- Modify: `apps/MineOS.Application/Interfaces/IModService.cs:7`
- Modify: `apps/MineOS.Infrastructure/Services/ModService.cs`
- Modify: `apps/MineOS.Api/Endpoints/ModEndpoints.cs`
- Create: `apps/MineOS.Tests/Unit/ModToggleTests.cs`

- [ ] **Step 1: Write failing test for mod disable**

```csharp
// apps/MineOS.Tests/Unit/ModToggleTests.cs
using MineOS.Infrastructure.Services;

namespace MineOS.Tests.Unit;

public class ModToggleTests : IDisposable
{
    private readonly string _tempDir;

    public ModToggleTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"mineos-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_tempDir, "mods"));
    }

    [Fact]
    public void DisableMod_Renames_Jar_To_Disabled()
    {
        var modsDir = Path.Combine(_tempDir, "mods");
        File.WriteAllText(Path.Combine(modsDir, "testmod.jar"), "fake");

        var newName = ModService.ComputeToggleName("testmod.jar", enabled: false);

        Assert.Equal("testmod.jar.disabled", newName);
    }

    [Fact]
    public void EnableMod_Renames_Disabled_To_Jar()
    {
        var newName = ModService.ComputeToggleName("testmod.jar.disabled", enabled: true);

        Assert.Equal("testmod.jar", newName);
    }

    [Fact]
    public void DisableMod_Already_Disabled_Returns_Same()
    {
        var newName = ModService.ComputeToggleName("testmod.jar.disabled", enabled: false);

        Assert.Equal("testmod.jar.disabled", newName);
    }

    [Fact]
    public void EnableMod_Already_Enabled_Returns_Same()
    {
        var newName = ModService.ComputeToggleName("testmod.jar", enabled: true);

        Assert.Equal("testmod.jar", newName);
    }

    public void Dispose() => Directory.Delete(_tempDir, true);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~ModToggleTests" --verbosity normal`
Expected: FAIL — `ComputeToggleName` does not exist

- [ ] **Step 3: Add `ComputeToggleName` to ModService**

Add a static helper method to `apps/MineOS.Infrastructure/Services/ModService.cs`:

```csharp
public static string ComputeToggleName(string filename, bool enabled)
{
    var isCurrentlyDisabled = filename.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);

    if (enabled && isCurrentlyDisabled)
        return filename[..^".disabled".Length];

    if (!enabled && !isCurrentlyDisabled)
        return filename + ".disabled";

    return filename;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~ModToggleTests" --verbosity normal`
Expected: 4 PASS

- [ ] **Step 5: Add `SetModEnabledAsync` to IModService and ModService**

In `apps/MineOS.Application/Interfaces/IModService.cs`, add:
```csharp
Task<string> SetModEnabledAsync(string serverName, string filename, bool enabled, CancellationToken cancellationToken);
```

In `apps/MineOS.Infrastructure/Services/ModService.cs`, implement:
```csharp
public async Task<string> SetModEnabledAsync(string serverName, string filename, bool enabled, CancellationToken cancellationToken)
{
    var modsPath = GetModsPath(serverName);
    var filePath = Path.Combine(modsPath, filename);

    if (!File.Exists(filePath))
        throw new FileNotFoundException($"Mod file not found: {filename}");

    var newFilename = ComputeToggleName(filename, enabled);
    if (newFilename == filename)
        return filename; // Already in desired state

    var newPath = Path.Combine(modsPath, newFilename);
    File.Move(filePath, newPath);

    // Mark server as needing restart
    var serverPath = Path.Combine(_hostOptions.BaseDirectory, _hostOptions.ServersPathSegment, serverName);
    await File.WriteAllTextAsync(
        Path.Combine(serverPath, ".mineos-restart-required"), "", cancellationToken);

    _logger.LogInformation("Mod {OldName} -> {NewName} for server {Server}",
        filename, newFilename, serverName);

    return newFilename;
}
```

- [ ] **Step 6: Add enable/disable endpoints to ModEndpoints.cs**

Add after the existing mod endpoints in `apps/MineOS.Api/Endpoints/ModEndpoints.cs`, following the plugin pattern from `PluginEndpoints.cs:111-159`:

```csharp
servers.MapPost("/{name}/mods/{filename}/enable", async (
    string name,
    string filename,
    IModService modService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var newFilename = await modService.SetModEnabledAsync(name, filename, true, cancellationToken);
        return Results.Ok(new { filename = newFilename, enabled = true });
    }
    catch (FileNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
    catch (DirectoryNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

servers.MapPost("/{name}/mods/{filename}/disable", async (
    string name,
    string filename,
    IModService modService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var newFilename = await modService.SetModEnabledAsync(name, filename, false, cancellationToken);
        return Results.Ok(new { filename = newFilename, enabled = false });
    }
    catch (FileNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
    catch (DirectoryNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});
```

- [ ] **Step 7: Build and run all tests**

Run: `dotnet build && dotnet test --verbosity normal`
Expected: All pass, 0 errors

- [ ] **Step 8: Commit**

```bash
git add apps/MineOS.Application/Interfaces/IModService.cs \
       apps/MineOS.Infrastructure/Services/ModService.cs \
       apps/MineOS.Api/Endpoints/ModEndpoints.cs \
       apps/MineOS.Tests/Unit/ModToggleTests.cs
git commit -m "feat: add mod enable/disable endpoints

Mirrors the existing plugin enable/disable pattern.
Renames .jar <-> .jar.disabled and sets restart-required flag.

Fixes #74

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: Loader Detection Endpoint

**Files:**
- Modify: `apps/MineOS.Api/Endpoints/ModEndpoints.cs:611-655`

- [ ] **Step 1: Add `GET /servers/{name}/loader` endpoint**

Add to `ModEndpoints.cs`, reusing the existing `ResolveServerDefaultsAsync` and `MapModLoader` methods:

```csharp
servers.MapGet("/{name}/loader", async (
    string name,
    IServerService serverService,
    IProfileService profileService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var (gameVersion, loader) = await ResolveServerDefaultsAsync(
            name, serverService, profileService, cancellationToken);
        return Results.Ok(new { loader, version = gameVersion });
    }
    catch (DirectoryNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
    catch (Exception)
    {
        return Results.Ok(new { loader = (string?)null, version = (string?)null });
    }
});
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build`
Expected: 0 errors

- [ ] **Step 3: Commit**

```bash
git add apps/MineOS.Api/Endpoints/ModEndpoints.cs
git commit -m "feat: add GET /servers/{name}/loader endpoint

Reuses existing ResolveServerDefaultsAsync logic to expose
detected mod loader and Minecraft version.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: CurseForge Loader Filtering

**Files:**
- Modify: `apps/MineOS.Application/Interfaces/ICurseForgeService.cs:7-16`
- Modify: `apps/MineOS.Infrastructure/Services/CurseForgeService.cs:20-90`
- Modify: `apps/MineOS.Api/Endpoints/CurseForgeEndpoints.cs:13-56`

- [ ] **Step 1: Add `loader` parameter to `ICurseForgeService.SearchModsAsync`**

In `apps/MineOS.Application/Interfaces/ICurseForgeService.cs`, add `string? loader` after `gameVersion`:

```csharp
Task<CurseForgeSearchResultDto> SearchModsAsync(
    string query,
    int? classId,
    int index,
    int pageSize,
    string? sort,
    string? order,
    long? minDownloads,
    string? gameVersion,
    string? loader,
    CancellationToken cancellationToken);
```

- [ ] **Step 2: Update `CurseForgeService.SearchModsAsync` implementation**

In `apps/MineOS.Infrastructure/Services/CurseForgeService.cs`, add after the existing conditional parameters (around line 60):

```csharp
if (!string.IsNullOrWhiteSpace(loader))
{
    var modLoaderType = loader.Trim().ToLowerInvariant() switch
    {
        "forge" => 1,
        "fabric" => 4,
        "quilt" => 5,
        "neoforge" => 6,
        _ => (int?)null
    };
    if (modLoaderType.HasValue)
        parameters.Add($"modLoaderType={modLoaderType.Value}");
}
```

Update the method signature to include `string? loader`.

- [ ] **Step 3: Update CurseForge endpoint to accept and forward `loader`**

In `apps/MineOS.Api/Endpoints/CurseForgeEndpoints.cs`, add `[FromQuery] string? loader` parameter and pass it through to `SearchModsAsync`.

- [ ] **Step 4: Build and run tests**

Run: `dotnet build && dotnet test --verbosity normal`
Expected: All pass

- [ ] **Step 5: Commit**

```bash
git add apps/MineOS.Application/Interfaces/ICurseForgeService.cs \
       apps/MineOS.Infrastructure/Services/CurseForgeService.cs \
       apps/MineOS.Api/Endpoints/CurseForgeEndpoints.cs
git commit -m "feat: add mod loader filtering to CurseForge search

Maps loader string to CurseForge modLoaderType enum:
forge=1, fabric=4, quilt=5, neoforge=6.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: Add Loader Logo Assets

**Files:**
- Create: `apps/web/static/images/loaders/forge.svg`
- Create: `apps/web/static/images/loaders/fabric.svg`
- Create: `apps/web/static/images/loaders/neoforge.svg`
- Create: `apps/web/static/images/loaders/quilt.svg`

- [ ] **Step 1: Download official logos**

Source SVGs from each project's official branding/GitHub:
- Forge: https://github.com/MinecraftForge/MinecraftForge (anvil logo)
- Fabric: https://github.com/FabricMC/community (fabric logo)
- NeoForge: https://github.com/neoforged/NeoForge (neoforge logo)
- Quilt: https://github.com/QuiltMC/art (quilt logo)

Save to `apps/web/static/images/loaders/`. If official SVGs aren't directly available, create clean SVG reproductions of the logos.

- [ ] **Step 2: Verify files exist and are valid SVGs**

Run: `ls -la apps/web/static/images/loaders/`
Expected: 4 SVG files

- [ ] **Step 3: Commit**

```bash
git add apps/web/static/images/loaders/
git commit -m "feat: add mod loader logo assets

Official logos for Forge, Fabric, NeoForge, and Quilt.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: Mods Page — Loader Banner & Toggle Switches

**Files:**
- Modify: `apps/web/src/routes/(app)/servers/[name]/mods/+page.svelte`

- [ ] **Step 1: Add loader detection state and fetch**

At the top of the script block (after existing state declarations around line 38), add:

```typescript
let detectedLoader = $state<string | null>(null);
let detectedVersion = $state<string | null>(null);
let loaderLoading = $state(true);
let showLoaderPicker = $state(false);

// Loader logo mapping
const loaderLogos: Record<string, string> = {
    forge: '/images/loaders/forge.svg',
    fabric: '/images/loaders/fabric.svg',
    neoforge: '/images/loaders/neoforge.svg',
    quilt: '/images/loaders/quilt.svg'
};

const loaderNames: Record<string, string> = {
    forge: 'Forge',
    fabric: 'Fabric',
    neoforge: 'NeoForge',
    quilt: 'Quilt'
};

onMount(async () => {
    try {
        const res = await fetch(`/api/servers/${data.server.name}/loader`);
        if (res.ok) {
            const info = await res.json();
            if (info.loader) {
                detectedLoader = info.loader;
                detectedVersion = info.version;
            } else {
                // Check localStorage fallback
                const stored = localStorage.getItem(`mineos-loader-${data.server.name}`);
                if (stored) {
                    detectedLoader = stored;
                } else {
                    showLoaderPicker = true;
                }
            }
        }
    } catch {
        showLoaderPicker = true;
    } finally {
        loaderLoading = false;
    }
});

function selectLoader(loader: string) {
    detectedLoader = loader;
    showLoaderPicker = false;
    localStorage.setItem(`mineos-loader-${data.server.name}`, loader);
}
```

- [ ] **Step 2: Add loader banner template**

Add at the top of the content area, before the existing mod list cards:

```svelte
{#if loaderLoading}
    <div class="loader-banner loading">Detecting mod loader...</div>
{:else if showLoaderPicker}
    <div class="loader-picker">
        <h3>Select your mod loader</h3>
        <p>We couldn't detect a mod loader for this server. Choose one to filter compatible mods.</p>
        <div class="loader-options">
            {#each ['forge', 'fabric', 'neoforge', 'quilt'] as loader}
                <button class="loader-option" onclick={() => selectLoader(loader)}>
                    <img src={loaderLogos[loader]} alt={loaderNames[loader]} class="loader-logo" />
                    <span>{loaderNames[loader]}</span>
                </button>
            {/each}
        </div>
    </div>
{:else if detectedLoader}
    <div class="loader-banner">
        <img src={loaderLogos[detectedLoader]} alt={loaderNames[detectedLoader] ?? detectedLoader} class="loader-logo" />
        <span class="loader-name">{loaderNames[detectedLoader] ?? detectedLoader}</span>
        {#if detectedVersion}
            <span class="loader-version">{detectedVersion}</span>
        {/if}
    </div>
{/if}
```

- [ ] **Step 3: Add toggle switch to each mod in the file list**

Find the mod file list rendering (around lines 637-665 where `.disabled` class is used). For each mod item, add a toggle switch before the filename:

```svelte
<label class="mod-toggle" title={mod.isDisabled ? 'Enable mod' : 'Disable mod'}>
    <input
        type="checkbox"
        checked={!mod.isDisabled}
        onchange={() => toggleMod(mod)}
    />
    <span class="toggle-slider"></span>
</label>
```

Add the `toggleMod` function:

```typescript
async function toggleMod(mod: InstalledModWithModpack) {
    const action = mod.isDisabled ? 'enable' : 'disable';
    const oldState = mod.isDisabled;

    // Optimistic update
    mod.isDisabled = !mod.isDisabled;

    try {
        const res = await fetch(
            `/api/servers/${data.server.name}/mods/${encodeURIComponent(mod.fileName)}/${action}`,
            { method: 'POST' }
        );
        if (!res.ok) {
            mod.isDisabled = oldState; // Revert
            const err = await res.json().catch(() => ({ error: 'Toggle failed' }));
            console.error(err.error);
        } else {
            const result = await res.json();
            // Update filename in local state (it changed from .jar to .jar.disabled or vice versa)
            mod.fileName = result.filename;
        }
    } catch {
        mod.isDisabled = oldState; // Revert on network error
    }
}
```

- [ ] **Step 4: Pass `detectedLoader` to search components**

Where `ModrinthSearch` and `CurseForgeSearch` are rendered, pass the loader:

```svelte
<ModrinthSearch {serverName} {serverVersion} loader={detectedLoader} {onInstallComplete} />
<CurseForgeSearch {serverName} {serverVersion} loader={detectedLoader} {onInstallComplete} />
```

- [ ] **Step 5: Add CSS for loader banner, picker, and toggle switch**

```css
.loader-banner {
    display: flex;
    align-items: center;
    gap: 12px;
    padding: 12px 20px;
    background: rgba(22, 27, 46, 0.9);
    border: 1px solid rgba(42, 47, 71, 0.8);
    border-radius: 12px;
    margin-bottom: 16px;
}

.loader-banner.loading {
    color: #8890b1;
    font-style: italic;
}

.loader-logo {
    width: 28px;
    height: 28px;
    object-fit: contain;
}

.loader-name {
    font-weight: 600;
    font-size: 16px;
    color: #eef0f8;
}

.loader-version {
    color: #8890b1;
    font-size: 14px;
}

.loader-picker {
    padding: 24px;
    background: rgba(22, 27, 46, 0.9);
    border: 1px solid rgba(42, 47, 71, 0.8);
    border-radius: 12px;
    margin-bottom: 16px;
    text-align: center;
}

.loader-picker h3 {
    margin: 0 0 8px;
    font-size: 18px;
}

.loader-picker p {
    margin: 0 0 20px;
    color: #8890b1;
    font-size: 14px;
}

.loader-options {
    display: flex;
    gap: 16px;
    justify-content: center;
    flex-wrap: wrap;
}

.loader-option {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 8px;
    padding: 16px 24px;
    background: rgba(30, 36, 58, 0.8);
    border: 1px solid rgba(62, 69, 100, 0.6);
    border-radius: 12px;
    color: #cdd3ee;
    cursor: pointer;
    transition: all 0.2s;
    font-size: 14px;
    font-weight: 500;
}

.loader-option:hover {
    border-color: var(--mc-grass);
    background: rgba(106, 176, 76, 0.1);
}

.mod-toggle {
    position: relative;
    display: inline-block;
    width: 36px;
    height: 20px;
    flex-shrink: 0;
}

.mod-toggle input {
    opacity: 0;
    width: 0;
    height: 0;
}

.toggle-slider {
    position: absolute;
    inset: 0;
    background: #2a2f47;
    border-radius: 20px;
    cursor: pointer;
    transition: background 0.2s;
}

.toggle-slider::before {
    content: '';
    position: absolute;
    height: 14px;
    width: 14px;
    left: 3px;
    bottom: 3px;
    background: #8890b1;
    border-radius: 50%;
    transition: transform 0.2s, background 0.2s;
}

.mod-toggle input:checked + .toggle-slider {
    background: rgba(106, 176, 76, 0.3);
}

.mod-toggle input:checked + .toggle-slider::before {
    transform: translateX(16px);
    background: var(--mc-grass);
}
```

- [ ] **Step 6: Run svelte-check**

Run: `cd apps/web && npm run check`
Expected: No new errors from our changes

- [ ] **Step 7: Commit**

```bash
git add apps/web/src/routes/\(app\)/servers/\[name\]/mods/+page.svelte
git commit -m "feat: add loader banner, picker, and mod toggle switches

Shows detected mod loader with logo at top of mods page.
Falls back to a one-time picker if detection fails.
Toggle switches for enabling/disabling individual mods
with optimistic UI updates and restart-required flag.

Fixes #74

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 6: Thread Loader Through Search Components

**Files:**
- Modify: `apps/web/src/lib/components/ModrinthSearch.svelte`
- Modify: `apps/web/src/lib/components/ModrinthModSearch.svelte`
- Modify: `apps/web/src/lib/components/ModrinthModpackSearch.svelte`
- Modify: `apps/web/src/lib/components/ModrinthResourcePackSearch.svelte`
- Modify: `apps/web/src/lib/components/CurseForgeSearch.svelte`
- Modify: `apps/web/src/routes/api/curseforge/[...path]/+server.ts`

- [ ] **Step 1: Update `ModrinthSearch.svelte` to accept and thread `loader` prop**

Add `loader` to props (around line 8):
```typescript
let { serverName, serverVersion, loader, onInstallComplete }: {
    serverName: string;
    serverVersion?: string | null;
    loader?: string | null;
    onInstallComplete?: () => void;
} = $props();
```

Pass to child components:
```svelte
<ModrinthModSearch {serverName} {serverVersion} {loader} {onInstallComplete} />
<ModrinthModpackSearch {serverName} {serverVersion} {loader} {onInstallComplete} />
<ModrinthResourcePackSearch {serverName} {serverVersion} {loader} />
```

- [ ] **Step 2: Update `ModrinthModSearch.svelte`**

Replace `selectedLoader` state (line 33) and `loaderOptions` (lines 44-49) with a prop:
```typescript
let { serverName, serverVersion, loader, onInstallComplete }: {
    serverName: string;
    serverVersion?: string | null;
    loader?: string | null;
    onInstallComplete?: () => void;
} = $props();
```

In the search function (around line 105), replace the `selectedLoader` logic:
```typescript
if (loader) {
    params.set('loader', loader);
}
```

Remove the loader `<select>` dropdown from the template (around line 236).

- [ ] **Step 3: Update `ModrinthModpackSearch.svelte`**

Same pattern as ModrinthModSearch: accept `loader` prop, use it in search params, remove any manual loader dropdown.

Note: `ModrinthResourcePackSearch.svelte` does NOT have a loader dropdown — resource packs are loader-agnostic. Add a `loader` prop for interface consistency but it won't affect search behavior. No dropdown to remove.

- [ ] **Step 4: Update `CurseForgeSearch.svelte`**

Add `loader` prop:
```typescript
let { serverName, serverVersion, loader, onInstallComplete }: {
    serverName: string;
    serverVersion?: string | null;
    loader?: string | null;
    onInstallComplete?: () => void;
} = $props();
```

In the search function, add `loader` to the query params:
```typescript
if (loader) {
    params.set('loader', loader);
}
```

- [ ] **Step 5: CurseForge proxy — no changes needed**

The proxy at `apps/web/src/routes/api/curseforge/[...path]/+server.ts` already forwards all query params via `event.url.search`. The `loader` param will pass through automatically.

- [ ] **Step 6: Run svelte-check and test in browser**

Run: `cd apps/web && npm run check`
Expected: No new errors

- [ ] **Step 7: Commit**

```bash
git add apps/web/src/lib/components/ModrinthSearch.svelte \
       apps/web/src/lib/components/ModrinthModSearch.svelte \
       apps/web/src/lib/components/ModrinthModpackSearch.svelte \
       apps/web/src/lib/components/ModrinthResourcePackSearch.svelte \
       apps/web/src/lib/components/CurseForgeSearch.svelte \
       apps/web/src/routes/api/curseforge/
git commit -m "feat: thread loader through all search components

Modrinth and CurseForge searches now filter by the
detected mod loader. Manual loader dropdowns removed
from individual search components.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 7: Integration Tests

**Files:**
- Create: `apps/MineOS.Tests/Integration/ModEndpointTests.cs`

- [ ] **Step 1: Write integration tests**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MineOS.Tests.Integration;

public class ModEndpointTests : IClassFixture<MineOsWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ModEndpointTests(MineOsWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Api-Key", "dev-static-api-key-change-me");
    }

    private async Task<string> GetTokenAsync()
    {
        var resp = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { username = "admin", password = "admin123!" });
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("accessToken").GetString()!;
    }

    private HttpRequestMessage AuthRequest(HttpMethod method, string url, string token, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Api-Key", "dev-static-api-key-change-me");
        return request;
    }

    [Fact]
    public async Task Loader_Endpoint_Returns_Ok()
    {
        var token = await GetTokenAsync();
        var name = $"loader-test-{Guid.NewGuid():N}"[..30];

        using var createReq = AuthRequest(HttpMethod.Post, "/api/v1/servers", token,
            JsonContent.Create(new { name }));
        await _client.SendAsync(createReq);

        using var loaderReq = AuthRequest(HttpMethod.Get, $"/api/v1/servers/{name}/loader", token);
        var response = await _client.SendAsync(loaderReq);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        // No profile set, so loader should be null
        Assert.True(json.GetProperty("loader").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task Disable_Nonexistent_Mod_Returns_404()
    {
        var token = await GetTokenAsync();
        var name = $"mod-404-{Guid.NewGuid():N}"[..30];

        using var createReq = AuthRequest(HttpMethod.Post, "/api/v1/servers", token,
            JsonContent.Create(new { name }));
        await _client.SendAsync(createReq);

        using var disableReq = AuthRequest(HttpMethod.Post,
            $"/api/v1/servers/{name}/mods/{Uri.EscapeDataString("nonexistent.jar")}/disable", token);
        var response = await _client.SendAsync(disableReq);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Disable_Mod_On_Nonexistent_Server_Returns_404()
    {
        var token = await GetTokenAsync();

        using var req = AuthRequest(HttpMethod.Post,
            "/api/v1/servers/does-not-exist/mods/test.jar/disable", token);
        var response = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run all tests**

Run: `dotnet test --verbosity normal`
Expected: All pass

- [ ] **Step 3: Commit**

```bash
git add apps/MineOS.Tests/Integration/ModEndpointTests.cs
git commit -m "test: add integration tests for mod loader and toggle endpoints

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Task 8: Docker Build & Smoke Test

- [ ] **Step 1: Rebuild both Docker images**

```bash
docker compose -f docker-compose.yml -f docker-compose.build.yml build
```

- [ ] **Step 2: Start containers and verify**

```bash
docker compose up -d
sleep 10
docker compose ps
```

- [ ] **Step 3: Test loader endpoint**

```bash
API_KEY="JDQzGNlEiqgV84a7PuTvpd0A6LSX35FR"
TOKEN=$(curl -s http://127.0.0.1:5078/api/v1/auth/login -X POST \
  -H "Content-Type: application/json" -H "X-Api-Key: $API_KEY" \
  -d '{"username":"admin","password":"admin123!"}' | python3 -c "import sys,json; print(json.load(sys.stdin)['accessToken'])")

# Create a test server and check loader endpoint
curl -s http://127.0.0.1:5078/api/v1/servers -X POST \
  -H "Authorization: Bearer $TOKEN" -H "X-Api-Key: $API_KEY" \
  -H "Content-Type: application/json" \
  -d '{"name":"mod-test"}'

curl -s http://127.0.0.1:5078/api/v1/servers/mod-test/loader \
  -H "Authorization: Bearer $TOKEN" -H "X-Api-Key: $API_KEY"
```

Expected: `{"loader":null,"version":null}` (no profile set yet)

- [ ] **Step 4: Test in browser**

Open http://localhost:3000, navigate to a server's Mods tab. Verify:
- Loader picker appears if no loader detected
- After selecting a loader, banner shows with logo
- Search results are filtered by selected loader
- Toggle switches appear next to each mod

- [ ] **Step 5: Commit any fixes, then stop containers**

```bash
docker compose down
```
