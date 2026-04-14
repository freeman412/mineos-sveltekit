# Server Creation Revamp: NeoForge/Quilt Support & Wizard Redesign

## Problem

The current server creation wizard presents 9 options in a flat list (Vanilla, Paper, Spigot, CraftBukkit, Forge, Fabric, CurseForge, Bedrock, Template), mixing different concepts at the same level:

- Base game editions (Vanilla, Bedrock)
- Server implementations with plugin support (Paper, Spigot, CraftBukkit)
- Modloaders (Forge, Fabric)
- Mod providers (CurseForge)

CurseForge is not a server type — it's a modpack provider that delivers packs running on Forge, Fabric, or NeoForge. NeoForge and Quilt are missing entirely.

## Goals

- Restructure the wizard to teach users the correct mental model (base game vs plugins vs mods)
- Add NeoForge and Quilt as modloader options
- Remove CurseForge as a top-level server creation path (modpack installation moves to server settings post-creation)
- Break the 22,000+ line monolithic wizard into small, reusable components
- Design for a mixed audience: approachable for beginners, not patronizing for power users

## Design Decisions

- **Mutually exclusive paths** for now (plugins OR mods, not both). Hybrid support (SpongeForge, Mohist) can be added later.
- **CurseForge/Modrinth modpack browsing** is a sub-step within server settings, not part of creation. Users can always add modpacks after creation.
- **Bedrock** stays in the same wizard — it's just another server type.
- **Quilt** included from the start alongside Forge, NeoForge, and Fabric.

---

## Wizard Flow

### Step 1 — Choose Server Category

Five cards with descriptions:

| Card | Description |
|------|-------------|
| **Vanilla** | The official Minecraft server from Mojang. No mods, no plugins — pure gameplay. |
| **Plugins** | Enhanced servers with plugin support for custom game modes, anti-cheat, permissions, and more. |
| **Mods** | Full mod support — new blocks, dimensions, mechanics, and total conversions. |
| **Bedrock** | Bedrock Edition dedicated server for cross-platform play (mobile, console, Windows). |
| **Template** | Clone an existing server as a starting point. |

Vanilla, Bedrock, and Template skip straight to version/name selection (no sub-step needed).

### Step 2 — Choose Implementation (Plugins/Mods only)

**If Plugins:**

| Card | Description |
|------|-------------|
| **Paper** | Recommended. High-performance Spigot fork with async chunks and extensive optimizations. |
| **Spigot** | The original plugin server. Wide compatibility with Bukkit plugins. |
| **CraftBukkit** | The classic. Fewest modifications to vanilla, built via BuildTools. |

**If Mods:**

| Card | Description |
|------|-------------|
| **Forge** | The most established modloader. Largest mod library, widest version support. |
| **NeoForge** | Community-driven Forge successor. Modern APIs, active development. 1.20.1+ only. |
| **Fabric** | Lightweight and fast. Growing mod ecosystem, popular for newer versions. |
| **Quilt** | Fabric-compatible fork with additional mod management features. |

### Step 3 — Version Selection

Same as today — pick Minecraft version, then loader version where applicable (Forge version, Fabric loader version, etc.).

### Step 4 — Name & Create

Same as today.

---

## Backend Changes

### New Services

**NeoForgeService** — Modeled after the existing `ForgeService`. NeoForge publishes versions at `https://maven.neoforged.net`. The installer pattern is similar to Forge (download installer JAR, run it, stream progress). NeoForge only supports Minecraft 1.20.1+.

**QuiltService** — Modeled after `FabricService` (Quilt is a Fabric fork). Quilt has a meta API at `https://meta.quiltmc.org/v3/versions`. Same pattern: fetch game versions + loader versions, download server launcher JAR. Very similar install flow to Fabric.

### New API Endpoints

Following the existing pattern:

- `GET /api/neoforge/versions` / `GET /api/neoforge/versions/{minecraftVersion}`
- `POST /api/neoforge/install`
- `GET /api/neoforge/install/{installId}/stream`
- `GET /api/quilt/game-versions`
- `GET /api/quilt/loader-versions`
- `POST /api/quilt/install`
- `GET /api/quilt/install/{installId}/stream`

### Changes to Existing Code

- **ServerService** — `DetectServerType` recognizes `neoforge` and `quilt` as valid server types in `.mineos-server-type`.
- **CurseForgeService** — Already maps NeoForge (loader ID 6) and Quilt (loader ID 5). No changes needed.
- **TPS Monitoring** — Already has `/neoforge tps` mapping. Quilt gets same treatment as Fabric (disabled by default).
- **ProfileService** — No changes. Each modloader has its own dedicated service.

### CurseForge Removal from Wizard

CurseForge is no longer a server creation path. The existing `CurseForgeService` and endpoints stay intact — they are used from server settings for modpack installation post-creation.

---

## Frontend Component Architecture

### Wizard Structure

```
servers/new/
  +page.svelte                  — Wizard shell (step management, navigation)
  steps/
    CategorySelect.svelte       — Step 1: Vanilla/Plugins/Mods/Bedrock/Template
    ImplementationSelect.svelte — Step 2: Paper/Spigot/etc or Forge/NeoForge/etc
    VersionSelect.svelte        — Step 3: Version picker (delegates to type-specific pickers)
    ServerName.svelte           — Step 4: Name input + create button
    Creating.svelte             — Progress/installation screen
  version-pickers/
    VanillaVersions.svelte      — Vanilla/Paper/Spigot/CraftBukkit version lists
    ForgeVersions.svelte        — Forge version picker (MC version → Forge version)
    NeoForgeVersions.svelte     — Same pattern as Forge
    FabricVersions.svelte       — Fabric game version → loader version
    QuiltVersions.svelte        — Same pattern as Fabric
    BedrockVersions.svelte      — Bedrock version list
```

### Wizard State

```typescript
type ServerCategory = 'vanilla' | 'plugins' | 'mods' | 'bedrock' | 'template';
type PluginImpl = 'paper' | 'spigot' | 'craftbukkit';
type ModLoader = 'forge' | 'neoforge' | 'fabric' | 'quilt';

type WizardState = {
  step: 'category' | 'implementation' | 'version' | 'name' | 'creating';
  category?: ServerCategory;
  implementation?: PluginImpl | ModLoader;
  version?: { minecraft: string; loader?: string };
  name?: string;
};
```

Categories that don't need a sub-selection (Vanilla, Bedrock, Template) set `implementation` automatically and skip to step 3.

### Shared/Reusable Components

**`SelectionCard.svelte`** — Clickable card with icon, title, description, and optional badge (e.g., "Recommended" on Paper). Used in Steps 1 and 2.

**`VersionList.svelte`** — Filterable/searchable version list with stable/snapshot toggle, loading state, and download status indicator. Used by all version pickers.

**`InstallProgress.svelte`** — SSE-based installation progress display with progress bar and expandable log output. Replaces duplicated install progress UIs across Forge/Fabric. Reused by Forge, NeoForge, Fabric, and Quilt.

**`TwoColumnVersionPicker.svelte`** — For modloaders with a two-step version pick (MC version on left, loader version on right). Used by Forge, NeoForge, Fabric, and Quilt. Vanilla/Paper/Spigot/Bedrock use `VersionList` directly.

### Backend Reuse

Forge, NeoForge, Fabric, and Quilt all follow the same lifecycle: fetch versions → start install → track progress via SSE. A shared interface or base with common install state tracking (`ConcurrentDictionary`, progress streaming) reduces duplication. Each service provides its own version-fetching and installer-running logic.

---

## Out of Scope (Future Work)

- **ChangeServerType modal** — The existing `ChangeServerType.svelte` should eventually be updated to match the new category/implementation model, but is not part of this spec.
- **Modpack browsing in server settings** — CurseForge/Modrinth modpack installation post-creation. The services and endpoints already exist; the UI integration into server settings is separate work.
- **Hybrid servers** — SpongeForge, Mohist, Arclight (servers supporting both mods and plugins). Deferred until there's demand.
