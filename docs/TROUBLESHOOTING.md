# Troubleshooting

## 403 Forbidden / login does nothing

> "I installed MineOS on another computer and can't log in from my PC."

**As of v1.2 this should not happen for direct access.** MineOS accepts any
address that reaches the machine it runs on — `http://localhost:3000`,
`http://192.168.1.50:3000`, `http://nas.local:3000` — with no configuration.
Login is validated by comparing your browser's `Origin` header against the
`Host` header of the same request, which always match for direct access.

If you still see a 403, the error page now shows the exact `Origin` and
`Host` values the server observed. The remaining causes:

### You're behind a reverse proxy that rewrites the Host header

Your proxy is forwarding requests with its *internal* upstream name as the
`Host`. Forward the original host instead:

**nginx**

```nginx
location / {
    proxy_pass http://127.0.0.1:3000;
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-Host $host;
    proxy_set_header X-Forwarded-Proto $scheme;
}
```

**Apache**

```apache
ProxyPreserveHost On
```

**Caddy / Traefik** — already forward `Host` by default; no action needed.

### You're running a version older than v1.2

Older versions required the `ORIGIN` value in `.env` to exactly match the
address in your browser. Either upgrade, or set `ORIGIN` in `.env` on the
MineOS machine to the exact URL you type in the browser (e.g.
`ORIGIN=http://192.168.1.50:3000`) and restart: `mineos stack restart`.

## Page loads but other devices can't reach MineOS at all

If the web UI doesn't load from other devices (connection refused/timeout,
not a 403), check the firewall on the MineOS machine allows the web port
(default 3000), and that `WEB_PORT` in `.env` matches the port you're using.
