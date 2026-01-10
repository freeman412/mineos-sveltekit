# API Contract (Draft) - .NET 8 Minimal API

This is a first-pass contract derived from the current Node/Socket.IO behavior. It targets container deployment and keeps parity with existing MineOS operations.

## Conventions
- Base path: /api/v1
- Content type: application/json
- All endpoints return standard problem details on error.
- Auth:
  - API: `X-Api-Key` header for protected backend endpoints.
  - User: JWT Bearer token for `/auth/me` and future user endpoints.
- Health: GET /api/v1/health

## Data types (shape only)
- HostMetrics: { uptimeSeconds, freeMemBytes, loadAvg: [1,5,15], disk: { availableBytes, freeBytes, totalBytes } }
- ServerSummary: { name, up, profile, port, playersOnline, playersMax }
- ServerHeartbeat: { up, memory, ping, query, timestamp }
- Profile: { id, group, type, version, releaseTime, url, filename, downloaded, progress? }
- ArchiveEntry: { time, size, filename }
- IncrementEntry: { time, step, size?, cum? }
- ServerProperties: { [key: string]: string }
- ServerConfig: { [section: string]: { [key: string]: string } }
- CronJob: { hash, source, action, msg, enabled }
- Notice: { uuid, command, success, err?, timeInitiated, timeResolved }

## Auth
- POST /api/v1/auth/login { username, password } -> { accessToken, expiresInSeconds, tokenType, username, role }
- POST /api/v1/auth/logout
- GET /api/v1/auth/me (Bearer token) -> { username, role }

## Host
- GET /api/v1/host/metrics -> HostMetrics
- GET /api/v1/host/metrics/stream?intervalMs=2000 -> SSE stream of HostMetrics
- GET /api/v1/host/servers -> ServerSummary[]
- GET /api/v1/host/profiles -> Profile[]
- POST /api/v1/host/profiles/{id}/download
- POST /api/v1/host/profiles/buildtools { group, version }
- DELETE /api/v1/host/profiles/buildtools/{id}
- POST /api/v1/host/profiles/{id}/copy-to-server { serverName, type }
- GET /api/v1/host/imports -> ArchiveEntry[]
- POST /api/v1/host/imports/{filename}/create-server { serverName }
- GET /api/v1/host/locales -> string[]
- GET /api/v1/host/users -> { username, uid, gid, home }[]
- GET /api/v1/host/groups -> { groupname, gid }[]

## Servers
- POST /api/v1/servers { serverName, properties, unconventional?: boolean }
- DELETE /api/v1/servers/{name} { deleteLive, deleteBackups, deleteArchives }
- GET /api/v1/servers/{name}/status -> ServerHeartbeat
- POST /api/v1/servers/{name}/actions/{action}
  - action: start | stop | restart | kill | stop-and-backup | backup | archive | archive-with-commit | restore | prune | saveall | saveall-latest-log | renice
  - body varies by action (e.g., { step } for restore/prune, { niceness } for renice)
- POST /api/v1/servers/{name}/console { command }
- GET /api/v1/servers/{name}/server-properties -> ServerProperties
- PUT /api/v1/servers/{name}/server-properties -> ServerProperties
- GET /api/v1/servers/{name}/server-config -> ServerConfig
- PUT /api/v1/servers/{name}/server-config -> ServerConfig
- GET /api/v1/servers/{name}/archives -> ArchiveEntry[]
- GET /api/v1/servers/{name}/backups -> IncrementEntry[]
- GET /api/v1/servers/{name}/backups/sizes -> IncrementEntry[]

## Cron
- GET /api/v1/servers/{name}/cron -> CronJob[]
- POST /api/v1/servers/{name}/cron { source, action, msg }
- PATCH /api/v1/servers/{name}/cron/{hash} { enabled }
- DELETE /api/v1/servers/{name}/cron/{hash}

## Logs/files
- GET /api/v1/servers/{name}/logs -> { paths: string[] }
- GET /api/v1/servers/{name}/logs/head/{*path} -> { payload }

## Events (SignalR or WebSocket)
Recommended: SignalR hubs for host + per-server events.

HostHub (subscribe once):
- HostDiskspace
- HostHeartbeat
- ProfileList
- SpigotList
- ImportableList
- LocaleList
- UserList
- GroupList
- HostNotice
- TrackServer
- UntrackServer
- BuildJarOutput

ServerHub (join by server name):
- Heartbeat
- PageData
- Archives
- Increments
- IncrementSizes
- TailData
- FileHead
- Notices
- ServerAck
- ServerFin
- ServerIcon
- ServerProperties
- ServerConfig
- ConfigYml
- CronConfig
- Eula

## Notes
- The current Socket.IO model mixes commands and events; the rewrite should favor HTTP for commands and websocket/SignalR for live data.
- Existing operations map directly to file/process actions; replace with explicit allowlists and strong input validation.
