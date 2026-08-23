---
name: releasing-mineos
description: Use when cutting, tagging, or publishing a mineos-sveltekit release, choosing a version number, or asked to "ship it" / "cut a release" / "make a build" / "publish images" for this repo.
---

# Releasing mineos-sveltekit

## Overview

Releases are **100% tag-driven**. Pushing a `v*` git tag is the entire release action — it triggers `publish-images.yml` (GHCR Docker images) and `publish-install-bundle.yml` (CLI binaries + install bundle + the GitHub Release). There are no version numbers to bump in source: the tag string flows into images and binaries via build args. Get the tag string and the tagged branch right and everything downstream is automatic; get them wrong and you silently ship a regression or clobber a release channel.

## The Iron Rules

1. **A release is a tag push, nothing else.** Never edit source "for the release." Never push to `master`/`main` to release. The version comes from the tag.
2. **The tag string is load-bearing — one character decides the channel.** The workflow routes purely on "does the tag contain a hyphen":
   - `vX.Y.Z` (no hyphen) → moves the `:latest` image tag **and** GitHub `releases/latest` (what `install.sh` installs by default). This is a **stable** release to every default user.
   - `vX.Y.Z-beta.N` / `-rc.N` / `-alpha.N` (has hyphen) → moves `:preview`, marked prerelease. Default installs are untouched.
   - A typo like `v1.2.0.beta.3` (dot, not hyphen) reads as **stable** and hijacks `:latest`. Type the tag exactly; re-read it before pushing.
3. **Tag the branch the version train lives on — not reflexively `master`.** History: `-beta`/`-rc` tags are cut on the **`vibing`** branch; the `vX.Y.Z` stable tag is cut on `master` after merging `vibing → master`. Confirm with `git branch -r --contains <last-tag>` before assuming.
4. **CI does not run the .NET tests.** Since #178 `node.js.yml` runs `npm ci`, `npm run test:unit`, `npm run check` and `npm run build`, so the **frontend** is covered. The xUnit suite (159 tests) still never runs in CI, so **run it locally before every tag** or you ship untested backend code.
5. **The tag push requires human approval** (release-guard hook). Prepare the tag, show your partner the exact command and version, and let them approve the push. Never work around the guard.

## Version scheme

Semver `MAJOR.MINOR.PATCH` with dotted pre-release suffixes:

| Change on the branch | Next version |
|---|---|
| New user-facing feature | bump MINOR, reset PATCH → `1.3.0` |
| Bug/security fix only | bump PATCH → `1.2.1` |
| Iterating toward an unreleased `X.Y.Z` | `X.Y.Z-beta.N`, then `X.Y.Z-rc.N`, then `X.Y.Z` |

- Pre-release order that has been used: `-beta.1..N` → `-rc.1..N` → final (no suffix). `-alpha` is supported by the workflow but hasn't been used.
- New features soak as betas before stable (v1.1.0 took 6 betas + 1 rc). Don't tag a bare `vX.Y.Z` stable for code that has had zero preview soak — cut a `-beta.N` first.
- No `v` typos, no four-part versions except where history already did (`v1.0.2.1-rc.1` exists but don't imitate it).

## Cutting a release — checklist

1. **Confirm the merge landed.** `gh pr view <N> --json state,mergeCommit`; `git fetch origin --tags`; `git log --oneline <last-tag>..origin/<branch>` to see exactly what's shipping.
2. **Check for branch divergence.** `git log --oneline origin/master..origin/vibing` and the reverse. If the preview line (`vibing`) has commits master doesn't (or vice-versa), tagging naively **regresses** the other channel. Reconcile (merge) first — see Common Mistakes.
3. **Run the tests CI skips.** `dotnet test mineos-sveltkit.sln` — expect **159 passing**. Note the `.sln` filename is misspelled `sveltkit`; that's correct. The frontend (`test:unit`, `check`, `build`) is already covered by CI since #178, so you only need it locally if you're tagging without a green CI run on the branch tip.

   **No local `dotnet`?** It is not installed on every dev machine. Run the suite in the SDK container instead:

   ```bash
   docker run --rm -v "$PWD":/src -w /src \
     -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 \
     mcr.microsoft.com/dotnet/sdk:8.0 \
     dotnet test apps/MineOS.Tests/MineOS.Tests.csproj
   ```
4. **Pick the version** per the table above; decide beta vs rc vs stable by soak time.
5. **Create an annotated tag whose message is the changelog** (this repo puts release notes in the tag message, not the GitHub release body). See below.
6. **Show your partner the tag + push command and get approval**, then push. `git push origin <tag>`.
7. **Watch the two workflows.** `gh run list --limit 4`; `gh run watch <id>`.
8. **Verify the channel.** For a prerelease, confirm `:latest` did **not** move: `docker buildx imagetools inspect ghcr.io/freeman412/mineos-api:latest` digest should be unchanged — record it *before* pushing so you have something to compare against. For a stable, confirm it did. Check `gh release view <tag> --json isPrerelease,assets` (expect **9** assets: 2 bundles + 6 CLI zips + `checksums.txt`).

9. **Publish the release notes — the workflow does not.** `publish-install-bundle.yml` creates the GitHub Release with an **empty body**; the notes live only in the tag message. This is why v1.2.0-beta.4, beta.5 and beta.7 all shipped with blank release pages. Copy the tag message over once the workflow finishes:

   ```bash
   git tag -l --format='%(contents)' <tag> > /tmp/notes.md
   gh release edit <tag> --notes-file /tmp/notes.md
   ```

   Fixing the workflow to do this itself would retire the step; until then it is manual and easy to forget.

## Annotated tag = changelog

```bash
git tag -a v1.2.0-beta.3 -m "v1.2.0-beta.3

Fixes:
- Paper version fetching migrated to PaperMC Fill v3 (v2 API sunset, 410 Gone) (#115)
- Proxy port allocation counts velocity.toml binds; fresh configs use config-version 2.7 (#115)

Includes Velocity proxy support (#63, #99)."
# then, after approval:
git push origin v1.2.0-beta.3
```

Read `git tag -l --format='%(contents)' v1.1.0` for the house style (title line, "N issues resolved…", Features/Fixes sections).

**`CHANGELOG.md` is not part of the release action.** #179 added one, but Iron Rule 1 still holds: a release is a tag push and nothing else. Keep the changelog current in the normal feature PRs that change behaviour, not in a commit cut for the tag. Its heading carries a `currently \`vX.Y.Z-beta.N\`` marker that goes stale as betas roll — fix that in a docs PR, never by committing on top of a release.

## What the tag triggers (reference)

- **publish-images.yml** — builds multi-arch (amd64+arm64) `ghcr.io/freeman412/mineos-api` and `mineos-web`. Channel tag (`:latest` vs `:preview`) per the hyphen rule; also tags `:vX.Y.Z…` and `:sha-…`. Injects the tag as `MINEOS_IMAGE_TAG` (api) / `PUBLIC_BUILD_ID` (web About page).
- **publish-install-bundle.yml** — builds `mineos-cli` for 6 OS/arch targets, packages the install bundle, creates the GitHub Release with 9 assets (2 bundles, 6 CLI zips, `checksums.txt`). `prerelease: true` iff the tag contains `-beta`/`-alpha`/`-rc`. It does **not** write a release body — see checklist step 9.
- **Not triggered:** the TrueNAS catalog under `deployments/truenas/` is published by `scripts/publish-truenas-catalog.sh` separately — only run it if that directory changed.

## Common Mistakes

| Mistake | Consequence | Do instead |
|---|---|---|
| `git push origin master` to "release" | No release happens (only tags trigger); and it's a forbidden direct push | Push a tag; releases are tags only |
| Tag `master` while `vibing` is ahead | Beta regresses — preview users lose commits only on `vibing` | Reconcile branches first (merge `master`→`vibing` or vice-versa), re-test, then tag |
| Bare `vX.Y.Z` for freshly-merged feature | Ships unsoaked code straight to every stable user via `:latest` | Cut `-beta.N` first; promote to stable after soak |
| Hyphen typo (`v1.2.0.beta.3`) | Reads as stable, hijacks `:latest` | Re-read the exact tag string before pushing |
| Skip local `dotnet test` | Ship backend code CI never tested | Run the 159 tests locally every time (SDK container if no local `dotnet`) |
| Bump versions in `package.json`/source | Pointless churn; ignored | Version comes only from the tag |

## Red Flags — STOP

- About to `git push` a branch to release something → wrong; releases are tags.
- About to tag without running `git log <last-tag>..` on both `master` and `vibing` → you don't know what you're shipping or whether channels diverged.
- About to push a stable `vX.Y.Z` for code that hasn't been a beta → stop, cut a beta.
- Pushing a tag without showing your partner first → the release-guard hook will block you anyway; surface it and get approval.
