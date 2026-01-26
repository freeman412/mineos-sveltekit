<script lang="ts">
	import { onDestroy, onMount } from 'svelte';
	import { invalidateAll } from '$app/navigation';
	import { modal } from '$lib/stores/modal';
	import type { ServerSummary } from '$lib/api/types';
	import { withBase } from '$lib/utils/paths';

	let buildGroup = $state('spigot');
	let buildVersion = $state('');
	let buildError = $state('');
	let status = $state<'idle' | 'running' | 'completed' | 'failed'>('idle');
	let runId = $state<string | null>(null);
	let profileId = $state<string | null>(null);
	let logs = $state<string[]>([]);
	let runs = $state<any[]>([]);
	let runsLoading = $state(false);
	let logContainer: HTMLDivElement | null = null;
	let eventSource: EventSource | null = null;
	let loadRunsTimeout: ReturnType<typeof setTimeout> | null = null;
	let hasInvalidated = $state(false);

	// For copy-to-server quick action
	let servers = $state<ServerSummary[]>([]);
	let selectedServer = $state('');
	let copyLoading = $state(false);

	// Common Minecraft versions for Spigot/CraftBukkit
	const commonVersions = [
		'latest',
		'1.21.4',
		'1.21.3',
		'1.21.1',
		'1.21',
		'1.20.6',
		'1.20.4',
		'1.20.2',
		'1.20.1',
		'1.20',
		'1.19.4',
		'1.19.3',
		'1.19.2',
		'1.19.1',
		'1.19',
		'1.18.2',
		'1.18.1',
		'1.17.1',
		'1.16.5',
		'1.16.4',
		'1.15.2',
		'1.14.4',
		'1.13.2',
		'1.12.2',
		'1.11.2',
		'1.10.2',
		'1.9.4',
		'1.8.8',
	];

	$effect(() => {
		if (logContainer) {
			logContainer.scrollTop = logContainer.scrollHeight;
		}
	});

	async function startBuild() {
		if (!buildVersion.trim()) {
			buildError = 'Version is required';
			return;
		}

		buildError = '';
		status = 'running';
		logs = [];
		profileId = null;
		runId = null;
		hasInvalidated = false;

		try {
			const res = await fetch('/api/host/profiles/buildtools', {
				method: 'POST',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify({ group: buildGroup, version: buildVersion.trim() })
			});

			const payload = await res.json().catch(() => null);
			if (!res.ok) {
				status = 'failed';
				buildError = payload?.error || 'Failed to start BuildTools';
				return;
			}

			runId = payload.runId;
			profileId = payload.profileId;
			status = payload.status ?? 'running';

			if (runId) {
				localStorage.setItem('mineos_buildtools_run', runId);
				openStream(runId);
				loadRuns();
			}
		} catch (err) {
			status = 'failed';
			buildError = err instanceof Error ? err.message : 'Failed to start BuildTools';
		}
	}

	function openStream(id: string) {
		eventSource?.close();
		eventSource = new EventSource(`/api/host/buildtools/${id}/stream`);

		eventSource.onmessage = (event) => {
			try {
				const entry = JSON.parse(event.data);
				if (entry?.message) {
					logs = [...logs, entry.message];
				}
				if (entry?.status) {
					const nextStatus = entry.status as typeof status;
					const prevStatus = status;
					status = nextStatus;

					if (nextStatus !== 'running') {
						if (nextStatus === 'completed' && !hasInvalidated) {
							hasInvalidated = true;
							invalidateAll();
						}
						loadRunsDebounced();
						eventSource?.close();
						eventSource = null;
					} else if (prevStatus !== nextStatus) {
						hasInvalidated = false;
					}
				}
			} catch {
				logs = [...logs, event.data];
			}
		};

		eventSource.onerror = () => {
			if (status === 'running') {
				buildError = 'Log stream disconnected. Check the server logs.';
				status = 'failed';
			}
			eventSource?.close();
			eventSource = null;
		};
	}

	async function loadRuns() {
		if (runsLoading) return; // Prevent concurrent requests

		runsLoading = true;
		try {
			const res = await fetch('/api/host/profiles/buildtools/runs');
			if (res.ok) {
				runs = await res.json();
			}
		} finally {
			runsLoading = false;
		}
	}

	function loadRunsDebounced() {
		if (loadRunsTimeout) clearTimeout(loadRunsTimeout);
		loadRunsTimeout = setTimeout(() => {
			loadRuns();
		}, 500); // Debounce by 500ms
	}

	function attachRun(id: string) {
		runId = id;
		logs = [];
		buildError = '';
		status = 'running';
		hasInvalidated = false;
		const run = runs.find((item) => item.runId === id);
		if (run) {
			profileId = run.profileId;
			status = run.status;
			buildGroup = run.group ?? buildGroup;
			buildVersion = run.version ?? buildVersion;
			if (run.status === 'completed' || run.status === 'failed') {
				hasInvalidated = true;
			}
		}
		localStorage.setItem('mineos_buildtools_run', id);
		openStream(id);
	}

	async function loadServers() {
		try {
			const res = await fetch('/api/servers');
			if (res.ok) {
				const data = await res.json();
				servers = data ?? [];
				if (servers.length > 0 && !selectedServer) {
					selectedServer = servers[0].name;
				}
			}
		} catch {
			// Ignore server load errors
		}
	}

	async function copyToServer() {
		if (!profileId || !selectedServer) return;

		copyLoading = true;
		try {
			const res = await fetch(`/api/host/profiles/${profileId}/copy-to-server`, {
				method: 'POST',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify({ serverName: selectedServer })
			});

			if (!res.ok) {
				const error = await res.json().catch(() => ({ error: 'Failed to copy profile' }));
				await modal.error(error.error || 'Failed to copy profile');
			} else {
				await modal.alert(
					`Profile "${profileId}" has been copied to server "${selectedServer}". The server needs to be restarted to use the new JAR file.`,
					'Profile Copied'
				);
			}
		} finally {
			copyLoading = false;
		}
	}

	onMount(() => {
		loadRuns();
		loadServers();
		const lastRun = localStorage.getItem('mineos_buildtools_run');
		if (lastRun) {
			attachRun(lastRun);
		}
	});

	onDestroy(() => {
		eventSource?.close();
		if (loadRunsTimeout) clearTimeout(loadRunsTimeout);
	});
</script>

<div class="page-header">
	<div>
		<a href="/profiles" class="breadcrumb">Back to Profiles</a>
		<h1>BuildTools Console</h1>
		<p class="subtitle">Build Spigot or CraftBukkit jars with live output.</p>
	</div>
</div>

<div class="buildtools-layout">
	<section class="buildtools-panel">
		<form
			class="buildtools-form"
			onsubmit={(event) => {
				event.preventDefault();
				if (status !== 'running') {
					startBuild();
				}
			}}
		>
			<label>
				Group
				<select bind:value={buildGroup} disabled={status === 'running'}>
					<option value="spigot">Spigot</option>
					<option value="craftbukkit">CraftBukkit</option>
				</select>
			</label>
			<label>
				Version
				<select bind:value={buildVersion} disabled={status === 'running'}>
					<option value="">Select a version...</option>
					{#each commonVersions as version}
						<option value={version}>{version}</option>
					{/each}
				</select>
			</label>
			<button class="btn-primary" type="submit" disabled={status === 'running'}>
				{status === 'running' ? 'Building...' : 'Run BuildTools'}
			</button>
		</form>

		{#if buildError}
			<p class="error">{buildError}</p>
		{/if}

		{#if runId}
			<div class="run-meta">
				<p>
					Run ID: <span>{runId}</span>
				</p>
				{#if profileId}
					<p>
						Profile ID: <span>{profileId}</span>
					</p>
				{/if}
				<p>
					Status: <span class:status-running={status === 'running'} class:status-success={status === 'completed'} class:status-failed={status === 'failed'}>{status}</span>
				</p>
			</div>

			{#if status === 'completed' && profileId}
				<div class="success-actions">
					<div class="success-header">
						<span class="success-icon">✓</span>
						<div>
							<h4>Build Complete!</h4>
							<p>Your {buildGroup} {buildVersion} JAR is ready to use.</p>
						</div>
					</div>

					<div class="quick-actions">
						<p class="action-label">Deploy to a server:</p>
						<div class="copy-group">
							<select bind:value={selectedServer} disabled={copyLoading}>
								{#each servers as server (server.name)}
									<option value={server.name}>{server.name}</option>
								{/each}
							</select>
							<button
								class="btn-primary"
								onclick={copyToServer}
								disabled={copyLoading || !selectedServer || servers.length === 0}
							>
								{copyLoading ? 'Copying...' : 'Copy to Server'}
							</button>
						</div>
						{#if servers.length === 0}
							<p class="no-servers">
								No servers available. <a href={withBase('/servers')}>Create a server</a> first.
							</p>
						{/if}
					</div>

					<div class="alt-actions">
						<a href="/profiles?group={buildGroup}" class="link-action">
							View in Profiles →
						</a>
					</div>
				</div>
			{/if}
		{/if}

		<div class="run-list">
			<div class="run-list-header">
				<h3>Recent Runs</h3>
				<button class="btn-secondary" onclick={loadRuns} disabled={runsLoading}>
					{runsLoading ? 'Refreshing...' : 'Refresh'}
				</button>
			</div>
			{#if runs.length === 0}
				<p class="run-empty">No BuildTools runs yet.</p>
			{:else}
				<ul>
					{#each runs as run}
						<li>
							<div>
								<div class="run-title">{run.profileId}</div>
								<div class="run-meta">{run.group} {run.version}</div>
							</div>
							<div class="run-actions">
								<span class="run-status" class:status-success={run.status === 'completed'} class:status-failed={run.status === 'failed'}>
									{run.status}
								</span>
								<button class="btn-secondary" onclick={() => attachRun(run.runId)}>View logs</button>
							</div>
						</li>
					{/each}
				</ul>
			{/if}
		</div>
	</section>

	<section class="log-panel">
		<div class="log-header">
			<h2>Build Output</h2>
			<p>Logs are written to <code>BaseDirectory/logs/buildtools</code> (default: <code>/var/games/minecraft/logs/buildtools</code>).</p>
		</div>
		<div class="log-output" bind:this={logContainer}>
			{#if logs.length === 0}
				<p class="log-placeholder">Build output will appear here.</p>
			{:else}
				{#each logs as line}
					<div class="log-line">{line}</div>
				{/each}
			{/if}
		</div>
	</section>
</div>

<style>
	.page-header {
		display: flex;
		justify-content: space-between;
		align-items: center;
		margin-bottom: 24px;
	}

	.breadcrumb {
		color: #aab2d3;
		text-decoration: none;
		font-size: 13px;
	}

	h1 {
		margin: 8px 0 6px;
		font-size: 30px;
	}

	.subtitle {
		margin: 0;
		color: #9aa2c5;
	}

	.buildtools-layout {
		display: grid;
		grid-template-columns: minmax(240px, 320px) 1fr;
		gap: 24px;
	}

	.buildtools-panel,
	.log-panel {
		background: #1a1e2f;
		border-radius: 16px;
		padding: 20px;
		border: 1px solid rgba(106, 176, 76, 0.12);
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.35);
	}

	.run-list {
		margin-top: 24px;
		display: flex;
		flex-direction: column;
		gap: 12px;
	}

	.run-list-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
	}

	.run-list h3 {
		margin: 0;
		font-size: 16px;
	}

	.run-list ul {
		list-style: none;
		padding: 0;
		margin: 0;
		display: grid;
		gap: 10px;
	}

	.run-list li {
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 12px;
		background: #141827;
		border-radius: 12px;
		padding: 12px;
		border: 1px solid rgba(42, 47, 71, 0.8);
	}

	.run-title {
		font-weight: 600;
		color: #eef0f8;
	}

	.run-meta {
		font-size: 12px;
		color: #9aa2c5;
	}

	.run-actions {
		display: flex;
		align-items: center;
		gap: 10px;
	}

	.run-status {
		font-size: 12px;
		color: #f0c674;
	}

	.run-empty {
		margin: 0;
		color: #9aa2c5;
		font-size: 13px;
	}

	.btn-secondary {
		background: #2b2f45;
		color: #d4d9f1;
		border: none;
		border-radius: 8px;
		padding: 8px 12px;
		font-size: 12px;
		cursor: pointer;
	}

	.btn-secondary:disabled {
		opacity: 0.6;
		cursor: not-allowed;
	}

	.buildtools-form {
		display: flex;
		flex-direction: column;
		gap: 12px;
	}

	label {
		display: flex;
		flex-direction: column;
		gap: 6px;
		font-size: 13px;
		color: #aab2d3;
	}

	input,
	select {
		background: #141827;
		border: 1px solid #2a2f47;
		border-radius: 8px;
		padding: 10px 12px;
		color: #eef0f8;
		font-family: inherit;
		font-size: 14px;
	}

	.btn-primary {
		background: var(--mc-grass);
		color: #fff;
		border: none;
		border-radius: 8px;
		padding: 12px 20px;
		font-size: 14px;
		font-weight: 600;
		cursor: pointer;
		display: inline-flex;
		align-items: center;
		justify-content: center;
		text-decoration: none;
	}

	.btn-primary:disabled {
		opacity: 0.6;
		cursor: not-allowed;
	}

	.error {
		color: #ff9f9f;
		margin-top: 12px;
		font-size: 13px;
	}

	.run-meta {
		margin-top: 16px;
		font-size: 13px;
		color: #aab2d3;
		display: grid;
		gap: 6px;
	}

	.run-meta span {
		color: #eef0f8;
		font-weight: 600;
	}

	.status-running {
		color: #f0c674;
	}

	.status-success {
		color: #b7f5a2;
	}

	.status-failed {
		color: #ff9f9f;
	}

	/* Success Actions */
	.success-actions {
		margin-top: 20px;
		background: rgba(106, 176, 76, 0.1);
		border: 1px solid rgba(106, 176, 76, 0.3);
		border-radius: 12px;
		padding: 16px;
	}

	.success-header {
		display: flex;
		align-items: center;
		gap: 12px;
		margin-bottom: 16px;
	}

	.success-icon {
		width: 32px;
		height: 32px;
		background: rgba(106, 176, 76, 0.2);
		border-radius: 50%;
		display: flex;
		align-items: center;
		justify-content: center;
		color: #b7f5a2;
		font-size: 16px;
		font-weight: bold;
	}

	.success-header h4 {
		margin: 0 0 2px;
		font-size: 15px;
		color: #b7f5a2;
	}

	.success-header p {
		margin: 0;
		font-size: 13px;
		color: #9aa2c5;
	}

	.quick-actions {
		background: rgba(20, 24, 39, 0.5);
		border-radius: 8px;
		padding: 12px;
	}

	.action-label {
		margin: 0 0 10px;
		font-size: 13px;
		color: #aab2d3;
		font-weight: 500;
	}

	.copy-group {
		display: flex;
		gap: 8px;
	}

	.copy-group select {
		flex: 1;
		min-width: 0;
	}

	.no-servers {
		margin: 8px 0 0;
		font-size: 12px;
		color: #9aa2c5;
	}

	.no-servers a {
		color: #6ab04c;
		text-decoration: none;
	}

	.no-servers a:hover {
		text-decoration: underline;
	}

	.alt-actions {
		margin-top: 12px;
		padding-top: 12px;
		border-top: 1px solid rgba(42, 47, 71, 0.6);
		display: flex;
		justify-content: flex-end;
	}

	.link-action {
		color: #a5b4fc;
		font-size: 13px;
		text-decoration: none;
	}

	.link-action:hover {
		text-decoration: underline;
	}

	.log-header {
		display: flex;
		align-items: baseline;
		justify-content: space-between;
		gap: 12px;
		margin-bottom: 12px;
	}

	.log-header h2 {
		margin: 0;
		font-size: 18px;
	}

	.log-header p {
		margin: 0;
		color: #9aa2c5;
		font-size: 12px;
	}

	.log-output {
		background: #0f121e;
		border: 1px solid rgba(42, 47, 71, 0.8);
		border-radius: 12px;
		padding: 16px;
		height: 420px;
		overflow-y: auto;
		font-family: "Cascadia Code", "Fira Code", "Consolas", monospace;
		font-size: 12px;
		color: #d4d9f1;
	}

	.log-line {
		white-space: pre-wrap;
		word-break: break-word;
	}

	.log-placeholder {
		margin: 0;
		color: #6f789b;
	}

	code {
		background: rgba(20, 24, 39, 0.8);
		padding: 2px 6px;
		border-radius: 6px;
		font-size: 12px;
	}

	@media (max-width: 960px) {
		.buildtools-layout {
			grid-template-columns: 1fr;
		}
	}
</style>
