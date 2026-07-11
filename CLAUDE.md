# CLAUDE.md

**Read `AGENTS.md` — it holds the full rules for changing this repo.** The
non-negotiables, in brief:

1. **Clean Architecture — dependencies point inward only.**
   `Api → Infrastructure → Application → Domain`. Domain and Application stay
   framework-free (no EF/ASP.NET/Npgsql). Endpoints depend on Application
   **interfaces**, resolved via DI — never on concrete Infrastructure types.
   Enforced by `apps/MineOS.Tests/Architecture/LayerDependencyTests.cs`; don't
   weaken it to fit a change.

2. **Tests green before "done."** CI does **not** run the .NET suite — run it
   locally: `dotnet test apps/MineOS.Tests/MineOS.Tests.csproj`. Never claim done
   with failing or absent tests. Frontend: `cd apps/web && npm run check`.

3. **Don't bypass auth.** Valid `X-Api-Key` = admin; `ServerAccessFilter` gates
   every server-scoped route; BuildTools/imports stay under the admin-only group;
   console commands use `-X stuff` (not `eval`) with escaping intact. Any change to
   who-can-do-what must be explicit in the PR.

4. **Branch & release flow.** `vibing` is staging and the **default branch — base
   PRs on it, not `master`.** Never push to `master` (protected) or push a `v*` tag
   unprompted. Releases are tag-driven; see `.claude/skills/releasing/SKILL.md`.

Full detail, the layer table, and where-things-go: **`AGENTS.md`**.
