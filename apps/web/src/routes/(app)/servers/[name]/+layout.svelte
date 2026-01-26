<script lang="ts">
	import { onMount } from 'svelte';
	import { page } from '$app/stores';
	import StatusBadge from '$lib/components/StatusBadge.svelte';
	import ServerQuickActions from '$lib/components/ServerQuickActions.svelte';
	import ServerIconUploader from '$lib/components/ServerIconUploader.svelte';
	import type { LayoutData } from './$types';
	import { withBase } from '$lib/utils/paths';

	let { data, children }: { data: LayoutData; children: any } = $props();
	let server = $state(data.server);
	let playerInfo = $state<{ online: number | null; max: number | null; version: string | null }>({
		online: null,
		max: null,
		version: null
	});

	const tabs = $derived.by(() => [
		{ href: withBase(`/servers/${server?.name}`), label: 'Dashboard', exact: true },
		{ href: withBase(`/servers/${server?.name}/config`), label: 'Properties' },
		{ href: withBase(`/servers/${server?.name}/advanced`), label: 'Config' },
		{ href: withBase(`/servers/${server?.name}/backups`), label: 'Backups' },
		{ href: withBase(`/servers/${server?.name}/archives`), label: 'Archives' },
		{ href: withBase(`/servers/${server?.name}/files`), label: 'Files' },
		{ href: withBase(`/servers/${server?.name}/performance`), label: 'Performance' },
		{ href: withBase(`/servers/${server?.name}/worlds`), label: 'Worlds' },
		{ href: withBase(`/servers/${server?.name}/players`), label: 'Players' },
		{ href: withBase(`/servers/${server?.name}/mods`), label: 'Mods' },
		{ href: withBase(`/servers/${server?.name}/plugins`), label: 'Plugins' },
		{ href: withBase(`/servers/${server?.name}/cron`), label: 'Cron Jobs' }
	]);

	function isActiveTab(href: string, exact = false) {
		if (exact) {
			return $page.url.pathname === href;
		}
		return $page.url.pathname.startsWith(href);
	}

	function normalizeStatus(status?: string) {
		if (!status) return { label: 'Unknown', running: false };
		const value = status.toLowerCase();
		if (value === 'running' || value === 'up') return { label: 'Running', running: true };
		if (value === 'stopped' || value === 'down') return { label: 'Stopped', running: false };
		return { label: status, running: false };
	}

	const statusMeta = $derived(normalizeStatus(server?.status));

	$effect(() => {
		server = data.server;
		playerInfo = { online: null, max: null, version: null };
	});

	let statusSource: EventSource | null = null;

	function scheduleBurstRefresh() {
		statusSource?.close();
		connectStatusStream();
	}

	onMount(() => {
		let cancelled = false;
		connectStatusStream();

		return () => {
			cancelled = true;
			statusSource?.close();
			statusSource = null;
		};
	});

	function connectStatusStream() {
		if (!server?.name) return;
		statusSource?.close();
		statusSource = new EventSource(
			`/api/servers/${encodeURIComponent(server.name)}/heartbeat/stream`
		);
		statusSource.onmessage = (event) => {
			try {
				const heartbeat = JSON.parse(event.data);
				if (server) {
					server = {
						...server,
						status: heartbeat.status,
						javaPid: heartbeat.javaPid,
						screenPid: heartbeat.screenPid
					};
				}
				playerInfo = {
					online: heartbeat?.ping?.playersOnline ?? null,
					max: heartbeat?.ping?.playersMax ?? null,
					version: heartbeat?.ping?.serverVersion ?? null
				};
			} catch (err) {
				console.error('Failed to parse heartbeat:', err);
			}
		};
	statusSource.onerror = () => {
		statusSource?.close();
		statusSource = null;
		setTimeout(connectStatusStream, 2000);
	};
}
</script>

<div class="server-container">
	<div class="server-header">
		<div class="server-info">
			<a href={withBase('/servers')} class="breadcrumb">&lt; Back to Servers</a>
			<div class="title-row">
				<h1>{server?.name}</h1>
				<StatusBadge variant={statusMeta.running ? 'success' : 'warning'} size="lg">
					{statusMeta.label}
				</StatusBadge>
			</div>
			<div class="server-meta">
				<div class="meta-chip players">
					<span class="chip-label">Players</span>
					<span class="chip-value">{playerInfo.online ?? '--'}</span>
					<span class="chip-sep">/</span>
					<span class="chip-value muted">{playerInfo.max ?? '--'}</span>
				</div>
				{#if playerInfo.version}
					<div class="meta-chip">
						<span class="chip-label">Version</span>
						<span class="chip-value">{playerInfo.version}</span>
					</div>
				{/if}
				{#if server?.javaPid}
					<div class="meta-chip">
						<span class="chip-label">PID</span>
						<span class="chip-value">{server.javaPid}</span>
					</div>
				{/if}
			</div>
		</div>
		<div class="server-side">
			<div class="server-icon">
				{#if server?.name}
					<ServerIconUploader serverName={server.name} />
				{/if}
			</div>
			<div class="server-actions">
				<ServerQuickActions server={server} on:refresh={scheduleBurstRefresh} />
			</div>
		</div>
	</div>

	<nav class="tabs">
		{#each tabs as tab}
			<a href={tab.href} class="tab" class:active={isActiveTab(tab.href, tab.exact)}>
				{tab.label}
			</a>
		{/each}
	</nav>

	<div class="content">
		{@render children()}
	</div>
</div>

<style>
	.server-container {
		display: flex;
		flex-direction: column;
		gap: 24px;
	}

	.server-header {
		position: relative;
		display: flex;
		justify-content: space-between;
		align-items: center;
		gap: 24px;
		padding: 24px 28px;
		background: linear-gradient(135deg, rgba(22, 27, 46, 0.95), rgba(10, 14, 24, 0.95));
		border: 1px solid rgba(42, 47, 71, 0.8);
		border-radius: 18px;
		box-shadow: 0 24px 40px rgba(0, 0, 0, 0.35);
		overflow: hidden;
	}

	.server-info {
		min-width: 240px;
		display: flex;
		flex-direction: column;
		gap: 12px;
		position: relative;
		z-index: 1;
	}

	.server-icon {
		display: flex;
		align-items: center;
	}

	.server-side {
		display: flex;
		align-items: center;
		gap: 18px;
		margin-left: auto;
		position: relative;
		z-index: 1;
	}

	.breadcrumb {
		display: inline-block;
		color: #8890b1;
		text-decoration: none;
		font-size: 14px;
		margin-bottom: 12px;
		transition: color 0.2s;
	}

	.breadcrumb:hover {
		color: #aab2d3;
	}

	h1 {
		margin: 0;
		font-size: 34px;
		font-weight: 700;
		letter-spacing: -0.02em;
	}

	.title-row {
		display: flex;
		align-items: center;
		gap: 14px;
		flex-wrap: wrap;
	}

	.server-meta {
		display: flex;
		align-items: center;
		gap: 12px;
		flex-wrap: wrap;
	}

	.meta-chip {
		display: inline-flex;
		align-items: center;
		gap: 8px;
		padding: 6px 12px;
		border-radius: 999px;
		background: rgba(19, 24, 40, 0.8);
		border: 1px solid rgba(62, 69, 100, 0.6);
		font-size: 12px;
		font-weight: 600;
		color: #cdd3ee;
	}

	.meta-chip.players {
		background: rgba(106, 176, 76, 0.18);
		border-color: rgba(106, 176, 76, 0.45);
		color: #d1f4c3;
	}

	.chip-label {
		color: #9aa6d1;
		text-transform: uppercase;
		letter-spacing: 0.06em;
		font-size: 10px;
	}

	.chip-value {
		color: #eef0f8;
		font-size: 14px;
	}

	.chip-value.muted {
		color: #c0c6e4;
	}

	.chip-sep {
		color: rgba(238, 240, 248, 0.6);
	}

	.server-header::before {
		content: '';
		position: absolute;
		inset: -20% 40% 30% -20%;
		background: radial-gradient(circle at top left, rgba(106, 176, 76, 0.18), transparent 70%);
		opacity: 0.9;
		z-index: 0;
	}

	.server-header::after {
		content: '';
		position: absolute;
		inset: 20% -10% -30% 50%;
		background: radial-gradient(circle at top right, rgba(96, 141, 255, 0.18), transparent 70%);
		opacity: 0.8;
		z-index: 0;
	}

	.tabs {
		display: flex;
		gap: 4px;
		border-bottom: 1px solid #2a2f47;
		overflow-x: auto;
		scroll-snap-type: x proximity;
	}

	.tab {
		padding: 12px 20px;
		color: #8890b1;
		text-decoration: none;
		border-bottom: 2px solid transparent;
		transition: all 0.2s;
		font-size: 14px;
		font-weight: 500;
		white-space: nowrap;
		scroll-snap-align: start;
	}

	.tab:hover {
		color: #aab2d3;
	}

	.tab.active {
		color: var(--mc-grass);
		border-bottom-color: var(--mc-grass);
	}

	.content {
		flex: 1;
	}

	@media (max-width: 640px) {
		.server-header {
			flex-direction: column;
			align-items: flex-start;
		}

		.server-icon {
			width: 100%;
			justify-content: center;
		}

		.server-side {
			width: 100%;
			justify-content: space-between;
			margin-left: 0;
		}

		.tabs {
			overflow-x: scroll;
		}
	}

	@media (max-width: 900px) {
		.tabs {
			gap: 2px;
		}

		.tab {
			padding: 10px 14px;
			font-size: 13px;
		}
	}
</style>
