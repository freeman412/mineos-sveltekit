<script lang="ts">
	import ProxyBackendRollup from '$lib/components/ProxyBackendRollup.svelte';
	import StatusBadge from '$lib/components/StatusBadge.svelte';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	function isRunning(status: string | undefined): boolean {
		const value = (status ?? '').toLowerCase();
		return value === 'running' || value === 'up';
	}
</script>

<svelte:head>
	<title>Networking | MineOS</title>
</svelte:head>

<div class="page">
	<header class="header">
		<div>
			<h1>Networking</h1>
			<p class="subtitle">
				Your proxy servers and the backends they route players to. Editing a proxy's configuration
				still happens on its own server page.
			</p>
		</div>
	</header>

	{#if data.proxies.length === 0}
		<div class="empty-state">
			<p><strong>No proxy servers yet.</strong></p>
			<p>
				A proxy (Velocity, BungeeCord, Waterfall) routes players between your game servers. Create
				one from <a href="/servers#new">Servers</a> and it will show up here.
			</p>
		</div>
	{:else}
		{#each data.proxies as proxy (proxy.name)}
			<section class="card">
				<div class="proxy-head">
					<div class="proxy-title">
						<h2><a href="/servers/{proxy.name}">{proxy.name}</a></h2>
						<StatusBadge
							variant={isRunning(proxy.status) ? 'success' : 'warning'}
							size="sm"
							pulse={isRunning(proxy.status)}
						>
							{isRunning(proxy.status) ? 'Running' : 'Stopped'}
						</StatusBadge>
					</div>
					<a class="edit-link" href="/servers/{proxy.name}/proxy-config">Edit properties</a>
				</div>

				{#if proxy.overview.error}
					<div class="fetch-error">
						Couldn't load backend info: {proxy.overview.error}
					</div>
				{:else if proxy.overview.summary && proxy.overview.summary.backends.length === 0}
					<p class="no-backends">
						No backends configured yet — players joining this proxy have nowhere to go. Add
						backend servers in its properties.
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

	.empty-state {
		padding: 32px 28px;
		background: var(--mc-panel, #1a1e2f);
		border: 1px solid var(--border-color, #2a2f47);
		border-radius: 16px;
		color: #c4cff5;
	}

	.empty-state p {
		margin: 0 0 8px;
	}

	.empty-state a {
		color: var(--mc-grass, #6ab04c);
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
		margin-bottom: 14px;
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

	.edit-link {
		font-size: 13px;
		font-weight: 600;
		color: #608dff;
		text-decoration: none;
		padding: 6px 12px;
		border: 1px solid rgba(96, 141, 255, 0.3);
		border-radius: 6px;
		background: rgba(96, 141, 255, 0.12);
	}

	.edit-link:hover {
		background: rgba(96, 141, 255, 0.22);
	}

	.fetch-error {
		padding: 10px 14px;
		font-size: 14px;
		color: #ffb6a6;
		background: rgba(210, 94, 72, 0.12);
		border: 1px solid rgba(210, 94, 72, 0.35);
		border-radius: 8px;
	}

	.no-backends {
		margin: 0;
		font-size: 14px;
		color: #8890b1;
		font-style: italic;
	}
</style>
