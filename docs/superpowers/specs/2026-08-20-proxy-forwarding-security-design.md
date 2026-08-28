# Proxy backend forwarding: secure by default, honest about the rest

**Date:** 2026-08-20
**Follows:** #99 (Velocity proxy support), #157 / #162 (BungeeCord proxy support)

## Problem

MineOS can already route several servers behind one port. `velocity.toml`'s
`[forced-hosts]` table is read, written, and editable in the UI, so one proxy on
25565 can serve `freemancraft.com` and `serverloco.win` from two different
backends. That half works.

The other half — making those backends *safe* — is entirely manual, and the
default MineOS generates is the unsafe one.

`ServerService.VelocityConfigDefaults` writes:

```
player-info-forwarding-mode = "none"
```

`none` is the unauthenticated forwarding mode. A server sitting behind a proxy
must also run `online-mode=false`, because the proxy performs Mojang
authentication on its behalf. Those two settings together mean **anyone who can
open a TCP connection to the backend port can join as any username and UUID they
like** — including an operator's. Nothing in MineOS writes the backend half that
would prevent this, and nothing warns that it is missing.

Two things make this worse than a documentation gap:

1. `UpdateServerTypeAsync` pre-populates a new proxy's `[servers]` table with
   *every* sibling Java server via `DiscoverJavaBackendsAsync`. Enrollment is
   opt-out, so a user can acquire backends they never consciously enrolled.
2. As of #162, BungeeCord is available too. Its `ip_forward: true` is the legacy
   forwarding mode, which carries no signature at all — network isolation is the
   only control that exists for it.

## Goals

1. A newly created Velocity proxy defaults to **verified** forwarding
   (`modern`), with a generated `forwarding.secret`.
2. Securing a Paper/Purpur backend is **one action**: MineOS writes
   `config/paper-global.yml` and `server.properties` correctly, or fails without
   leaving the server in a worse state than it found it.
3. Every server reports, accurately and on every read, whether it is currently
   spoofable.
4. Where verified forwarding is impossible (Forge, vanilla, BungeeCord
   `ip_forward`), MineOS **checks whether the backend is actually reachable from
   outside** rather than printing a caveat.
5. The forwarding secret never leaves the host. It is never returned by any API
   response, in any form, redacted or otherwise.

## Non-goals

- **No blocking.** MineOS never refuses to enroll a backend. Self-hosted users
  have legitimate setups the tool cannot model, and a hard block only pushes
  them to hand-edit files, which loses the warning too.
- **No auto-migration.** Existing proxies keep `player-info-forwarding-mode =
  "none"` until a human acts. They will surface as `Misconfigured` with a fix
  button. MineOS does not silently rewrite a running user's proxy config.
- **No background reconciler.** Continuously forcing backend config to match
  proxy config fights hand-edited files, which is hostile in a tool whose users
  edit `server.properties` directly.
- **No change to who-can-do-what.** Every new route sits in the existing
  `servers` group behind `ServerAccessFilter`.
- Bedrock is out of scope entirely — UDP 19132 is not proxied by Velocity or
  BungeeCord and stays port-per-server.

## Status model

Status is **derived on every read, never stored.** No new table, no migration.
A stored "secured" flag goes stale the moment someone edits `server.properties`
by hand, and a stale security badge is worse than no badge.

For a given server, resolution runs:

1. **Is it a backend?** Scan sibling proxy servers' `velocity.toml` `[servers]`
   (and `config.yml` `servers`) for an address matching this server's listen
   endpoint, via the existing `GetServerListenEndpointAsync`.
2. **What can it do?** `DetectLoaderAsync` gives the tier.
3. **What is it?** Read `server.properties` `online-mode`, and for Paper,
   `config/paper-global.yml` → `proxies.velocity.{enabled, online-mode, secret}`.
4. **Do the secrets match?** Compared inside Infrastructure only.

| Status | Meaning |
|---|---|
| `NotABackend` | Direct server, `online-mode=true`. Nothing to report. |
| `Secured` | Modern forwarding, secrets match, `online-mode=false`. Correct. |
| `Misconfigured` | Enrolled, `online-mode=false`, forwarding absent or secret mismatched. **Spoofable right now.** |
| `Securable` | Fixable: one click (Paper/Purpur) or one mod (Fabric). |
| `Unverifiable` | Forge / vanilla / BungeeCord `ip_forward`. Isolation is the only control. |

### Capability tiers

| Loader | Path |
|---|---|
| Paper, Purpur | Native modern forwarding. One-click secure. |
| Fabric | Modern forwarding via FabricProxy-Lite. Offer install, then one-click. (PR 2) |
| Forge, NeoForge, vanilla | No verified forwarding path. `Unverifiable` + exposure check. |

## Write path

`POST /servers/{name}/forwarding/secure`, acting on the **backend** server, so
`ServerAccessFilter` gates it against a server the caller already has access to.
Idempotent. Steps, in order:

1. **Resolve the proxy.** Refuse if zero or more than one proxy claims this
   backend — ambiguity here means guessing about security.
2. **Ensure the secret.** If the proxy has no `forwarding.secret`, generate 32
   bytes from `RandomNumberGenerator`, base64url-encode, write `0600`, chown via
   `OwnershipHelper`. If one exists, **reuse it** — regenerating would break
   every already-secured sibling backend.
3. **Write the backend half.** `config/paper-global.yml` →
   `proxies.velocity.{enabled: true, online-mode: true, secret: <value>}`, edited
   in place with `YamlDotNet.RepresentationModel` so Paper's other keys survive.
   Set the proxy's `player-info-forwarding-mode` to `modern` if still `none`.
4. **Flip `online-mode=false`** in the backend's `server.properties` via
   `UpdateServerPropertiesAsync`. Then `MarkRestartRequiredAsync` on both servers.

**Step 4 is last, deliberately.** If step 3 fails part-way, the result is a
backend that still authenticates its own players — broken behind a proxy, but not
spoofable. The reverse order would leave a live, unauthenticated server. Fail
toward the safe state.

The action writes an `AuditLog` entry. It is a POST rather than part of a config
save because it generates key material and has a real partial-failure mode.

## Exposure check

For `Unverifiable` backends, isolation is the only control, so MineOS verifies it
instead of captioning it: read the API container's own port bindings over the
already-mounted `/var/run/docker.sock`. .NET 8 speaks Unix sockets natively via
`SocketsHttpHandler.ConnectCallback`, so **no new dependency is required**.

| Verdict | Meaning |
|---|---|
| `Exposed` | The backend's port is published to the host, or `NetworkMode: host`. |
| `NotExposed` | Positively determined that the port is not published. |
| `Unknown` | The socket was unreadable or the answer was inconclusive. |

**`Unknown` is never reported as `NotExposed`.** A security check that guesses
"probably fine" is worse than one that admits it does not know.
`docker-compose.host.yml` uses host networking, where every port is exposed
regardless of `MC_PORT_RANGE`; that must report `Exposed`, not a computed
false negative.

## API surface

| Route | Purpose |
|---|---|
| `GET /servers/{name}/forwarding` | Derived status, resolved proxy name, loader tier, `secretMatches: bool`, exposure verdict. |
| `POST /servers/{name}/forwarding/secure` | The write path above. |
| `GET /servers/{name}/forwarding/backends` | Proxy-side roll-up, filtered to servers the caller may access. |

The DTO carries `secretMatches: bool` and **never the secret value**. The
existing `VelocityConfigDto` exposes only `ForwardingSecretFile` (a filename);
that stays.

## Layering

| Layer | Contents |
|---|---|
| `Domain` | `ProxyForwardingStatus`, `ExposureVerdict`, `BackendLink` — pure C#. |
| `Application` | `IProxyForwardingService`, `BackendForwardingDto`. |
| `Infrastructure` | `ProxyForwardingService`: TOML/YAML reads, secret generation, docker.sock. |
| `Api` | Endpoints in the `servers` group; no logic. |

## UI

- **Backend server page:** a status strip. `Misconfigured` is loud and
  non-dismissible: *"Players can currently join as anyone — this server accepts
  unauthenticated connections"*, with the **Secure this backend** button.
- **Proxy `proxy-config` page:** a backend roll-up showing every enrolled server
  and its status, filtered to what the caller can access.
- No `SystemNotification` rows are created; all of this is derived at read time.

## Testing

The core is pure and table-driven: given a proxy config, a loader, a
`server.properties`, and a `paper-global.yml`, assert the status. Same shape as
`BungeeConfigTests`, no service to stand up.

- Status matrix across all five statuses and every loader tier.
- Secret comparison: match, mismatch, missing on either side.
- `paper-global.yml` round-trip preserving unmodeled Paper keys.
- Write-path ordering: a failure in step 3 leaves `online-mode` untouched.
- Exposure: published range, host networking, unreadable socket → `Unknown`.

New tests are measured against a captured clean baseline. That baseline used to
carry 6 pre-existing failures; they turned out to be a single bug in
`MineOsWebApplicationFactory`, which ran the integration tests against the real
`/var/games/minecraft` instead of a temp directory (fixed separately in #163).
`dotnet test` on `vibing` now reports **73 passed / 0 failed / 73 total**, so
"green" means green and this feature is measured against zero.

## Delivery

**PR 1** — status model, derived endpoint, Paper/Purpur secure action, exposure
check, `VelocityConfigDefaults` → `modern`, BungeeCord as `Unverifiable`.

**PR 2** — Fabric: detect FabricProxy-Lite, offer to install it, then treat the
backend as `Securable`. **Delivered.**

Three things only surfaced once it ran against a real Fabric server, and each is
now covered by a regression test:

1. `DetectLoaderAsync` reports the *loader* version for Fabric (`0.19.3`), not
   the game version, so the Minecraft version is parsed from the server jar name
   (`fabric-server-mc.1.21.1-loader.0.19.3.jar`) instead. Without this, every
   install was refused.
2. FabricProxy-Lite hard-requires Fabric API, and Fabric **refuses to boot** when
   a required dependency is missing — so the install resolves required Modrinth
   dependencies too, or aborts without installing anything.
3. A disabled mod jar must not count as installed; MineOS disables mods by
   renaming them, and a disabled FabricProxy-Lite verifies nothing.

## Release notes

Flipping the default to `modern` affects **new proxies only**. Users upgrading
with existing proxies will suddenly see `Misconfigured` warnings on setups they
believed were fine. Those setups are not fine — but the warning must be
explained in the release notes rather than arriving as a surprise.
