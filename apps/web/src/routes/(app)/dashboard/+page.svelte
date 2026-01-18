<script lang="ts">
	import { onMount } from 'svelte';
	import { goto, invalidateAll } from '$app/navigation';
	import * as api from '$lib/api/client';
	import { modal } from '$lib/stores/modal';
	import type { PageData } from './$types';
	import type { HostMetrics, ServerSummary } from '$lib/api/types';

	let { data }: { data: PageData } = $props();

	let servers = $state<ServerSummary[]>(data.servers.data ?? []);
	let serversError = $state<string | null>(data.servers.error);
	let hostMetrics = $state<HostMetrics | null>(data.hostMetrics.data ?? null);
	let hostMetricsError = $state<string | null>(data.hostMetrics.error);
	let serversStream: EventSource | null = null;
	let metricsStream: EventSource | null = null;
	let memoryHistory = $state<Record<string, number[]>>({});
	let actionLoading = $state<Record<string, boolean>>({});

	const maxMemoryPoints = 30;

	const formatBytes = (bytes: number): string => {
		if (bytes === 0) return '0 B';
		const k = 1024;
		const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
		const i = Math.floor(Math.log(bytes) / Math.log(k));
		return `${(bytes / Math.pow(k, i)).toFixed(1)} ${sizes[i]}`;
	};

	const formatUptime = (seconds: number): string => {
		const days = Math.floor(seconds / 86400);
		const hours = Math.floor((seconds % 86400) / 3600);
		const minutes = Math.floor((seconds % 3600) / 60);

		if (days > 0) return `${days}d ${hours}h`;
		if (hours > 0) return `${hours}h ${minutes}m`;
		return `${minutes}m`;
	};

	const totalServers = $derived(servers.length ?? 0);
	const runningServers = $derived(
		servers.filter((s) => s.up).length ?? 0
	);
	const totalPlayers = $derived(
		servers.reduce((sum, s) => sum + (s.playersOnline ?? 0), 0) ?? 0
	);
	const maxPlayers = $derived(
		servers.reduce((sum, s) => sum + (s.playersMax ?? 0), 0) ?? 0
	);
	const totalServerMemory = $derived(
		servers.reduce((sum, s) => sum + (s.memoryBytes ?? 0), 0) ?? 0
	);

	function buildSparkline(values: number[], width = 120, height = 28) {
		if (!values || values.length < 2) return '';
		const min = Math.min(...values);
		const max = Math.max(...values);
		const range = max - min || 1;
		return values
			.map((value, idx) => {
				const x = (idx / (values.length - 1)) * width;
				const y = height - ((value - min) / range) * height;
				return `${x},${y}`;
			})
			.join(' ');
	}

	function updateMemoryHistory(nextServers: ServerSummary[]) {
		const updated = { ...memoryHistory };
		for (const server of nextServers) {
			if (server.memoryBytes == null) continue;
			const history = updated[server.name] ? [...updated[server.name]] : [];
			history.push(server.memoryBytes);
			if (history.length > maxMemoryPoints) {
				history.shift();
			}
			updated[server.name] = history;
		}
		memoryHistory = updated;
	}

	function openServer(serverName: string) {
		goto(`/servers/${encodeURIComponent(serverName)}`);
	}

	function handleServerKeydown(event: KeyboardEvent, serverName: string) {
		if (event.key === 'Enter' || event.key === ' ') {
			event.preventDefault();
			openServer(serverName);
		}
	}

	async function handleAction(
		serverName: string,
		action: 'start' | 'stop' | 'kill',
		event?: Event
	) {
		event?.stopPropagation();
		event?.preventDefault();

		actionLoading[serverName] = true;
		try {
			let result;
			switch (action) {
				case 'start':
					result = await api.startServer(fetch, serverName);
					break;
				case 'stop':
					result = await api.stopServer(fetch, serverName);
					break;
				case 'kill':
					result = await api.killServer(fetch, serverName);
					break;
			}

			if (result?.error) {
				await modal.error(`Failed to ${action} server: ${result.error}`);
			} else {
				setTimeout(() => invalidateAll(), 1000);
			}
		} finally {
			delete actionLoading[serverName];
			actionLoading = { ...actionLoading };
		}
	}

	onMount(() => {
		updateMemoryHistory(servers);
		serversStream = new EventSource('/api/host/servers/stream');
		serversStream.onmessage = (event) => {
			try {
				const nextServers = JSON.parse(event.data) as ServerSummary[];
				servers = nextServers;
				updateMemoryHistory(nextServers);
				serversError = null;
			} catch (err) {
				console.error('Failed to parse servers stream:', err);
			}
		};
		serversStream.onerror = () => {
			serversStream?.close();
			serversStream = null;
		};

		metricsStream = new EventSource('/api/host/metrics/stream');
		metricsStream.onmessage = (event) => {
			try {
				hostMetrics = JSON.parse(event.data) as HostMetrics;
				hostMetricsError = null;
			} catch (err) {
				console.error('Failed to parse metrics stream:', err);
			}
		};
		metricsStream.onerror = () => {
			metricsStream?.close();
			metricsStream = null;
		};

		return () => {
			serversStream?.close();
			metricsStream?.close();
		};
	});
</script>

<div class="dashboard">
	<div class="page-header">
		<div>
			<h1>Dashboard</h1>
			<p class="subtitle">Overview of your Minecraft hosting environment</p>
		</div>
		<a href="/servers/new" class="btn-primary">+ Create Server</a>
	</div>

	<!-- Quick Stats -->
	<div class="stats-grid">
		<div class="stat-card">
			<div class="stat-icon">[S]</div>
			<div class="stat-content">
				<div class="stat-value">{totalServers}</div>
				<div class="stat-label">Total Servers</div>
			</div>
		</div>

		<div class="stat-card">
			<div class="stat-icon">[R]</div>
			<div class="stat-content">
				<div class="stat-value">{runningServers}</div>
				<div class="stat-label">Running</div>
			</div>
		</div>

		<div class="stat-card">
			<div class="stat-icon">[P]</div>
			<div class="stat-content">
				<div class="stat-value">{totalPlayers} / {maxPlayers}</div>
				<div class="stat-label">Players Online</div>
			</div>
		</div>

		<div class="stat-card">
			<div class="stat-icon">[M]</div>
			<div class="stat-content">
				<div class="stat-value">{formatBytes(totalServerMemory)}</div>
				<div class="stat-label">Server Memory</div>
			</div>
		</div>

		{#if hostMetrics}
			<div class="stat-card">
				<div class="stat-icon">[D]</div>
				<div class="stat-content">
					<div class="stat-value">
						{formatBytes(hostMetrics.disk.freeBytes)}
					</div>
					<div class="stat-label">Disk Free</div>
				</div>
			</div>
		{/if}
	</div>
	<!-- Host Metrics -->
	{#if hostMetrics}
		<div class="metrics-section">
			<h2>Host Metrics</h2>
			<div class="metrics-grid">
				<div class="metric-card">
					<div class="metric-header">
						<span class="metric-title">System Uptime</span>
					</div>
					<div class="metric-value">
						{formatUptime(hostMetrics.uptimeSeconds)}
					</div>
				</div>

				<div class="metric-card">
					<div class="metric-header">
						<span class="metric-title">Load Average</span>
					</div>
					<div class="metric-value">
						{hostMetrics.loadAvg
							.slice(0, 3)
							.map((v) => v.toFixed(2))
							.join(', ')}
					</div>
				</div>

				<div class="metric-card">
					<div class="metric-header">
						<span class="metric-title">Free Memory</span>
					</div>
					<div class="metric-value">
						{formatBytes(hostMetrics.freeMemBytes)}
					</div>
				</div>

				<div class="metric-card">
					<div class="metric-header">
						<span class="metric-title">Disk Usage</span>
					</div>
					<div class="metric-value">
						{((
							(hostMetrics.disk.totalBytes -
								hostMetrics.disk.freeBytes) /
							hostMetrics.disk.totalBytes
						) * 100).toFixed(1)}%
					</div>
					<div class="metric-subtext">
						{formatBytes(
							hostMetrics.disk.totalBytes - hostMetrics.disk.freeBytes
						)} / {formatBytes(hostMetrics.disk.totalBytes)}
					</div>
				</div>
			</div>
		</div>
	{/if}

	<!-- Recent Servers -->
	<div class="servers-section">
		<div class="section-header">
			<h2>Servers</h2>
			<a href="/servers" class="link-btn">View all -></a>
		</div>

		{#if serversError}
			<div class="error-box">
				<p>Failed to load servers: {serversError}</p>
			</div>
		{:else if servers && servers.length > 0}
			<div class="server-list">
				{#each servers.slice(0, 6) as server}
					<div
						class="server-item"
						role="link"
						tabindex="0"
						onclick={() => openServer(server.name)}
						onkeydown={(event) => handleServerKeydown(event, server.name)}
					>
						<div class="server-info">
							<div class="server-name">{server.name}</div>
						<div class="server-meta">
							{#if server.profile}
								<span class="profile-badge">{server.profile}</span>
							{/if}
							{#if server.port}
								<span class="port-badge">:{server.port}</span>
							{/if}
							{#if server.needsRestart}
								<span class="restart-badge">Restart required</span>
							{/if}
						</div>
					</div>
						<div class="server-status">
							{#if server.up}
								<span class="status-indicator status-up"></span>
								<span class="status-text">Running</span>
								{#if server.playersOnline !== null && server.playersMax !== null}
									<span class="players-count"
										>{server.playersOnline}/{server.playersMax}</span
									>
								{/if}
								{#if server.memoryBytes !== null && server.memoryBytes !== undefined}
									<span class="memory-count">{formatBytes(server.memoryBytes)}</span>
									{#if memoryHistory[server.name]?.length > 1}
										<svg class="mini-sparkline" viewBox="0 0 120 28" preserveAspectRatio="none">
											<polyline
												points={buildSparkline(memoryHistory[server.name])}
												fill="none"
												stroke="rgba(106, 176, 76, 0.8)"
												stroke-width="2"
												stroke-linecap="round"
												stroke-linejoin="round"
											/>
										</svg>
									{/if}
								{/if}
							{:else}
								<span class="status-indicator status-down"></span>
								<span class="status-text">Stopped</span>
							{/if}
							<div class="server-actions">
								{#if server.up}
									<button
										class="server-action-btn"
										onclick={(event) => handleAction(server.name, 'stop', event)}
										disabled={actionLoading[server.name]}
									>
										Stop
									</button>
									<button
										class="server-action-btn danger"
										onclick={(event) => handleAction(server.name, 'kill', event)}
										disabled={actionLoading[server.name]}
									>
										Kill
									</button>
								{:else}
									<button
										class="server-action-btn success"
										onclick={(event) => handleAction(server.name, 'start', event)}
										disabled={actionLoading[server.name]}
									>
										Start
									</button>
								{/if}
							</div>
						</div>
					</div>
				{/each}
			</div>
		{:else}
			<div class="empty-state">
				<p class="empty-icon">[]</p>
				<h3>No servers yet</h3>
				<p>Create your first Minecraft server to get started</p>
				<a href="/servers/new" class="btn-primary">Create Server</a>
			</div>
		{/if}
	</div>

	<!-- Quick Links -->
	<div class="quick-links">
		<a href="/profiles" class="quick-link-card">
			<div class="quick-link-icon">[P]</div>
			<div class="quick-link-content">
				<div class="quick-link-title">Profiles</div>
				<div class="quick-link-desc">Download and manage server JARs</div>
			</div>
		</a>
	</div>
</div>

<style>
	.dashboard {
		max-width: 1200px;
	}

	.page-header {
		display: flex;
		justify-content: space-between;
		align-items: center;
		margin-bottom: 32px;
		gap: 24px;
	}

	h1 {
		margin: 0 0 8px;
		font-size: 32px;
		font-weight: 600;
	}

	h2 {
		margin: 0 0 16px;
		font-size: 20px;
		font-weight: 600;
	}

	h3 {
		margin: 0 0 8px;
		font-size: 18px;
		font-weight: 600;
	}

	.subtitle {
		margin: 0;
		color: #aab2d3;
		font-size: 15px;
	}

	.btn-primary {
		background: var(--mc-grass);
		color: white;
		border: none;
		border-radius: 8px;
		padding: 12px 24px;
		font-family: inherit;
		font-size: 15px;
		font-weight: 600;
		cursor: pointer;
		transition: background 0.2s;
		text-decoration: none;
		display: inline-block;
	}

	.btn-primary:hover {
		background: var(--mc-grass-dark);
	}

	.stats-grid {
		display: grid;
		grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
		gap: 20px;
		margin-bottom: 32px;
	}

	.stat-card {
		background: linear-gradient(160deg, rgba(26, 30, 47, 0.95), rgba(17, 20, 34, 0.95));
		border-radius: 16px;
		padding: 24px;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.35);
		display: flex;
		align-items: center;
		gap: 16px;
		border: 1px solid rgba(106, 176, 76, 0.12);
	}

	.stat-icon {
		font-size: 32px;
		opacity: 0.9;
		color: #b7f5a2;
	}

	.stat-content {
		flex: 1;
	}

	.stat-value {
		font-size: 28px;
		font-weight: 600;
		color: #eef0f8;
		line-height: 1;
		margin-bottom: 4px;
	}

	.stat-label {
		font-size: 13px;
		color: #9aa2c5;
	}

	.metrics-section {
		margin-bottom: 32px;
	}

	.metrics-grid {
		display: grid;
		grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
		gap: 16px;
	}

	.metric-card {
		background: #1a1e2f;
		border-radius: 12px;
		padding: 20px;
		box-shadow: 0 10px 30px rgba(0, 0, 0, 0.25);
		border: 1px solid rgba(42, 47, 71, 0.8);
	}

	.metric-header {
		margin-bottom: 12px;
	}

	.metric-title {
		font-size: 13px;
		color: #9aa2c5;
		text-transform: uppercase;
		letter-spacing: 0.5px;
		font-weight: 500;
	}

	.metric-value {
		font-size: 24px;
		font-weight: 600;
		color: #eef0f8;
	}

	.metric-subtext {
		font-size: 12px;
		color: #8890b1;
		margin-top: 4px;
	}

	.servers-section {
		margin-bottom: 32px;
	}

	.section-header {
		display: flex;
		justify-content: space-between;
		align-items: center;
		margin-bottom: 16px;
	}

	.link-btn {
		color: var(--mc-grass);
		text-decoration: none;
		font-size: 14px;
		font-weight: 500;
		transition: color 0.2s;
	}

	.link-btn:hover {
		color: var(--mc-grass-dark);
	}

	.server-list {
		background: #1a1e2f;
		border-radius: 16px;
		overflow: hidden;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.35);
		border: 1px solid rgba(106, 176, 76, 0.08);
	}

	.server-item {
		display: flex;
		justify-content: space-between;
		align-items: center;
		padding: 16px 20px;
		border-bottom: 1px solid #2a2f47;
		text-decoration: none;
		transition: background 0.2s;
		cursor: pointer;
	}

	.server-item:last-child {
		border-bottom: none;
	}

	.server-item:hover {
		background: rgba(106, 176, 76, 0.08);
	}

	.server-item:focus-visible {
		outline: 2px solid rgba(106, 176, 76, 0.6);
		outline-offset: -2px;
	}

	.server-info {
		display: flex;
		flex-direction: column;
		gap: 6px;
	}

	.server-name {
		font-size: 16px;
		font-weight: 500;
		color: #eef0f8;
	}

	.server-meta {
		display: flex;
		gap: 8px;
		align-items: center;
	}

	.profile-badge {
		font-size: 12px;
		color: #9aa2c5;
		background: rgba(106, 176, 76, 0.1);
		padding: 2px 8px;
		border-radius: 4px;
		border: 1px solid rgba(106, 176, 76, 0.25);
	}

	.port-badge {
		font-size: 12px;
		color: #9aa2c5;
		font-family: 'Courier New', monospace;
	}

	.restart-badge {
		font-size: 11px;
		color: #f4c08e;
		background: rgba(255, 200, 87, 0.15);
		padding: 2px 6px;
		border-radius: 6px;
		border: 1px solid rgba(255, 200, 87, 0.3);
	}

	.server-status {
		display: flex;
		align-items: center;
		gap: 8px;
		flex-wrap: wrap;
	}

	.status-indicator {
		width: 8px;
		height: 8px;
		border-radius: 50%;
	}

	.status-up {
		background: var(--mc-grass);
		box-shadow: 0 0 8px rgba(106, 176, 76, 0.4);
	}

	.status-down {
		background: #ff9f9f;
	}

	.status-text {
		font-size: 14px;
		color: #d4d9f1;
	}

	.server-actions {
		display: flex;
		gap: 6px;
		margin-left: 8px;
	}

	.server-action-btn {
		background: #2b2f45;
		color: #d4d9f1;
		border: none;
		border-radius: 6px;
		padding: 6px 10px;
		font-size: 12px;
		cursor: pointer;
		transition: background 0.2s;
	}

	.server-action-btn:hover:not(:disabled) {
		background: #3a3f5a;
	}

	.server-action-btn:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.server-action-btn.success {
		background: rgba(106, 176, 76, 0.2);
		color: #b7f5a2;
	}

	.server-action-btn.success:hover:not(:disabled) {
		background: rgba(106, 176, 76, 0.3);
	}

	.server-action-btn.danger {
		background: rgba(255, 92, 92, 0.2);
		color: #ffb6b6;
	}

	.server-action-btn.danger:hover:not(:disabled) {
		background: rgba(255, 92, 92, 0.3);
	}

	.players-count {
		font-size: 13px;
		color: #9aa2c5;
		margin-left: 4px;
	}

	.memory-count {
		font-size: 12px;
		color: #7ae68d;
		margin-left: 4px;
	}

	.mini-sparkline {
		width: 90px;
		height: 28px;
		margin-left: 6px;
		opacity: 0.9;
	}

	.error-box {
		background: rgba(255, 92, 92, 0.1);
		border: 1px solid rgba(255, 92, 92, 0.3);
		border-radius: 12px;
		padding: 16px 20px;
		color: #ff9f9f;
	}

	.error-box p {
		margin: 0;
	}

	.empty-state {
		text-align: center;
		padding: 60px 20px;
		color: #8e96bb;
	}

	.empty-icon {
		font-size: 48px;
		margin-bottom: 12px;
	}

	.quick-links {
		display: grid;
		grid-template-columns: repeat(auto-fit, minmax(280px, 1fr));
		gap: 20px;
	}

	.quick-link-card {
		background: #1a1e2f;
		border-radius: 16px;
		padding: 24px;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.35);
		display: flex;
		align-items: center;
		gap: 16px;
		text-decoration: none;
		transition: all 0.2s;
	}

	.quick-link-card:hover {
		transform: translateY(-2px);
		box-shadow: 0 24px 48px rgba(0, 0, 0, 0.45);
	}

	.quick-link-icon {
		font-size: 32px;
		opacity: 0.9;
	}

	.quick-link-content {
		flex: 1;
	}

	.quick-link-title {
		font-size: 16px;
		font-weight: 600;
		color: #eef0f8;
		margin-bottom: 4px;
	}

	.quick-link-desc {
		font-size: 13px;
		color: #9aa2c5;
	}

	@media (max-width: 768px) {
		.page-header {
			flex-direction: column;
			align-items: flex-start;
		}

		.stats-grid {
			grid-template-columns: repeat(2, 1fr);
		}

		.metrics-grid {
			grid-template-columns: 1fr;
		}

		.quick-links {
			grid-template-columns: 1fr;
		}
	}
</style>
