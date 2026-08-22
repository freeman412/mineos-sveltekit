# Changelog

All notable changes to MineOS are documented here. This project follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Pre-releases publish `:preview` Docker images and are not intended for production.

## [Unreleased]

Nothing yet — the 1.2.0 line below is feature-complete and in testing.

## [1.2.0] — in beta (currently `v1.2.0-beta.6`)

The proxy release. MineOS goes from "one server at a time" to running a network:
a proxy players connect to, with game servers behind it whose identities the proxy
can actually vouch for.

### Added

- **Proxies section.** Proxies have their own `/proxies` page instead of sitting in
  the game-server grid: one card per proxy with live status, address, players and
  memory, a backend-security table with per-row actions, Start/Stop/Restart, and
  properties at `/proxies/<name>/proxy-config`.
- **Attach a server to a proxy from the create wizard.** `/servers/new?type=proxy` is
  a dedicated proxy flow, and creating a game server offers "Behind a proxy?" to
  register it with a proxy once its files finish installing.
- **Velocity proxy support** (#63, #99) and **BungeeCord proxy support** alongside it,
  with a `config.yml` editor (#64, #157).
- **Verified proxy forwarding.** New Velocity proxies default to modern forwarding,
  every server derives a live Secured / Misconfigured / Securable / Unverifiable
  status, and Paper/Purpur backends can be secured in one click (#164).
- **Exposure reporting.** Backends that cannot verify forwarded players (Forge,
  vanilla, anything behind BungeeCord) report whether their port is actually
  reachable from outside, read from Docker rather than assumed (#164).
- **One-click FabricProxy-Lite install**, with its required Modrinth dependencies, so
  Fabric backends can verify forwarded players too (#166).
- **Mod dependency resolution.** Single-mod installs resolve and install their
  dependencies, preview what they will pull in, and skip what is already present
  (#21, #170).
- **Mod loader version updates from the Mods page** — upgrade or downgrade NeoForge,
  Forge, Fabric and Quilt on an existing server (#107).
- **CLI monitoring** (#119): live server refresh, a real `/health` badge, a richer
  server table (players / memory / needs-restart), and a live per-server metrics
  panel (TPS/CPU/RAM/players) streamed over SSE.

### Changed

- Servers and the dashboard show **game servers only**, so their stats match their
  lists. Proxies are discoverable from the sidebar and their own section.
- Attach and detach keep Velocity's `try` list and BungeeCord's `priorities` in sync
  with the server map, so a proxy never boots warning about a backend it cannot route
  to.
- Links to the old `/servers/<name>/proxy-config` permanently redirect to
  `/proxies/<name>/proxy-config`.
- PaperMC migrated to the Fill v3 API ahead of the `api.papermc.io/v2` sunset (#99).
- CI now runs the web unit tests and type-check before the build, so broken tests and
  type errors gate a pull request.

### Fixed

- An attach whose forwarding check failed reported success, leaving a backend
  registered but unsecured and saying nothing about it. It now reports the failure
  and names the risk.
- SSE streams reconnect with exponential backoff instead of dying silently on the
  first dropped connection; the server heartbeat uses the same path.
- Mod search returned zero results on every Fabric/Quilt server (the loader version
  was used as the Minecraft version), and Paper servers without a profile searched
  unfiltered (#167, #168).
- Forwarding status was sent as enum numbers while the web client matched on names,
  so the security panel rendered blank (#165).
- Start verification treated any `startup.log` output as success, reporting JVM-level
  crashes as healthy starts for every server type (#162).
- Integration tests ran against the real `/var/games/minecraft` instead of a temp
  directory, which is why the suite carried 6 "known" failures (#163).
- NeoForge servers were mislabeled as Forge in loader detection, in `isLatest`
  selection (string vs numeric sort), and in the overview's JAR File field.
- Loader install progress appeared frozen due to a buffered SSE proxy.
- Console `/tellraw` and quoted commands.
- CLI: narrow-terminal TUI crash, world-readable `.env` (now `0600`), self-update
  SHA-256 verification and HTTP timeouts, a goroutine race (#119).

### Security

- **Backends behind a proxy were open to impersonation.** They required
  `online-mode=false` while MineOS generated `player-info-forwarding-mode = "none"`,
  leaving any reachable backend open to players joining as any username, including
  operators. New proxies are now configured to verify forwarded identities, and
  existing ones are **reported** as misconfigured rather than silently changed (#164).
- Release hardening: cross-server ACL, admin gates, path-traversal guards, and safe
  backup-restore / world-replace.
- Dynamic same-origin CSRF / 403 fix (#100).
- Microsoft.OpenApi 2.3.0 → 2.12.2 for GHSA-v5pm-xwqc-g5wc (#169).

### Upgrade notes

- No database migrations.
- **Existing proxies keep their current forwarding mode** and will now surface as
  misconfigured with a fix available. This is a report of a pre-existing condition,
  not a change to your configuration — you choose when to secure them.
- Mod search results for Paper servers are now filtered to the server's Minecraft
  version where they previously were not.

### Pre-releases on this line

| Tag | Focus |
|---|---|
| `v1.2.0-beta.6` | Proxies get their own section; wizard attach; live status; CI gate |
| `v1.2.0-beta.5` | Verified proxy forwarding, BungeeCord support, mod dependency resolution |
| `v1.2.0-beta.4` | CLI overhaul — hardening + monitoring (#119) |
| `v1.2.0-beta.3` | Velocity proxy support, Fill v3 migration, release hardening |
| `v1.2.0-beta.2` | NeoForge JAR File mislabel fix |
| `v1.2.0-beta.1` | Mod loader version updates (#107), CSRF fix (#100) |

## [1.1.0] — 2026-04-14

15 issues resolved, 168 files changed.

### Added

- Native Bedrock server support (#53)
- Java 25 auto-detection for MC 26.1+ (#90)
- Redesigned server creation wizard with NeoForge + Quilt
- Mod loader detection, filtering, and per-mod toggles (#74)
- Per-server TPS monitoring control (#76)
- Change Server Type on existing servers
- Console colorization (#68)
- Fabric mod loader support (#62)
- Client mod management with `.mrpack` generation (#66, #10)
- Cron job scheduling (#22)
- Server icon cropping (#35)
- Performance chart time axis (#9)
- CLI `mineos update`; ARM64 Docker images
- 58 automated tests

### Fixed

- Whitelist / op / ban console commands (#80)
- macOS install bash 3.2 compatibility (#81)
- Backup sizes showing 0B (#51)
- Source build auto-clone (#77)

## Earlier releases

1.0.x and 0.2.x predate this file. Their notes live on the
[releases page](https://github.com/freeman412/mineos-sveltekit/releases).
