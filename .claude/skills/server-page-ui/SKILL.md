---
name: server-page-ui
description: Use when adding, moving or restyling anything on a server or proxy detail page in mineos-sveltekit — page header, tabs, status chips, title-row actions, or a new panel/tab under /servers/[name] or /proxies/[name].
---

# Server & proxy detail pages

## The one thing to know

**`/servers/[name]` and `/proxies/[name]` are the same UI.** Both route layouts are
five-line delegates to a single shared component:

```svelte
<script lang="ts">
	import ServerShell from '$lib/components/server/ServerShell.svelte';
	let { data, children } = $props();
</script>

<ServerShell {data}>{@render children()}</ServerShell>
```

Chrome — header, title row, status badge, tabs, icon uploader, heartbeat stream — lives in
`ServerShell.svelte`. The route layouts hold none of it.

## Where your change goes

| Adding | Goes in |
|---|---|
| Header content, a title-row action, a chip | `lib/components/server/ServerShell.svelte` |
| A whole tab/panel | `lib/components/server/<Thing>Panel.svelte`, plus a route folder with a thin `+page.svelte` |
| A tab entry | `lib/utils/serverTabs.ts` (`buildTabs`) |
| Data every panel needs | `lib/components/server/panelData.ts` (`ServerPanelData`) |

Panels type their props as `ServerPanelData`, **not** a route's generated `./$types`. They
are shared by two routes, so no single route's generated types describe them.

## Editing the shell is editing both pages

A change to `ServerShell` shows up on proxies as well as game servers. That is usually
what you want — a proxy is a server — but decide it deliberately rather than discovering
it. If something must be servers-only, branch on `server?.serverType` inside the shell
(`isProxy` is already derived there); do not fork the component.

## The trap

This shell was extracted after the detail pages already existed. Any branch cut before the
extraction still contains the old ~700-line inline layout, and git will merge it back
without complaining — the route layout is a file both sides legitimately edited. The result
silently reverts the extraction, or drops whatever the branch added to the header.

Two feature branches hit this already. Both needed the same handling:

1. Take the **thin** route layout (the five-line delegate).
2. Port the branch's header/title-row work into `ServerShell` by hand.
3. Check whether the ported UI should appear on proxies too, and say so in the PR.

If you are rebasing a long-lived branch that touches `servers/[name]/+layout.svelte`,
expect this and budget for a manual port, not a merge.

## Before you push

`npm run check` from `apps/web` catches an unused CSS selector when markup moves out from
under its styles — a reliable signal that a port left something behind.
