# Fix recurring 403 Forbidden: dynamic same-origin CSRF + self-explaining errors

**Date:** 2026-06-12
**Issues:** #100 (403 Forbidden), likely #71 (Bad Request), #97 (Web UI won't open)

## Problem

SvelteKit's built-in CSRF protection rejects any form POST whose `Origin` header
doesn't exactly match the single `ORIGIN` env var (default
`http://localhost:3000` in docker-compose). MineOS is a server app: users
normally browse to a LAN IP (`http://192.168.1.50:3000`), a hostname, or go
through a reverse proxy — so a mismatch is the *common* case, not an edge case.

The failure mode is maximally confusing:

- Pages load fine (GETs aren't checked); only login/form POSTs fail.
- The 403 is bare text emitted by SvelteKit core before any app code runs.
- Nothing appears in server logs.
- `install.sh` never asks which address the user will browse from, and only one
  origin can be valid at a time (LAN IP *or* localhost, never both).

Result: issue #100 keeps being re-reported.

## Goals

1. Accessing MineOS directly via **any** address — localhost, LAN IP, hostname —
   works with **zero origin configuration**. The browser's `Origin` header is
   validated against the request's own `Host` header instead of a fixed value,
   so the configured origin no longer matters for direct access.
2. Reverse-proxy users either work automatically (proxy forwards `Host`) or get
   a **self-explaining 403 page** showing the observed values and the exact fix.
3. Rejections produce a clear, single-line server log (no more "logs say nothing").
4. Remaining cases self-diagnose via docs and an improved bug-report template.

## Non-goals

- No change to the .NET API CORS config (`Cors__AllowedOrigins`). Browser
  traffic reaches the API through the SvelteKit server proxy
  (`src/lib/server/proxyApi.ts`), so API CORS is not what users are hitting.
- No CSRF token system. Same-origin header validation retains equivalent
  protection for this app's threat model.

## Design

### 1. Dynamic same-origin CSRF check (root-cause fix)

- `apps/web/svelte.config.js`: set `kit.csrf.checkOrigin: false` to disable the
  fixed-origin check.
- New `apps/web/src/hooks.server.ts` `handle` hook that re-implements the check
  dynamically. It mirrors SvelteKit's own scope — only state-changing methods
  (POST/PUT/PATCH/DELETE) with form-capable content types
  (`application/x-www-form-urlencoded`, `multipart/form-data`, `text/plain`)
  are validated; JSON/API requests are untouched.
- Validation passes when **any** of these hold:
  1. `Origin` host:port equals `Host` header host:port. Protocol is
     deliberately ignored: behind a TLS-terminating proxy the browser sends
     `https://…` while the internal request is `http://…`, and host equality is
     what actually defends against CSRF (a cross-site attacker cannot forge a
     matching `Origin`).
  2. `Origin` host:port equals `X-Forwarded-Host` (proxies that rewrite `Host`
     but forward the original — header is attacker-controlled only if the proxy
     itself is misconfigured, same trust level as Host).
  3. `Origin` exactly equals the `ORIGIN` env var, if set (backward
     compatibility with existing installs).
- Missing or unparseable `Origin` on a form POST is treated as a mismatch
  (same as SvelteKit today). URL parsing is wrapped so a malformed header can
  never crash the hook.
- The matching logic lives in a pure function
  (`apps/web/src/lib/server/originCheck.ts`) so it is unit-testable.
- `ORIGIN` stays supported and optional: adapter-node still uses it for
  absolute URL generation when set; `.env.template` comments updated to say it
  is no longer required for login to work.

### 2. Self-explaining 403 page + logging

When validation fails the hook returns a standalone styled HTML page (no app
dependencies, inline CSS) containing:

- The observed `Origin`, `Host`, and `X-Forwarded-Host` values from the actual
  request.
- Plain-language explanation: "Your browser is at X but the server received the
  request as Y."
- Targeted remediation: reverse-proxy users → forward the `Host` header (nginx
  `proxy_set_header Host $host;`, Caddy/Traefik defaults already do this);
  others → link to TROUBLESHOOTING doc.

The hook also emits one structured `console.warn` line with the same values so
the server logs finally show the problem.

### 3. Docs & triage

- `docs/TROUBLESHOOTING.md` — new doc, first section: "403 Forbidden / login
  does nothing", explaining the mechanism and proxy fixes.
- README: FAQ entry linking to it.
- `.env.template`: update `ORIGIN` comments (optional, what it still does).
- `.github/ISSUE_TEMPLATE/bug_report.yml`: add fields for "URL in your browser
  address bar" and "Are you using a reverse proxy? Which one?".
- After merge/release: comment on #100 with the fix, verify whether #71 and #97
  share the cause, close as appropriate.

## Error handling

- Malformed `Origin`/`Host` headers → treated as mismatch, never a crash.
- The hook runs before all routes; non-form requests pass through with no
  overhead beyond a method/content-type check.

## Testing

- **Unit tests** for the pure `originCheck` function: same host:port passes;
  protocol mismatch passes; differing host fails; differing port fails;
  X-Forwarded-Host match passes; ORIGIN env match passes; missing/garbage
  Origin fails. (Add a minimal vitest setup if none exists; only Playwright is
  configured today.)
- **E2E (Playwright):** login via `127.0.0.1:<port>` while the app has no
  `ORIGIN` set — previously a guaranteed 403, must now succeed.
- **Manual:** `curl -X POST` with mismatched `Origin` + form content type
  returns the explanatory page and logs a warning; JSON POSTs unaffected.
