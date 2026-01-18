<script lang="ts">
	import { page } from '$app/stores';
	import StatusBadge from '$lib/components/StatusBadge.svelte';
	import type { LayoutData } from './$types';

	let { data, children }: { data: LayoutData; children: any } = $props();

	const tabs = [
		{ href: `/servers/${data.server?.name}`, label: 'Dashboard', exact: true },
		{ href: `/servers/${data.server?.name}/config`, label: 'Properties' },
		{ href: `/servers/${data.server?.name}/advanced`, label: 'Config' },
		{ href: `/servers/${data.server?.name}/backups`, label: 'Backups' },
		{ href: `/servers/${data.server?.name}/archives`, label: 'Archives' },
		{ href: `/servers/${data.server?.name}/files`, label: 'Files' },
		{ href: `/servers/${data.server?.name}/performance`, label: 'Performance' },
		{ href: `/servers/${data.server?.name}/worlds`, label: 'Worlds' },
		{ href: `/servers/${data.server?.name}/players`, label: 'Players' },
		{ href: `/servers/${data.server?.name}/mods`, label: 'Mods' },
		{ href: `/servers/${data.server?.name}/cron`, label: 'Cron Jobs' }
	];

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

	const statusMeta = normalizeStatus(data.server?.status);
</script>

<div class="server-container">
	<div class="server-header">
		<div>
			<a href="/servers" class="breadcrumb">&lt; Back to Servers</a>
			<h1>{data.server?.name}</h1>
			<div class="server-meta">
				<StatusBadge variant={statusMeta.running ? 'success' : 'warning'} size="lg">
					{statusMeta.label}
				</StatusBadge>
				{#if data.server?.javaPid}
					<span class="meta-item">PID: {data.server.javaPid}</span>
				{/if}
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
		display: flex;
		justify-content: space-between;
		align-items: flex-start;
		gap: 24px;
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
		.tabs {
			overflow-x: scroll;
		}
	}
</style>
