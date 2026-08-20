---
name: mineos-local-lab
description: Use when testing mineos-sveltekit changes for real — standing up a throwaway MineOS in Docker, running the .NET suite without a local SDK, creating proxy/backend/Fabric servers, or verifying Minecraft behaviour (routing, forwarding security, mod installs) end to end.
---

# The local lab

## Overview

The lab is a throwaway MineOS stack in Docker with real Minecraft servers behind it. It exists because **this repo's most important behaviour is invisible to unit tests**: whether a hostname routes to the right backend, whether a secured backend actually refuses a spoofed login, whether a mod install produces a server that still boots.

Every one of those has shipped broken at least once with a green test suite. The lab is how you catch that before a user does.

## The Iron Rules

1. **Never `docker compose down` without `-p mclab`.** The repo's own dev containers are named `mineos-api` / `mineos-web`. The lab uses `mclab-api` / `mclab-web` precisely so the two cannot collide — keep it that way, and never remove a container you did not create.
2. **A green suite is not a working feature.** Enum serialization, loader-version confusion, and missing mod dependencies all passed every test and failed instantly against a real server. If a change touches the wire format, the Minecraft protocol, or an external API, run it here.
3. **Rebuilding `api` kills every running Minecraft server.** They are `screen` sessions inside that container. After `--build`, restart the servers you need.
4. **Wait for the rebuild before testing it.** `docker compose up -d --build` leaves the *old* container serving requests while the new image builds. Poll until the container's `StartedAt` moves, or you will test the code you just replaced. This has produced at least one "the fix didn't work" false alarm.
5. **Lab settings are not production settings.** The proxy runs `online-mode=false` so a probe can log in without a Mojang account. On a real deployment that must be `true` — the forwarding secret protects backends, not the proxy's online-mode.

## Running the .NET tests without a local SDK

CI does not run the xUnit suite and you may not have `dotnet` installed. Run it in a container:

```bash
docker run --rm --user "$(id -u):$(id -g)" \
  -v "$PWD:/src" -v "$HOME/.nuget-lab:/nuget" -w /src \
  -e HOME=/tmp -e NUGET_PACKAGES=/nuget \
  -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 \
  mcr.microsoft.com/dotnet/sdk:8.0 \
  dotnet test apps/MineOS.Tests/MineOS.Tests.csproj
```

`--user` matters: without it the build writes root-owned `obj/`/`bin/` into your worktree. The `NUGET_PACKAGES` mount keeps restores fast across runs.

**The suite should be 0 failures.** If you see integration tests failing on `/var/games`, you are on a commit older than the fix that isolated them to a temp root — rebase.

## Standing up the lab

Work from a git worktree so the lab never disturbs your checkout.

```bash
LAB=/tmp/mclab && mkdir -p "$LAB/minecraft" "$LAB/data"
cat > "$LAB/.env" <<EOF
ConnectionStrings__DefaultConnection=Data Source=/app/data/mineos.db
Auth__SeedUsername=admin
Auth__SeedPassword=admin123!
Auth__JwtSecret=local-lab-jwt-secret-key-at-least-32-characters-long
ApiKey__SeedKey=local-lab-api-key-0123456789
HOST_BASE_DIRECTORY=$LAB/minecraft
Data__Directory=$LAB/data
Host__BaseDirectory=/var/games/minecraft
Host__OwnerUid=1000
Host__OwnerGid=1000
API_PORT=5378
WEB_PORT=3300
MC_PORT_RANGE=25565-25570
ORIGIN=http://localhost:3300
MINEOS_TELEMETRY_ENABLED=false
EOF

cat > "$LAB/override.yml" <<'EOF'
services:
  api:
    container_name: mclab-api
  web:
    container_name: mclab-web
networks:
  default:
    name: mclab-network
EOF

docker compose --env-file "$LAB/.env" -p mclab \
  -f docker-compose.yml -f docker-compose.build.yml -f "$LAB/override.yml" \
  up -d --build
```

Ports are deliberately off the defaults (`3300`, `5378`) because a dev instance often holds `3000`/`5078`. `HOST_BASE_DIRECTORY` and `Data__Directory` are absolute so compose can run from the repo while state lives outside it.

Web UI: `http://localhost:3300` — `admin` / `admin123!`.
API: `http://localhost:5378/api/v1`, header `X-Api-Key: local-lab-api-key-0123456789`.

**The API base path is `/api/v1`, and server listing is `GET /api/v1/host/servers`** (`/api/v1/servers` is POST-only). `GET /swagger/v1/swagger.json` is the fastest way to find a route.

## Building a proxy topology

Create backends **before** the proxy: proxy creation pre-populates its backend list from sibling Java servers.

```bash
K=local-lab-api-key-0123456789; B=http://localhost:5378/api/v1
for s in alpha beta; do
  curl -s -X POST -H "X-Api-Key: $K" -H 'Content-Type: application/json' \
    -d "{\"name\":\"$s\",\"ownerUid\":1000,\"ownerGid\":1000,\"serverType\":\"java\"}" "$B/servers"
done
curl -s -X POST -H "X-Api-Key: $K" -H 'Content-Type: application/json' \
  -d '{"name":"hub","ownerUid":1000,"ownerGid":1000,"serverType":"proxy","proxyKind":"velocity"}' "$B/servers"
```

Then, per server: download a profile, copy it in, accept the EULA, and trim memory.

```bash
curl -s -X POST -H "X-Api-Key: $K" "$B/profiles/paper-1.21.11/download"
curl -s -X POST -H "X-Api-Key: $K" -H 'Content-Type: application/json' \
  -d '{"serverName":"alpha"}' "$B/profiles/paper-1.21.11/copy-to-server"
curl -s -X POST -H "X-Api-Key: $K" -H 'Content-Type: application/json' \
  -d '{"accepted":true}' "$B/servers/alpha/eula"
```

Speed and memory matter — three JVMs on a laptop:

- `javaXmx` **1024** for backends, **512** for the proxy (the default is 4096 each).
- `level-type` `minecraft\:flat`, `view-distance` `4` — a flat world starts in seconds.
- Give each backend a distinct `motd` (`MOTD-alpha`). **That is how you prove routing**, since a status ping reports the backend's MOTD.

Fabric is not a profile. It has its own installer, and **`loaderVersion` is required** despite being nullable:

```bash
curl -s "$B/fabric/loader-versions" -H "X-Api-Key: $K"     # pick a stable one
curl -s -X POST -H "X-Api-Key: $K" -H 'Content-Type: application/json' \
  -d '{"minecraftVersion":"1.21.1","loaderVersion":"0.19.3","serverName":"gamma"}' "$B/fabric/install"
```

For routing tests, set the proxy's `pingPassthrough` to `ALL` — otherwise Velocity answers pings itself and every hostname looks identical.

## Probing Minecraft without a game client

`mcprobe.py` (next to this file) speaks enough of the protocol to answer the two questions that matter. The handshake carries the address the player typed, which is what `forced-hosts` routes on.

```bash
python3 mcprobe.py status 127.0.0.1 25565 alpha.example    # which backend answers?
python3 mcprobe.py login  127.0.0.1 25566 alpha.example    # will it let me in?
```

**Three traps, all of which have produced wrong conclusions:**

1. **The protocol version must match the server**, or you get `"Outdated client!"` and learn nothing about security. Read the real number from a status ping first (`version.protocol`) and pass it — do not hardcode.
2. **Packet `0x03` is Set Compression, not acceptance.** After it the stream is compressed and the probe goes blind, so a rejection can look like a successful login. Set `network-compression-threshold=-1` on the server under test, and corroborate with its log: a real join logs the player, a refusal logs `lost connection`.
3. **A connection reset usually means the server is still starting**, not that it refused you. Wait for `Done (` in `logs/latest.log`.

## Verifying forwarding security

The claim worth proving is that a *secured* backend refuses anyone who is not the proxy.

```
# Secured backend, direct connection:
{"outcome": "REJECTED", "challenged_on": "velocity:player_info",
 "message": "This server requires you to connect with Velocity."}

# Unsecured backend with online-mode=false, direct connection:
{"outcome": "ACCEPTED", "challenged_on": null}     # joined as any username
```

`challenged_on: velocity:player_info` is the signal: the backend demanded cryptographic proof of proxy identity. Its absence means nothing was verified.

To reproduce the dangerous state deliberately: set a backend's `online-mode=false` **without** securing it, restart, and log in as any name. Turn `white-list` off first, or an unrelated whitelist will mask the result.

## Teardown

```bash
docker compose -p mclab down
rm -rf /tmp/mclab
```

Removing the state directory matters: worlds and jars add up to hundreds of megabytes.
