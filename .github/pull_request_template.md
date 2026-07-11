<!--
  Base this PR on `vibing` (staging), not `master`. See CONTRIBUTING.md / AGENTS.md.
-->

## What & why



## Checklist

- [ ] Based on **`vibing`** (not `master`)
- [ ] `.NET` tests pass locally — `dotnet test apps/MineOS.Tests/MineOS.Tests.csproj` (CI does **not** run these)
- [ ] `apps/web` check passes if the frontend changed — `cd apps/web && npm run check`
- [ ] Stays within the Clean Architecture layer rules (`AGENTS.md`) — no outward/framework deps in Domain or Application
- [ ] No change to auth/access behavior — or, if there is, it's called out explicitly below

## Auth / access impact

<!-- None, or describe exactly what changes about who-can-do-what. -->
None.
