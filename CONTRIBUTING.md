# Contributing to MineOS (SvelteKit)

Thanks for contributing! A few things keep this project releasable and safe. Please
skim them before opening a PR.

## Branch flow — base your PR on `vibing`

- **`vibing` is the staging branch and the repo default. Open your PR against
  `vibing`.** New work is integrated and beta-tested here first.
- `master` is the stable/production branch. It is protected — it only receives
  vetted work promoted from `vibing`, and releases are cut from tags. Please don't
  target `master` directly; a PR opened against it will be asked to retarget.

## Before you open the PR

- **Run the backend tests** — CI does *not* run them for you:
  ```
  dotnet test apps/MineOS.Tests/MineOS.Tests.csproj
  ```
- **Run the frontend check** if you touched `apps/web`:
  ```
  cd apps/web && npm run check
  ```
- Keep the change within the **Clean Architecture** boundaries (see `AGENTS.md`).
  The architecture tests will fail the build if an inner layer gains an outward or
  framework dependency.

## Architecture, security, and release rules

The full set of invariants — layer dependency rules, auth/security invariants, and
the tag-driven release process — lives in **[`AGENTS.md`](./AGENTS.md)**. It applies
to human and AI contributors alike. If you use an AI coding agent, point it at that
file (and `CLAUDE.md`, which agents read automatically).

## Local development

The stack runs under Docker Compose (`api` + `web`). Build locally with:
```
docker compose -f docker-compose.yml -f docker-compose.build.yml build
docker compose up -d
```
The web UI is served on port 3000 and the API on 5078. See `docker-compose*.yml` and
`install.sh` for host/production variants.
