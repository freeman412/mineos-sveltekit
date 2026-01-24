<script lang="ts">
	import { onMount } from 'svelte';
	import { page } from '$app/stores';
	import StatusBadge from '$lib/components/StatusBadge.svelte';
	import ServerQuickActions from '$lib/components/ServerQuickActions.svelte';
	import ServerIconUploader from '$lib/components/ServerIconUploader.svelte';
	import type { LayoutData } from './$types';

	let { data, children }: { data: LayoutData; children: any } = $props();
	let server = $state(data.server);

	const tabs = $derived.by(() => [
		{ href: `/servers/${server?.name}`, label: 'Dashboard', exact: true },
		{ href: `/servers/${server?.name}/config`, label: 'Properties' },
		{ href: `/servers/${server?.name}/advanced`, label: 'Config' },
		{ href: `/servers/${server?.name}/backups`, label: 'Backups' },
		{ href: `/servers/${server?.name}/archives`, label: 'Archives' },
		{ href: `/servers/${server?.name}/files`, label: 'Files' },
		{ href: `/servers/${server?.name}/performance`, label: 'Performance' },
		{ href: `/servers/${server?.name}/worlds`, label: 'Worlds' },
		{ href: `/servers/${server?.name}/players`, label: 'Players' },
		{ href: `/servers/${server?.name}/mods`, label: 'Mods' },
		{ href: `/servers/${server?.name}/plugins`, label: 'Plugins' },
		{ href: `/servers/${server?.name}/cron`, label: 'Cron Jobs' }
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
			<a href="/servers" class="breadcrumb">&lt; Back to Servers</a>
			<h1>{server?.name}</h1>
			<div class="server-meta">
				<StatusBadge variant={statusMeta.running ? 'success' : 'warning'} size="lg">
					{statusMeta.label}
				</StatusBadge>
				{#if server?.javaPid}
					<span class="meta-item">PID: {server.javaPid}</span>
				{/if}
			</div>
		</div>
		<div class="server-icon">
			{#if server?.name}
				<ServerIconUploader serverName={server.name} />
			{/if}
		</div>
		<div class="server-actions">
			<ServerQuickActions server={server} on:refresh={scheduleBurstRefresh} />
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
		display: flex;
		justify-content: space-between;
		align-items: flex-start;
		gap: 24px;
		flex-wrap: wrap;
	}

	.server-info {
		min-width: 240px;
	}

	.server-icon {
		display: flex;
		align-items: center;
	}

	.server-actions {
		margin-left: auto;
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
		margin: 0 0 12px;
		font-size: 32px;
		font-weight: 600;
	}

	.server-meta {
		display: flex;
		align-items: center;
		gap: 16px;
	}

	.meta-item {
		font-size: 14px;
		color: #9aa2c5;
	}

	.tabs {
		display: flex;
		gap: 4px;
		border-bottom: 1px solid #2a2f47;
		overflow-x: auto;
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
		}

		.server-icon {
			width: 100%;
			justify-content: center;
		}

		.server-actions {
			width: 100%;
			margin-left: 0;
		}

		.tabs {
			overflow-x: scroll;
		}
	}
</style>
