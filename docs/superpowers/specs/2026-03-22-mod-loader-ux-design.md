# Mod Loader UX: Detection, Filtering, and Toggle

## Problem

Mod loaders (Forge, Fabric, NeoForge, Quilt) are mutually exclusive — installing a Forge mod on a Fabric server causes crashes. Currently:

- Modrinth search auto-detects the loader from the server profile but lets users override it manually
- CurseForge search has zero loader filtering — users can install incompatible mods
- No way to enable/disable individual mods without manually renaming files
- No visual indication of which loader a server runs

## Design

### 1. Loader Detection & Banner

A banner at the top of the mods page displays the detected mod loader with its official logo and Minecraft version (e.g., Forge anvil + "Forge 1.20.1").

**Detection priority:**

1. Server profile's `Group` field via existing `ResolveServerDefaultsAsync` logic in `ModEndpoints.cs` (also maps "ftb" → "forge")
2. JAR filename regex parsing (existing patterns in `ClientPackageService`)
3. Fall back to a one-time picker

**Undetected state:** Four branded buttons (Forge / Fabric / NeoForge / Quilt) shown as a one-time picker. Selection is saved to `localStorage` keyed by server name — persists across tabs and sessions. Cleared when the server's profile changes.

**Non-modded servers:** The mods tab is already grayed out for vanilla, Paper, Spigot, and Bedrock servers via the tab visibility feature. The loader banner and picker only appear for servers that can actually reach the mods page (those with a modded profile or jar file set). If a user somehow reaches the mods page on a non-modded server, the picker is shown — selecting a loader is a clear signal they intend to use mods.

The detected loader is passed to all search components as a prop. Search components no longer have their own loader dropdowns.

**API approach:** Add a lightweight endpoint `GET /api/v1/servers/{name}/loader` that returns `{ loader: "forge", version: "1.20.1" }` (or nulls). This calls the existing `ResolveServerDefaultsAsync` logic. The mods page fetches this once on load — no new `+page.server.ts` needed, just a client-side fetch in `onMount`.

### 2. Search Filtering

**Modrinth:** Already filters by loader. Remove the manual loader dropdown — lock it to the detected loader passed from the parent. Replace `selectedLoader` state with a `loader` prop. Apply to all three Modrinth sub-components: `ModrinthModSearch`, `ModrinthModpackSearch`, and `ModrinthResourcePackSearch` (all have loader dropdowns). Thread the `loader` prop through the parent `ModrinthSearch.svelte` wrapper.

**CurseForge:** Add `modLoaderType` parameter through the full call chain:

- Frontend passes `loader` string to the SvelteKit proxy endpoint
- Proxy forwards to the .NET API
- `ICurseForgeService.SearchModsAsync` accepts a new `string? loader` parameter
- `CurseForgeService` maps loader string to CurseForge enum (1=Forge, 4=Fabric, 5=Quilt, 6=NeoForge) and includes `modLoaderType` in the API query

No additional UI indication in search tabs — the page-level banner already communicates the active loader.

Minecraft version auto-detection from server heartbeat remains unchanged. All other search controls (sort, category, downloads) remain unchanged.

### 3. Mod Toggle (Enable/Disable)

**Backend:** Follow the existing plugin toggle pattern with two explicit endpoints:

- `POST /api/v1/servers/{name}/mods/{filename}/enable` — renames `.jar.disabled` → `.jar`
- `POST /api/v1/servers/{name}/mods/{filename}/disable` — renames `.jar` → `.jar.disabled`

This matches the existing `PluginEndpoints.cs` pattern (`POST .../enable` and `POST .../disable`) for consistency. Returns the updated filename. Sets the server's restart-required flag.

Frontend must `encodeURIComponent()` the filename in the URL (filenames may contain brackets, spaces).

**Frontend:** Each mod in the file list gets a toggle switch:

- On = `.jar` file (green toggle)
- Off = `.jar.disabled` (gray toggle, dimmed filename using existing `.disabled` CSS)
- Toggling while server is running is allowed — file rename is safe, changes take effect on next restart
- After toggle, the existing `needsRestart` banner appears
- **Optimistic update:** flip `isDisabled` in local state immediately, revert on error. Avoids reloading the full mod list (which can be hundreds of mods with modpacks).

**Plugins page:** Already has toggle functionality (`POST .../enable` and `POST .../disable` endpoints + UI buttons). No changes needed.

### 4. Loader Logos

Official SVG logos stored in `apps/web/static/images/loaders/`:

- `forge.svg` — Forge anvil
- `fabric.svg` — Fabric logo
- `neoforge.svg` — NeoForge logo
- `quilt.svg` — Quilt patchwork logo

Sourced from each project's official public branding. Used in:

- Mods page loader banner
- One-time loader picker buttons
- Server creation page type selector (Forge/Fabric options)

## Files to Modify

### Backend (.NET)

| File | Change |
|------|--------|
| `ModEndpoints.cs` | Add `POST /{name}/mods/{filename}/enable` and `POST .../disable` endpoints; add `GET /servers/{name}/loader` endpoint |
| `IModService.cs` | Add `EnableModAsync` and `DisableModAsync` methods |
| `ModService.cs` | Implement enable/disable via file rename + restart flag |
| `ICurseForgeService.cs` | Add `string? loader` parameter to `SearchModsAsync` |
| `CurseForgeService.cs` | Map loader to `modLoaderType` enum, include in API query |
| `CurseForgeEndpoints.cs` | Accept and forward `loader` query parameter |

### Frontend (SvelteKit)

| File | Change |
|------|--------|
| `servers/[name]/mods/+page.svelte` | Add loader banner with logo, toggle switches with optimistic updates, pass loader to search components |
| `ModrinthSearch.svelte` | Accept `loader` prop, thread to child components |
| `ModrinthModSearch.svelte` | Replace `selectedLoader` state with `loader` prop, remove dropdown |
| `ModrinthModpackSearch.svelte` | Replace loader dropdown with `loader` prop |
| `ModrinthResourcePackSearch.svelte` | Replace loader dropdown with `loader` prop |
| `CurseForgeSearch.svelte` | Accept `loader` prop, pass to search API call |
| `api/curseforge/[...path]/+server.ts` | Forward `loader` query param |
| `static/images/loaders/` | Add four SVG logo files |
