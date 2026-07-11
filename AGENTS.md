# Agent & contributor rules

Rules for anyone — human or AI agent — changing this repo. They are not style
preferences; several are enforced by tests and by branch protection, and a change
that breaks them will not merge. Read this before writing code.

**Violating the letter of these rules is violating the spirit of them.** If a rule
seems to block a shortcut, the rule wins — raise it in the PR instead of routing
around it.

---

## 1. Clean Architecture — dependencies point inward, always

The backend is layered. Dependencies may only point **inward**:

```
Api  ──►  Infrastructure  ──►  Application  ──►  Domain
(HTTP,        (impls, EF,         (interfaces,      (entities,
 DI root)      external I/O)       DTOs, use cases)   enums — pure C#)
```

| Layer | May depend on | MUST NOT contain |
|-------|---------------|------------------|
| `MineOS.Domain` | nothing | any framework — no EF, no ASP.NET, no Npgsql, no other MineOS layer |
| `MineOS.Application` | Domain only | Infrastructure/Api types; EF/ASP.NET/Npgsql. It defines interfaces; it does not implement I/O |
| `MineOS.Infrastructure` | Application, Domain | references to Api |
| `MineOS.Api` | Application, Infrastructure | business logic (it wires DI and maps HTTP; logic lives in Application/Infrastructure) |

**Where things go:** new entity → Domain. New capability contract (`IFooService`) or
DTO → Application. Implementation of that contract (HTTP calls, EF, `screen`/process
work, filesystem) → Infrastructure. New endpoint → Api, depending on the Application
**interface**, resolved via DI — never `new`-ing a concrete Infrastructure type.

**This is enforced.** `MineOS.Tests/Architecture/LayerDependencyTests.cs` fails red if
Domain or Application gains a framework/outward reference, or if Infrastructure
references Api. Do not weaken or `[Skip]` those tests to make a change fit — the change
is wrong, not the test. (The `.csproj` graph already makes outward *project* references
circular and thus uncompilable; the tests additionally catch framework leakage, which
compiles but is still forbidden.)

## 2. Tests must be green before anything is "done"

CI **only builds the web app — it does not run the .NET tests.** So the .NET suite is
your responsibility locally:

```
dotnet test apps/MineOS.Tests/MineOS.Tests.csproj
```

Never mark a task complete, open a PR as ready, or claim success with failing or
absent tests. If you changed backend behavior, a passing suite that never exercised
your change is not evidence — add or extend a test. Frontend: `cd apps/web && npm run check`.

## 3. Security & auth invariants — do not bypass

The API is protected in layers. Keep them intact:

- **`ApiKeyMiddleware`**: a valid `X-Api-Key` authenticates as an **admin** identity.
  The middleware order in `Program.cs` is `UseAuthentication → ApiKeyMiddleware →
  UseAuthorization` — do not reorder it.
- **`ServerAccessFilter`**: every **server-scoped** route (`/servers/{name}/...`,
  players, worlds, performance, console) must carry the per-server ACL check. If you
  add a server-scoped endpoint group, add the filter — a non-admin must not reach a
  server they lack access to.
- **Admin-only host operations** (BuildTools, imports upload / create-server / delete)
  live under the role-gated `adminHost` group. Keep destructive/host-wide actions there.
- **Console commands** are delivered to `screen` via `-X stuff` (not `eval`) and the
  payload is escaped in `ProcessManager.EscapeForScreenStuff`. Don't reintroduce an
  `eval` re-parse or drop the escaping — it prevents screen-command injection.

Changing who-can-do-what is a deliberate, reviewable decision. Call it out explicitly
in the PR; don't smuggle it in.

## 4. Branch & release flow

- **`vibing` is staging and the default branch. Base every PR on `vibing`, not
  `master`.** `master` is production/stable.
- **Never push directly to `master`** (it is branch-protected) and **never push a
  `v*` tag unprompted** — tags trigger real releases.
- Releases are **tag-driven**: a `v*` tag builds images. A hyphen in the tag
  (`v1.2.0-beta.3`) publishes `:preview`/prerelease; no hyphen (`v1.2.0`) publishes
  `:latest`/stable. Version comes from the tag — do not bump a version in source.
- Betas/RCs are tagged on `vibing`; stable is promoted to `master` and tagged there.
- Full process, checklist, and common mistakes: `.claude/skills/releasing/SKILL.md`.

---

## Project shape (orientation)

- `apps/MineOS.{Domain,Application,Infrastructure,Api}` — the .NET 8 backend (layers above).
- `apps/MineOS.Tests` — xUnit: `Unit/`, `Integration/`, `Architecture/`.
- `apps/web` — SvelteKit 2 / Svelte 5 (runes: `$state`/`$derived`/`$effect`). Talks to
  the API through the `/api/v1` proxy, which forwards both `X-Api-Key` and the user JWT.
- Servers are managed as `screen` sessions under `/var/games/minecraft` on the host.
