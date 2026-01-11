<script lang="ts">
	import { onDestroy, onMount } from 'svelte';
	import { invalidateAll } from '$app/navigation';

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
				if (entry?.status && entry.status !== 'running') {
					status = entry.status;
					if (entry.status === 'completed') {
						invalidateAll();
					}
					loadRuns();
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

	function attachRun(id: string) {
		runId = id;
		logs = [];
		buildError = '';
		status = 'running';
		const run = runs.find((item) => item.runId === id);
		if (run) {
			profileId = run.profileId;
			status = run.status;
			buildGroup = run.group ?? buildGroup;
			buildVersion = run.version ?? buildVersion;
		}
		localStorage.setItem('mineos_buildtools_run', id);
		openStream(id);
	}

	onMount(() => {
		loadRuns();
		const lastRun = localStorage.getItem('mineos_buildtools_run');
		if (lastRun) {
			attachRun(lastRun);
		}
	});

	onDestroy(() => {
		eventSource?.close();
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
				<input type="text" bind:value={buildVersion} placeholder="1.20.4" disabled={status === 'running'} />
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
