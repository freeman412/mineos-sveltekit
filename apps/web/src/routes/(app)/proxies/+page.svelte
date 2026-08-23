<script lang="ts">
	import { onMount } from 'svelte';
	import { invalidateAll } from '$app/navigation';
	import { env } from '$env/dynamic/public';
	import { browser } from '$app/environment';
	import * as api from '$lib/api/client';
	import { formatBytes } from '$lib/utils/formatting';
	import { loadProxyOverviews, overlaySummaries, type ProxyOverview } from '$lib/utils/proxy';
	import { attachServerToProxy, detachServerFromProxy } from '$lib/utils/proxyAttach';
	import { createEventStream, type EventStreamHandle } from '$lib/utils/eventStream';
	import { modal } from '$lib/stores/modal';
	import ProxyBackendRollup from '$lib/components/ProxyBackendRollup.svelte';
	import CopyButton from '$lib/components/CopyButton.svelte';
	import StatusBadge from '$lib/components/StatusBadge.svelte';
	import type { PageData } from './$types';
	import type { BackendForwarding, ServerSummary } from '$lib/api/types';

	let { data }: { data: PageData } = $props();

	const hostname = $derived.by(() => {
		const envHost = env.PUBLIC_MINECRAFT_HOST as string | undefined;
		return (envHost && envHost.trim()) || (browser ? window.location.hostname : 'localhost');
	});

	let actionLoading = $state<Record<string, boolean>>({});
	let actionError = $state<Record<string, string>>({});
	// Newest summaries from the host SSE stream, keyed by name; the cards
	// re-derive from these so status/players/memory tick without a reload.
	let liveSummaries = $state<Record<string, ServerSummary>>({});
	let serversStream: EventStreamHandle | null = null;

	// Backend rollups, seeded from load and refreshed after attach/detach/
	// fix-forwarding or when a proxy's running state flips.
	let overviewMap = $state<Record<string, ProxyOverview>>(
		Object.fromEntries(data.proxies.map((p) => [p.name, p.overview]))
	);
	let overviewBusy = $state<Record<string, boolean>>({});
	/** Backend an attach/detach/fix action is running on, per proxy. */
	let backendBusy = $state<Record<string, string | null>>({});
	/**
	 * Per-proxy pick in the "Attach a server" dropdown. Missing keys read as
	 * the empty placeholder, so the select starts on "Choose a server…"
	 * instead of showing a candidate the disabled button would not act on.
	 */
	let attachPick = $state<Record<string, string>>({});

	// The proxy list itself (which proxies exist) still comes from load;
	// the stream only refreshes each row's summary.
	const proxies = $derived(overlaySummaries(data.proxies, liveSummaries));

	onMount(() => {
		serversStream = createEventStream<ServerSummary[]>({
			url: '/api/host/servers/stream',
			onMessage: (nextServers) => {
				const next: Record<string, ServerSummary> = {};
				for (const server of nextServers) next[server.name] = server;
				liveSummaries = next;
			},
			reconnect: {},
			onClose: () => {
				serversStream = null;
			}
		});

		return () => {
			serversStream?.close();
		};
	});

	function isRunning(proxy: PageData['proxies'][number]): boolean {
		if (proxy.summary) return proxy.summary.up;
		return (proxy.detailStatus ?? '').toLowerCase() === 'running';
	}

	async function refreshOverview(proxyName: string) {
		if (overviewBusy[proxyName]) return;
		overviewBusy[proxyName] = true;
		try {
			const [overview] = await loadProxyOverviews(fetch, [proxyName]);
			if (overview) overviewMap[proxyName] = overview;
		} finally {
			delete overviewBusy[proxyName];
		}
	}

	// A restart can change what the proxy reports about its backends, so
	// re-check them whenever a proxy's running state flips.
	let prevRunning = new Map<string, boolean>(
		data.proxies.map((p) => [p.name, isRunning(p)])
	);
	$effect(() => {
		for (const proxy of proxies) {
			const now = isRunning(proxy);
			const before = prevRunning.get(proxy.name);
			prevRunning.set(proxy.name, now);
			if (before !== undefined && before !== now) void refreshOverview(proxy.name);
		}
	});

	function overviewFor(proxy: PageData['proxies'][number]): ProxyOverview {
		return overviewMap[proxy.name] ?? proxy.overview;
	}

	function candidateBackends(proxy: PageData['proxies'][number]): string[] {
		const attached = new Set(
			(overviewFor(proxy).summary?.backends ?? []).map((b) => b.serverName)
		);
		return data.gameServers.filter((name) => !attached.has(name));
	}

	async function handleAction(name: string, action: 'start' | 'stop' | 'restart' | 'kill') {
		if (actionLoading[name]) return;
		actionLoading[name] = true;
		actionError[name] = '';
		try {
			const result =
				action === 'start'
					? await api.startServer(fetch, name)
					: action === 'stop'
						? await api.stopServer(fetch, name)
						: action === 'kill'
							? await api.killServer(fetch, name)
							: await api.restartServer(fetch, name);
			if (result.error) {
				actionError[name] = `${action} failed: ${result.error}`;
				return;
			}
			await invalidateAll();
		} finally {
			actionLoading[name] = false;
		}
	}

	// The pre-#176 server card could delete a proxy; the proxies card could not,
	// leaving no way to remove one from the section that owns it.
	async function handleDelete(name: string) {
		if (actionLoading[name]) return;
		const confirmed = await modal.confirm(
			`Delete proxy "${name}"? Its configuration, plugins and logs are removed permanently. ` +
				'Game servers behind it are not deleted, but they stop being reachable through it.',
			'Delete Proxy'
		);
		if (!confirmed) return;

		actionLoading[name] = true;
		actionError[name] = '';
		try {
			const result = await api.deleteServer(fetch, name);
			if (result.error) {
				actionError[name] = `delete failed: ${result.error}`;
				return;
			}
			await invalidateAll();
		} finally {
			actionLoading[name] = false;
		}
	}

	function clearBusy(proxyName: string) {
		delete backendBusy[proxyName];
	}

	async function handleAttach(proxyName: string) {
		const serverName = attachPick[proxyName];
		if (!serverName || backendBusy[proxyName]) return;
		actionError[proxyName] = '';
		backendBusy[proxyName] = serverName;
		try {
			const result = await attachServerToProxy(fetch, { serverName, proxyName });
			if (!result.ok) {
				actionError[proxyName] = result.error;
				// The attach may have registered the backend before failing, so the
				// rollup still needs refreshing before we surface the warning.
				await refreshOverview(proxyName);
				return;
			}
			attachPick[proxyName] = '';
			await refreshOverview(proxyName);
		} finally {
			clearBusy(proxyName);
		}
	}

	async function handleDetach(proxyName: string, backend: BackendForwarding) {
		if (backendBusy[proxyName]) return;
		const confirmed = await modal.confirm(
			`Remove ${backend.serverName} from ${proxyName}'s backend list? Players will no longer reach it through ${proxyName}.`,
			'Detach Server'
		);
		if (!confirmed) return;
		actionError[proxyName] = '';
		backendBusy[proxyName] = backend.serverName;
		try {
			const result = await detachServerFromProxy(fetch, {
				serverName: backend.serverName,
				proxyName
			});
			if (!result.ok) {
				actionError[proxyName] = result.error;
				return;
			}
			await refreshOverview(proxyName);
		} finally {
			clearBusy(proxyName);
		}
	}

	async function handleRemediate(proxyName: string, backend: BackendForwarding) {
		if (backendBusy[proxyName] || !backend.remediationAction) return;
		actionError[proxyName] = '';
		backendBusy[proxyName] = backend.serverName;
		try {
			if (backend.remediationAction === 'install-mod') {
				const modResult = await api.installForwardingMod(fetch, backend.serverName);
				if (modResult.error) {
					actionError[proxyName] = `Installing the forwarding mod failed: ${modResult.error}`;
					return;
				}
			}
			const secureResult = await api.secureBackend(fetch, backend.serverName);
			if (secureResult.error) {
				actionError[proxyName] = `Securing forwarding failed: ${secureResult.error}`;
				return;
			}
			await refreshOverview(proxyName);
		} finally {
			clearBusy(proxyName);
		}
	}
</script>

<svelte:head>
	<title>Proxies | MineOS</title>
</svelte:head>

<div class="page">
	<header class="header">
		<div>
			<h1>Proxies</h1>
			<p class="subtitle">
				Your proxies and the game servers behind them. Players join a proxy's address and hop
				between its servers — together, that's your network. Open one to reach its console,
				logs, files and plugins.
			</p>
		</div>
		<a class="btn-setup" href="/servers/new?type=proxy">+ Set up a proxy</a>
	</header>

	{#if data.proxies.length === 0}
		<div class="empty-state">
			<p><strong>No proxies yet.</strong></p>
			<p>
				A proxy (Velocity, BungeeCord, Waterfall) gives players one address to join, with your
				game servers attached behind it so they can hop between worlds without switching servers.
			</p>
			<a class="btn-setup" href="/servers/new?type=proxy">Set up a proxy</a>
		</div>
	{:else}
	{#snippet attachRow(proxy: PageData['proxies'][number], label: string)}
		{@const candidates = candidateBackends(proxy)}
		{#if candidates.length > 0}
			<div class="attach-row">
				<label class="attach-label" for="attach-{proxy.name}">{label}</label>
				<select
					id="attach-{proxy.name}"
					class="attach-select"
					value={attachPick[proxy.name] ?? ''}
					onchange={(e) => (attachPick[proxy.name] = e.currentTarget.value)}
				>
					<option value="" disabled>Choose a server…</option>
					{#each candidates as name (name)}
						<option value={name}>{name}</option>
					{/each}
				</select>
				<button
					class="btn-action btn-attach"
					type="button"
					disabled={!attachPick[proxy.name] || backendBusy[proxy.name] != null}
					onclick={() => handleAttach(proxy.name)}
				>
					{backendBusy[proxy.name] ? 'Attaching…' : 'Attach'}
				</button>
			</div>
		{/if}
	{/snippet}

		{#each proxies as proxy (proxy.name)}
			{@const overview = overviewFor(proxy)}
			<section class="card">
				<div class="proxy-head">
					<div class="proxy-title">
						<h2><a href="/proxies/{proxy.name}">{proxy.name}</a></h2>
						<StatusBadge
							variant={isRunning(proxy) ? 'success' : 'error'}
							size="sm"
							pulse={isRunning(proxy)}
						>
							{isRunning(proxy) ? 'Running' : 'Stopped'}
						</StatusBadge>
					</div>
					<div class="proxy-actions">
						<a class="edit-link" href="/proxies/{proxy.name}">Manage</a>
						{#if isRunning(proxy)}
							<button
								class="btn-action"
								type="button"
								disabled={actionLoading[proxy.name]}
								onclick={() => handleAction(proxy.name, 'restart')}
							>
								{actionLoading[proxy.name] ? '…' : 'Restart'}
							</button>
							<button
								class="btn-action btn-stop"
								type="button"
								disabled={actionLoading[proxy.name]}
								onclick={() => handleAction(proxy.name, 'stop')}
							>
								Stop
							</button>
							<button
								class="btn-action btn-stop"
								type="button"
								disabled={actionLoading[proxy.name]}
								onclick={() => handleAction(proxy.name, 'kill')}
								title="Force-kill the proxy process"
							>
								Kill
							</button>
						{:else}
							<button
								class="btn-action btn-start"
								type="button"
								disabled={actionLoading[proxy.name]}
								onclick={() => handleAction(proxy.name, 'start')}
							>
								{actionLoading[proxy.name] ? '…' : 'Start'}
							</button>
						{/if}
						<button
							class="btn-action btn-delete"
							type="button"
							disabled={actionLoading[proxy.name]}
							onclick={() => handleDelete(proxy.name)}
						>
							Delete
						</button>
					</div>
				</div>

				<div class="proxy-meta">
					{#if proxy.summary?.port}
						<span class="meta-item">
							<span class="meta-label">Address</span>
							<span class="meta-value address">{hostname}:{proxy.summary.port}</span>
							<CopyButton value="{hostname}:{proxy.summary.port}" />
						</span>
					{/if}
					{#if proxy.summary?.playersOnline != null}
						<span class="meta-item">
							<span class="meta-label">Players</span>
							<span class="meta-value">{proxy.summary.playersOnline} / {proxy.summary.playersMax ?? '?'}</span>
						</span>
					{/if}
					{#if proxy.summary?.memoryBytes}
						<span class="meta-item">
							<span class="meta-label">Memory</span>
							<span class="meta-value">{formatBytes(proxy.summary.memoryBytes)}</span>
						</span>
					{/if}
				</div>

				{#if actionError[proxy.name]}
					<div class="fetch-error">{actionError[proxy.name]}</div>
				{/if}

				{#if overview.error}
					<div class="fetch-error">
						Couldn't load backend info: {overview.error}
					</div>
				{:else if overview.summary && overview.summary.backends.length === 0}
					<p class="no-backends">
						No backends configured yet — players joining this proxy have nowhere to go. Attach a
						game server below, or pick one in the create-server wizard.
					</p>
					{@render attachRow(proxy, 'Attach a server:')}
				{:else}
					<ProxyBackendRollup
						summary={overview.summary}
						busyBackend={backendBusy[proxy.name] ?? null}
						onremediate={(backend) => handleRemediate(proxy.name, backend)}
						ondetach={(backend) => handleDetach(proxy.name, backend)}
					/>
					{@render attachRow(proxy, 'Attach another server:')}
				{/if}
			</section>
		{/each}
	{/if}
</div>

<style>
	.page {
		display: flex;
		flex-direction: column;
		gap: 20px;
	}

	.header {
		display: flex;
		justify-content: space-between;
		align-items: flex-start;
		gap: 16px;
		flex-wrap: wrap;
	}

	.header h1 {
		margin: 0 0 6px;
		font-size: 28px;
		font-weight: 700;
		letter-spacing: -0.02em;
	}

	.subtitle {
		margin: 0;
		color: #8890b1;
		font-size: 14px;
		max-width: 640px;
	}

	.btn-setup {
		font-size: 13px;
		font-weight: 600;
		color: #ffffff;
		text-decoration: none;
		padding: 9px 16px;
		border-radius: 8px;
		background: var(--mc-grass, #6ab04c);
		white-space: nowrap;
	}

	.btn-setup:hover {
		filter: brightness(1.1);
	}

	.empty-state {
		padding: 32px 28px;
		background: var(--mc-panel, #1a1e2f);
		border: 1px solid var(--border-color, #2a2f47);
		border-radius: 16px;
		color: #c4cff5;
		display: flex;
		flex-direction: column;
		align-items: flex-start;
		gap: 4px;
	}

	.empty-state p {
		margin: 0 0 8px;
		max-width: 640px;
	}

	.card {
		background: var(--mc-panel, rgba(22, 27, 46, 0.95));
		border: 1px solid var(--border-color, #2a2f47);
		border-radius: 16px;
		padding: 20px 24px;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.35);
	}

	.proxy-head {
		display: flex;
		justify-content: space-between;
		align-items: center;
		gap: 12px;
		flex-wrap: wrap;
	}

	.proxy-title {
		display: flex;
		align-items: center;
		gap: 12px;
	}

	.proxy-title h2 {
		margin: 0;
		font-size: 20px;
		font-weight: 600;
	}

	.proxy-title a {
		color: #eef0f8;
		/* #176 rendered this with no affordance at all, so the only route to a
		   proxy's console and files looked like plain text. */
		text-decoration: underline;
		text-decoration-color: rgba(238, 240, 248, 0.35);
		text-underline-offset: 4px;
	}

	.proxy-title a:hover {
		color: var(--mc-grass, #6ab04c);
	}

	.proxy-actions {
		display: flex;
		align-items: center;
		gap: 8px;
		flex-wrap: wrap;
	}

	.btn-action {
		padding: 7px 14px;
		font-size: 13px;
		font-weight: 600;
		font-family: inherit;
		border-radius: 8px;
		border: 1px solid var(--border-color, #2a2f47);
		background: var(--mc-panel-light, #2a2f47);
		color: #c4cff5;
		cursor: pointer;
	}

	.btn-action:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.btn-start {
		background: rgba(106, 176, 76, 0.15);
		border-color: rgba(106, 176, 76, 0.4);
		color: var(--mc-grass, #6ab04c);
	}

	.btn-stop {
		background: rgba(210, 94, 72, 0.12);
		border-color: rgba(210, 94, 72, 0.35);
		color: #ffb6a6;
	}

	.btn-delete {
		border-color: rgba(255, 107, 107, 0.35);
		background: rgba(255, 107, 107, 0.12);
		color: #ff8f8f;
	}

	.edit-link {
		font-size: 13px;
		font-weight: 600;
		color: #608dff;
		text-decoration: none;
		padding: 7px 12px;
		border: 1px solid rgba(96, 141, 255, 0.3);
		border-radius: 8px;
		background: rgba(96, 141, 255, 0.12);
	}

	.edit-link:hover {
		background: rgba(96, 141, 255, 0.22);
	}

	.proxy-meta {
		display: flex;
		align-items: center;
		gap: 22px;
		flex-wrap: wrap;
		margin: 14px 0 4px;
	}

	.meta-item {
		display: inline-flex;
		align-items: center;
		gap: 8px;
	}

	.meta-label {
		font-size: 11px;
		font-weight: 600;
		text-transform: uppercase;
		letter-spacing: 0.08em;
		color: #8e96bb;
	}

	.meta-value {
		font-size: 13px;
		color: #eef0f8;
	}

	.address {
		font-family: var(--font-mono, monospace);
	}

	.fetch-error {
		margin-top: 12px;
		padding: 10px 14px;
		font-size: 14px;
		color: #ffb6a6;
		background: rgba(210, 94, 72, 0.12);
		border: 1px solid rgba(210, 94, 72, 0.35);
		border-radius: 8px;
	}

	.no-backends {
		margin: 12px 0 0;
		font-size: 14px;
		color: #8890b1;
		font-style: italic;
	}

	.attach-row {
		display: flex;
		align-items: center;
		gap: 10px;
		margin-top: 12px;
		flex-wrap: wrap;
	}

	.attach-label {
		font-size: 13px;
		font-weight: 600;
		color: #8890b1;
	}

	.attach-select {
		padding: 7px 10px;
		font-size: 13px;
		font-family: inherit;
		border-radius: 8px;
		border: 1px solid var(--border-color, #2a2f47);
		background: var(--mc-panel-light, #2a2f47);
		color: #eef0f8;
	}

	.btn-attach {
		background: rgba(106, 176, 76, 0.15);
		border-color: rgba(106, 176, 76, 0.4);
		color: var(--mc-grass, #6ab04c);
	}
</style>
