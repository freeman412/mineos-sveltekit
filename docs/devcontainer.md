# Dev Containers

This repo uses a Docker Compose-based Dev Containers setup with two services:

- `api`: .NET 8 backend + tools + Minecraft dependencies
- `web`: SvelteKit frontend

## Prereqs

- Docker Desktop (WSL2 enabled on Windows)
- VS Code with the "Dev Containers" extension

## Open the containers

1) Open the repo in VS Code.
2) Open Command Palette: `Dev Containers: Open Folder in Container...`
3) Pick a config:
   - `api` -> attaches to the backend container
   - `web` -> attaches to the frontend container

Both containers start via Compose and share the repo volume.

## Run the backend

In the API container terminal:

```sh
dotnet watch --project apps/MineOS.Api run
```

The API listens on `http://localhost:5078` (forwarded from the container).

Minecraft servers started inside the API container are reachable on `localhost:25565-25568`
(TCP/UDP). If you need a different range, update `.devcontainer/docker-compose.yml`.

## Run the frontend

In the web container terminal:

```sh
cd apps/web
npm run dev -- --host 0.0.0.0 --port 5174
```

The web app listens on `http://localhost:5174`.

## Environment wiring

The web container is configured to call the API with:

- `PRIVATE_API_BASE_URL=http://api:5078`
- `PRIVATE_API_KEY=dev-static-api-key-change-me`

These are set in `.devcontainer/docker-compose.yml`. The API key must match
`ApiKey:StaticKey` in `apps/MineOS.Api/appsettings.Development.json`.

For CurseForge integration, set `CurseForge:ApiKey` in
`apps/MineOS.Api/appsettings.Development.json` (the API key is required to
query CurseForge and install mods/modpacks).

## Dependencies

The API container includes the following Minecraft server management dependencies:

- **rdiff-backup**: For incremental server backups (Phase 2)
- **tar**: For compressed archive creation (.tar.gz)
- **screen**: For managing server processes
- **rsync**: For file synchronization (used in Phase 4 profiles)
- **temurin-25-jdk**: Default Java runtime (`JAVA_HOME`); required by Velocity 4.x
- **temurin-21-jdk**: Paper 1.21+ and Velocity 3.5.x
- **temurin-8-jre**: Legacy Java 8 support

## Tips

- Need a shell in the other service? Use `Dev Containers: Attach to Running Container...`
- If port 5173 or 5078 is in use, update `.devcontainer/docker-compose.yml` and re-open.
- Leave `java_binary` blank and MineOS picks a runtime for you: proxies from the jar's own bytecode
  target, game servers from the Minecraft version. Set it only to override that choice.
- To target a specific runtime, set `java_binary` in the server config (or the Advanced page) to a full
  path. The Java Binary field suggests what is actually installed, read from the host at page load;
  paths are arch-suffixed, e.g. `/usr/lib/jvm/temurin-25-jdk-arm64/bin/java`.
