<script lang="ts">
	import type { ApiResult, HostMetrics, ServerSummary } from '$lib/api/types';
	import { onMount } from 'svelte';

	export let data: {
		metrics: ApiResult<HostMetrics>;
		servers: ApiResult<ServerSummary[]>;
	};

	let liveMetrics: HostMetrics | null = null;
	let liveError: string | null = null;
	let liveConnected = false;

	const formatBytes = (value: number) => {
		if (!value) return '0 B';
		const units = ['B', 'KB', 'MB', 'GB', 'TB'];
		const i = Math.floor(Math.log(value) / Math.log(1024));
		return `${(value / Math.pow(1024, i)).toFixed(1)} ${units[i]}`;
	};

	const formatUptime = (seconds: number) => {
		const hours = Math.floor(seconds / 3600);
		const minutes = Math.floor((seconds % 3600) / 60);
		return `${hours}h ${minutes}m`;
	};

	const parseSse = async (response: Response, onData: (payload: string) => void) => {
		const reader = response.body?.getReader();
		if (!reader) return;
		const decoder = new TextDecoder();
		let buffer = '';

		while (true) {
			const { done, value } = await reader.read();
			if (done) break;
			buffer += decoder.decode(value, { stream: true });

			let idx = buffer.indexOf('\n\n');
			while (idx >= 0) {
				const chunk = buffer.slice(0, idx).trim();
				buffer = buffer.slice(idx + 2);
				for (const line of chunk.split('\n')) {
					if (line.startsWith('data:')) {
						onData(line.slice(5).trim());
					}
				}
				idx = buffer.indexOf('\n\n');
			}
		}
	};

	onMount(() => {
		const controller = new AbortController();
		const start = async () => {
			try {
				const response = await fetch(`/api/host/metrics/stream?intervalMs=2000`, {
					signal: controller.signal
				});

				if (!response.ok) {
					liveError = `Live stream failed: ${response.status}`;
					return;
				}

				liveConnected = true;
				await parseSse(response, (payload) => {
					try {
						liveMetrics = JSON.parse(payload) as HostMetrics;
					} catch {
						liveError = 'Live stream parse error';
					}
				});
			} catch (err) {
				if (!(err instanceof DOMException && err.name === 'AbortError')) {
					liveError = err instanceof Error ? err.message : 'Live stream error';
				}
			}
		};

		start();
		return () => controller.abort();
	});
</script>

<svelte:head>
	<link rel="preconnect" href="https://fonts.googleapis.com" />
	<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin />
	<link
		href="https://fonts.googleapis.com/css2?family=Space+Grotesk:wght@400;500;600&display=swap"
		rel="stylesheet"
	/>
</svelte:head>

<main class="page">
	<header class="header">
		<div>
			<p class="eyebrow">MineOS Rewrite</p>
			<h1>Host Overview</h1>
			<p class="subtle">Read-only snapshot from the new .NET API.</p>
		</div>
		<div class="pill">API v1</div>
	</header>

	<section class="grid">
		<div class="card">
			<h2>Uptime</h2>
			{#if data.metrics.data}
				<p class="metric">{formatUptime(data.metrics.data.uptimeSeconds)}</p>
			{:else}
				<p class="metric muted">n/a</p>
			{/if}
		</div>
		<div class="card">
			<h2>Free Memory</h2>
			{#if data.metrics.data}
				<p class="metric">{formatBytes(data.metrics.data.freeMemBytes)}</p>
			{:else}
				<p class="metric muted">n/a</p>
			{/if}
		</div>
		<div class="card">
			<h2>Disk Free</h2>
			{#if data.metrics.data}
				<p class="metric">{formatBytes(data.metrics.data.disk.freeBytes)}</p>
			{:else}
				<p class="metric muted">n/a</p>
			{/if}
		</div>
		<div class="card">
			<h2>Load Avg</h2>
			{#if data.metrics.data}
				<p class="metric">{data.metrics.data.loadAvg.join(' / ')}</p>
			{:else}
				<p class="metric muted">n/a</p>
			{/if}
		</div>
		<div class="card live-card">
			<h2>Live Metrics</h2>
			{#if liveMetrics}
				<p class="metric">{liveMetrics.loadAvg.join(' / ')}</p>
				<p class="subtext">
					{formatBytes(liveMetrics.freeMemBytes)} free · {formatBytes(liveMetrics.disk.freeBytes)} disk
				</p>
			{:else if liveError}
				<p class="metric muted">{liveError}</p>
			{:else if liveConnected}
				<p class="metric muted">Waiting for data...</p>
			{:else}
				<p class="metric muted">Connecting…</p>
			{/if}
		</div>
	</section>

	<section class="panel">
		<div class="panel-header">
			<h2>Servers</h2>
			{#if data.servers.error}
				<span class="status error">{data.servers.error}</span>
			{:else}
				<span class="status">Read-only</span>
			{/if}
		</div>
		{#if data.servers.data && data.servers.data.length > 0}
			<table>
				<thead>
					<tr>
						<th>Name</th>
						<th>Status</th>
						<th>Profile</th>
						<th>Port</th>
						<th>Players</th>
					</tr>
				</thead>
				<tbody>
					{#each data.servers.data as server}
						<tr>
							<td>{server.name}</td>
							<td>
								<span class:up={server.up} class:down={!server.up}>
									{server.up ? 'Up' : 'Down'}
								</span>
							</td>
							<td>{server.profile ?? '—'}</td>
							<td>{server.port ?? '—'}</td>
							<td>
								{server.playersOnline ?? 0} / {server.playersMax ?? '—'}
							</td>
						</tr>
					{/each}
				</tbody>
			</table>
		{:else}
			<p class="empty">No servers yet. The API currently returns an empty list.</p>
		{/if}
	</section>
</main>

<style>
	:global(body) {
		margin: 0;
		font-family: 'Space Grotesk', system-ui, sans-serif;
		background: radial-gradient(circle at top, #1f2538 0%, #0f1118 55%, #0b0c12 100%);
		color: #eef0f8;
	}

	.page {
		max-width: 1100px;
		margin: 0 auto;
		padding: 48px 24px 72px;
	}

	.header {
		display: flex;
		justify-content: space-between;
		align-items: center;
		gap: 24px;
		margin-bottom: 32px;
	}

	.eyebrow {
		text-transform: uppercase;
		letter-spacing: 0.2em;
		font-size: 12px;
		color: #7c86a6;
		margin: 0 0 8px;
	}

	h1 {
		margin: 0 0 8px;
		font-size: 34px;
	}

	.subtle {
		margin: 0;
		color: #aab2d3;
	}

	.pill {
		background: #2b2f45;
		border-radius: 999px;
		padding: 8px 16px;
		font-weight: 500;
		color: #d4d9f1;
	}

	.grid {
		display: grid;
		grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
		gap: 16px;
		margin-bottom: 32px;
	}

	.card {
		background: #1a1e2f;
		border-radius: 16px;
		padding: 18px 20px;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.35);
	}

	.card h2 {
		margin: 0 0 12px;
		font-size: 14px;
		font-weight: 500;
		color: #9aa2c5;
	}

	.metric {
		margin: 0;
		font-size: 22px;
		font-weight: 600;
	}

	.metric.muted {
		color: #717aa3;
	}

	.subtext {
		margin: 8px 0 0;
		color: #98a2c9;
		font-size: 13px;
	}

	.panel {
		background: #141827;
		border-radius: 18px;
		padding: 20px;
		box-shadow: 0 24px 50px rgba(0, 0, 0, 0.35);
	}

	.panel-header {
		display: flex;
		justify-content: space-between;
		align-items: center;
		margin-bottom: 16px;
	}

	.status {
		font-size: 12px;
		color: #98a2c9;
	}

	.status.error {
		color: #ff9f9f;
	}

	table {
		width: 100%;
		border-collapse: collapse;
	}

	th,
	td {
		padding: 12px 8px;
		text-align: left;
		border-bottom: 1px solid #2a2f47;
	}

	th {
		font-size: 12px;
		text-transform: uppercase;
		letter-spacing: 0.12em;
		color: #8890b1;
	}

	.empty {
		margin: 0;
		color: #8e96bb;
	}

	.up {
		color: #7ae68d;
	}

	.down {
		color: #ff9f9f;
	}
	@media (max-width: 720px) {
		.header {
			flex-direction: column;
			align-items: flex-start;
		}
	}
</style>
