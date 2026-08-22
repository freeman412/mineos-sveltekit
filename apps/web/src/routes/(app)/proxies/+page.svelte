<script lang="ts">
	import { onMount } from 'svelte';
	import { invalidateAll } from '$app/navigation';
	import { env } from '$env/dynamic/public';
	import { browser } from '$app/environment';
	import * as api from '$lib/api/client';
	import { formatBytes } from '$lib/utils/formatting';
	import { overlaySummaries } from '$lib/utils/proxy';
	import { createEventStream, type EventStreamHandle } from '$lib/utils/eventStream';
	import ProxyBackendRollup from '$lib/components/ProxyBackendRollup.svelte';
	import CopyButton from '$lib/components/CopyButton.svelte';
	import StatusBadge from '$lib/components/StatusBadge.svelte';
	import type { PageData } from './$types';
	import type { ServerSummary } from '$lib/api/types';

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

	async function handleAction(name: string, action: 'start' | 'stop' | 'restart') {
		if (actionLoading[name]) return;
		actionLoading[name] = true;
		actionError[name] = '';
		try {
			const result =
				action === 'start'
					? await api.startServer(fetch, name)
					: action === 'stop'
						? await api.stopServer(fetch, name)
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
				between its servers — together, that's your network. A proxy is still a server process:
				console, files, and backups stay on its server page.
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
		{#each proxies as proxy (proxy.name)}
			<section class="card">
				<div class="proxy-head">
					<div class="proxy-title">
						<h2><a href="/servers/{proxy.name}">{proxy.name}</a></h2>
						<StatusBadge
							variant={isRunning(proxy) ? 'success' : 'error'}
							size="sm"
							pulse={isRunning(proxy)}
						>
							{isRunning(proxy) ? 'Running' : 'Stopped'}
						</StatusBadge>
					</div>
					<div class="proxy-actions">
						<a class="edit-link" href="/proxies/{proxy.name}/proxy-config">Edit properties</a>
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

				{#if proxy.overview.error}
					<div class="fetch-error">
						Couldn't load backend info: {proxy.overview.error}
					</div>
				{:else if proxy.overview.summary && proxy.overview.summary.backends.length === 0}
					<p class="no-backends">
						No backends configured yet — players joining this proxy have nowhere to go. Attach a
						game server from the create-server wizard, or add one in its properties.
					</p>
				{:else}
					<ProxyBackendRollup summary={proxy.overview.summary} />
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
		text-decoration: none;
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
</style>
