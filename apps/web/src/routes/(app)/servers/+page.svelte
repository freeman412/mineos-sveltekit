<script lang="ts">
	import { onMount } from 'svelte';
	import { goto, invalidateAll } from '$app/navigation';
	import { env } from '$env/dynamic/public';
	import { browser } from '$app/environment';
	import * as api from '$lib/api/client';
	import { modal } from '$lib/stores/modal';
	import { formatBytes, formatDate } from '$lib/utils/formatting';
	import { createEventStream, type EventStreamHandle } from '$lib/utils/eventStream';
	import ProgressBar from '$lib/components/ProgressBar.svelte';
	import StatusBadge from '$lib/components/StatusBadge.svelte';
	import type { PageData } from './$types';
	import type { ArchiveEntry, ForgeInstallStatus, ServerSummary } from '$lib/api/types';

	let { data }: { data: PageData } = $props();

	// Get hostname for server addresses
	const hostname = $derived.by(() => {
		const envHost = env.PUBLIC_MINECRAFT_HOST as string | undefined;
		return (envHost && envHost.trim()) || (browser ? window.location.hostname : 'localhost');
	});

	let actionLoading = $state<Record<string, boolean>>({});
	let importLoading = $state<Record<string, boolean>>({});
	let servers = $state<ServerSummary[]>(data.servers.data ?? []);
	let serversError = $state<string | null>(data.servers.error);
	let serversStream: EventStreamHandle | null = null;
	let memoryHistory = $state<Record<string, number[]>>({});
	let imports = $state<ArchiveEntry[]>(data.imports.data ?? []);
	let importsError = $state<string | null>(data.imports.error);
	let serverNames = $state<Record<string, string>>({});
	let dragActive = $state(false);
	let uploadError = $state('');
	let uploadBusy = $state(false);
	let uploadProgress = $state(0);
	let uploadFileName = $state('');
	let uploadStatus = $state('');
	let activeForgeInstalls = $state<ForgeInstallStatus[]>([]);
	let importJobs = $state<
		Record<string, { jobId: string; status: string; percentage: number; message?: string | null }>
	>({});
	const importStreams = new Map<string, EventSource>();
	let jobsInterval: ReturnType<typeof setInterval> | null = null;

	const maxMemoryPoints = 30;
	const creatingServers = $derived.by(
		() => new Set(activeForgeInstalls.map((install) => install.serverName))
	);

	$effect(() => {
		imports = data.imports.data ?? [];
		importsError = data.imports.error;
	});


	function buildSparkline(values: number[], width = 120, height = 32) {
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

	function handleCardClick(serverName: string) {
		if (creatingServers.has(serverName)) return;
		goto(`/servers/${encodeURIComponent(serverName)}`);
	}

	function handleCardKeydown(event: KeyboardEvent, serverName: string) {
		if (creatingServers.has(serverName)) return;
		if (event.key === 'Enter' || event.key === ' ') {
			event.preventDefault();
			handleCardClick(serverName);
		}
	}

	async function handleAction(
		serverName: string,
		action: 'start' | 'stop' | 'restart' | 'kill',
		event?: Event
	) {
		event?.stopPropagation();
		event?.preventDefault();
		if (creatingServers.has(serverName)) return;

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
				case 'restart':
					result = await api.restartServer(fetch, serverName);
					break;
				case 'kill':
					result = await api.killServer(fetch, serverName);
					break;
			}

			if (result.error) {
				await modal.error(`Failed to ${action} server: ${result.error}`);
			} else {
				// Wait a bit for the action to complete, then refresh
				setTimeout(() => invalidateAll(), 1000);
			}
		} finally {
			delete actionLoading[serverName];
			actionLoading = { ...actionLoading };
		}
	}

	async function loadActiveTasks() {
		try {
			const res = await fetch('/api/jobs');
			if (res.ok) {
				const payload = await res.json();
				activeForgeInstalls = payload.forgeInstalls ?? [];
			}
		} catch (err) {
			console.error('Failed to load active tasks:', err);
		}
	}

	async function handleDelete(serverName: string, event?: Event) {
		event?.stopPropagation();
		event?.preventDefault();

		const confirmed = await modal.confirm(`Are you sure you want to delete server "${serverName}"?`, 'Delete Server');
		if (!confirmed) return;

		actionLoading[serverName] = true;
		try {
			const result = await api.deleteServer(fetch, serverName);
			if (result.error) {
				await modal.error(`Failed to delete server: ${result.error}`);
			} else {
				await invalidateAll();
			}
		} finally {
			delete actionLoading[serverName];
			actionLoading = { ...actionLoading };
		}
	}

	function getApiBase() {
		return env.PUBLIC_API_BASE_URL || window.location.origin;
	}

	function uploadArchive(file: File): Promise<void> {
		return new Promise((resolve, reject) => {
			const xhr = new XMLHttpRequest();
			xhr.open('POST', `${getApiBase()}/api/v1/host/imports/upload`, true);
			xhr.withCredentials = true;
			xhr.setRequestHeader('X-File-Name', file.name);
			xhr.setRequestHeader('Content-Type', file.type || 'application/octet-stream');

			xhr.upload.onprogress = (event) => {
				if (!event.lengthComputable) return;
				uploadProgress = Math.round((event.loaded / event.total) * 100);
			};

			xhr.onload = () => {
				if (xhr.status >= 200 && xhr.status < 300) {
					resolve();
					return;
				}

				let message = 'Upload failed';
				try {
					const parsed = JSON.parse(xhr.responseText || '{}');
					message = parsed.error || message;
				} catch {
					if (xhr.responseText) {
						message = xhr.responseText;
					}
				}
				reject(new Error(message));
			};

			xhr.onerror = () => reject(new Error('Upload failed'));
			xhr.send(file);
		});
	}

	async function uploadArchives(files: FileList | File[]) {
		if (!files || files.length === 0) return;
		uploadError = '';
		uploadBusy = true;
		try {
			const list = Array.from(files);
			for (let i = 0; i < list.length; i += 1) {
				const file = list[i];
				uploadFileName = file.name;
				uploadStatus = `Uploading ${i + 1} of ${list.length}`;
				uploadProgress = 0;
				await uploadArchive(file);
			}
			await modal.success(
				`Uploaded ${list.length} archive${list.length === 1 ? '' : 's'}.`,
				'Upload complete'
			);
			await invalidateAll();
		} catch (err) {
			uploadError = err instanceof Error ? err.message : 'Upload failed';
		} finally {
			uploadBusy = false;
			dragActive = false;
			uploadStatus = '';
		}
	}

	function startImportJob(filename: string, jobId: string) {
		if (!jobId) return;
		importJobs[filename] = {
			jobId,
			status: 'queued',
			percentage: 0,
			message: 'Queued'
		};
		importJobs = { ...importJobs };

		const source = new EventSource(`/api/jobs/${encodeURIComponent(jobId)}/stream`);
		importStreams.set(jobId, source);

		source.onmessage = (event) => {
			try {
				const update = JSON.parse(event.data) as {
					status: string;
					percentage: number;
					message?: string | null;
				};

				importJobs[filename] = {
					jobId,
					status: update.status,
					percentage: update.percentage ?? 0,
					message: update.message ?? null
				};
				importJobs = { ...importJobs };

				if (update.status === 'completed' || update.status === 'failed') {
					source.close();
					importStreams.delete(jobId);
					if (update.status === 'completed') {
						void modal.success(`Import for "${filename}" finished.`, 'Import complete');
						void invalidateAll();
					} else {
						void modal.error(`Import for "${filename}" failed.`, 'Import failed');
					}
				}
			} catch {
				// ignore parse errors
			}
		};

		source.onerror = () => {
			source.close();
			importStreams.delete(jobId);
		};
	}

	async function handleCreateImport(filename: string) {
		const serverName = (serverNames[filename] || '').trim();
		if (!serverName) {
			await modal.alert('Server name is required', 'Required Field');
			return;
		}

		importLoading[filename] = true;
		try {
			const res = await fetch(
				`/api/host/imports/${encodeURIComponent(filename)}/create-server`,
				{
					method: 'POST',
					headers: { 'Content-Type': 'application/json' },
					body: JSON.stringify({ serverName })
				}
			);

			if (!res.ok) {
				const error = await res.json().catch(() => ({ error: 'Failed to import server' }));
				await modal.error(error.error || 'Failed to import server');
			} else {
				const result = await res.json();
				const jobId = result.jobId as string | undefined;
				if (jobId) {
					startImportJob(filename, jobId);
					await modal.alert(
						`Import for "${filename}" queued. You can keep working while it unpacks.`,
						'Import started'
					);
				}
				serverNames[filename] = '';
				serverNames = { ...serverNames };
			}
		} finally {
			delete importLoading[filename];
			importLoading = { ...importLoading };
		}
	}

	async function handleDeleteImport(filename: string) {
		const confirmed = await modal.confirm(
			`Delete import file "${filename}"?`,
			'Delete Import'
		);
		if (!confirmed) return;

		importLoading[filename] = true;
		try {
			const res = await fetch(`/api/host/imports/${encodeURIComponent(filename)}`, {
				method: 'DELETE'
			});

			if (!res.ok) {
				const error = await res.json().catch(() => ({ error: 'Failed to delete import' }));
				await modal.error(error.error || 'Failed to delete import');
			} else {
				imports = imports.filter((i) => i.filename !== filename);
			}
		} finally {
			delete importLoading[filename];
			importLoading = { ...importLoading };
		}
	}

	onMount(() => {
		updateMemoryHistory(servers);
		loadActiveTasks();
		jobsInterval = setInterval(loadActiveTasks, 5000);

		serversStream = createEventStream<ServerSummary[]>({
			url: '/api/host/servers/stream',
			onMessage: (nextServers) => {
				servers = nextServers;
				updateMemoryHistory(nextServers);
				serversError = null;
			},
			onClose: () => {
				serversStream = null;
			}
		});

		return () => {
			serversStream?.close();
			for (const source of importStreams.values()) {
				source.close();
			}
			importStreams.clear();
			if (jobsInterval) {
				clearInterval(jobsInterval);
			}
		};
	});
</script>

<div class="page-header">
	<div>
		<h1>Servers</h1>
		<p class="subtitle">Manage your Minecraft servers</p>
	</div>
	<a href="/servers/new" class="btn-primary">
		<span>+</span> Create Server
	</a>
</div>

{#if serversError}
	<div class="error-box">
		<p>Failed to load servers: {serversError}</p>
	</div>
{:else if servers.length > 0}
	<div class="server-grid">
		{#each servers as server}
			{@const isCreating = creatingServers.has(server.name)}
			<div
				class="server-card"
				class:creating={isCreating}
				role="link"
				tabindex={isCreating ? -1 : 0}
				aria-disabled={isCreating}
				onclick={() => handleCardClick(server.name)}
				onkeydown={(event) => handleCardKeydown(event, server.name)}
			>
				<div class="card-header">
					<div class="server-icon-wrapper">
						<img
							src="/api/servers/{server.name}/icon"
							alt="{server.name} icon"
							class="server-icon"
							onerror={(e) => (e.currentTarget.style.display = 'none')}
						/>
					</div>
					<div class="server-title">
						<StatusBadge variant={isCreating ? 'warning' : server.up ? 'success' : 'error'} dot size="lg" />
						<h2>{server.name}</h2>
					</div>
					<StatusBadge variant={isCreating ? 'warning' : server.up ? 'success' : 'error'} size="sm">
						{isCreating ? 'Creating' : server.up ? 'Running' : 'Stopped'}
					</StatusBadge>
				</div>

				<div class="card-meta">
					{#if isCreating}
						<span class="badge badge-warning">Creating</span>
					{/if}
					{#if server.profile}
						<span class="badge">Profile: {server.profile}</span>
					{/if}
					{#if server.port}
						<span class="badge badge-muted">{hostname}:{server.port}</span>
					{/if}
					{#if server.needsRestart}
						<span class="badge badge-warning">Restart required</span>
					{/if}
				</div>
				<div class="card-metrics">
					<div class="metric">
						<span class="metric-label">Players</span>
						<span class="metric-value">
							{server.playersOnline ?? '--'} / {server.playersMax ?? '--'}
						</span>
					</div>
					<div class="metric">
						<span class="metric-label">Status</span>
						<span class="metric-value">{isCreating ? 'Creating' : server.up ? 'Online' : 'Offline'}</span>
					</div>
					<div class="metric memory">
						<span class="metric-label">Memory</span>
						<span class="metric-value">{formatBytes(server.memoryBytes)}</span>
						{#if memoryHistory[server.name]?.length > 1}
							<svg class="sparkline" viewBox="0 0 120 32" preserveAspectRatio="none">
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
					</div>
				</div>

				<div class="card-actions">
					{#if server.up}
						<button
							class="btn-action btn-warning"
							onclick={(event) => handleAction(server.name, 'stop', event)}
							disabled={actionLoading[server.name] || isCreating}
						>
							Stop
						</button>
						<button
							class="btn-action"
							onclick={(event) => handleAction(server.name, 'restart', event)}
							disabled={actionLoading[server.name] || isCreating}
						>
							Restart
						</button>
						<button
							class="btn-action btn-danger"
							onclick={(event) => handleAction(server.name, 'kill', event)}
							disabled={actionLoading[server.name] || isCreating}
						>
							Kill
						</button>
					{:else}
						<button
							class="btn-action btn-success"
							onclick={(event) => handleAction(server.name, 'start', event)}
							disabled={actionLoading[server.name] || isCreating}
						>
							Start
						</button>
					{/if}
					<button
						class="btn-action btn-danger"
						onclick={(event) => handleDelete(server.name, event)}
						disabled={actionLoading[server.name] || isCreating}
					>
						Delete
					</button>
				</div>
			</div>
		{/each}
	</div>
{:else}
	<div class="empty-state">
		<p class="empty-icon">[]</p>
		<h2>No servers yet</h2>
		<p>Create your first Minecraft server to get started</p>
		<a href="/servers/new" class="btn-primary">Create Server</a>
	</div>
{/if}

<section class="import-section" id="import">
	<div class="section-header">
		<h2>Import Servers</h2>
		<span class="section-subtitle">Upload archives and unpack them in the background</span>
	</div>

	<div
		class="upload-zone"
		class:active={dragActive}
		ondragover={(event) => {
			event.preventDefault();
			dragActive = true;
		}}
		ondragleave={() => {
			dragActive = false;
		}}
		ondrop={(event) => {
			event.preventDefault();
			dragActive = false;
			if (event.dataTransfer?.files?.length) {
				uploadArchives(event.dataTransfer.files);
			}
		}}
	>
		<div>
			<h3>Drag & drop an archive</h3>
			<p>.zip, .tar.gz, or .tgz files supported</p>
		</div>
		<label class="btn-action">
			<input
				type="file"
				accept=".zip,.tar.gz,.tgz"
				multiple
				onchange={(event) => {
					const input = event.currentTarget as HTMLInputElement;
					if (input.files) uploadArchives(input.files);
					input.value = '';
				}}
				hidden
			/>
			{uploadBusy ? 'Uploading...' : 'Choose files'}
		</label>
	</div>

	{#if uploadBusy}
		<div class="upload-progress">
			<div class="progress-header">
				<strong>{uploadFileName}</strong>
				<span>{uploadStatus}</span>
			</div>
			<ProgressBar value={uploadProgress} color="green" size="sm" showLabel />
		</div>
	{/if}

	{#if uploadError}
		<p class="error-text">{uploadError}</p>
	{/if}

	{#if importsError}
		<div class="error-box">
			<p>Failed to load imports: {importsError}</p>
		</div>
	{:else if imports && imports.length > 0}
		<div class="imports-card">
			<table>
				<thead>
					<tr>
						<th>Filename</th>
						<th>Size</th>
						<th>Uploaded</th>
						<th>Server Name</th>
						<th>Unpack</th>
						<th>Action</th>
					</tr>
				</thead>
				<tbody>
					{#each imports as entry}
						<tr>
							<td class="mono">{entry.filename}</td>
							<td>{formatBytes(entry.size)}</td>
							<td>{formatDate(entry.time)}</td>
							<td>
								<input
									type="text"
									placeholder="new-server"
									value={serverNames[entry.filename] ?? ''}
									oninput={(event) => {
										const value = (event.currentTarget as HTMLInputElement).value;
										serverNames[entry.filename] = value;
										serverNames = { ...serverNames };
									}}
								/>
							</td>
							<td>
								{#if importJobs[entry.filename]}
									<div class="job-progress">
										<div
											class="job-bar"
											style={`width: ${importJobs[entry.filename].percentage}%`}
										></div>
									</div>
									<div class="job-meta">
										{importJobs[entry.filename].status} {importJobs[entry.filename].percentage}%
									</div>
								{:else}
									<span class="muted">--</span>
								{/if}
							</td>
							<td>
								<div class="import-actions">
									<button
										class="btn-action"
										onclick={() => handleCreateImport(entry.filename)}
										disabled={importLoading[entry.filename]}
									>
										{importLoading[entry.filename] ? 'Queueing...' : 'Import'}
									</button>
									<button
										class="btn-delete-import"
										onclick={() => handleDeleteImport(entry.filename)}
										disabled={importLoading[entry.filename]}
										title="Delete import"
									>
										🗑️
									</button>
								</div>
							</td>
						</tr>
					{/each}
				</tbody>
			</table>
		</div>
	{:else}
		<div class="empty-state">
			<h3>No imports found</h3>
			<p>Add archives to the import folder to create servers.</p>
		</div>
	{/if}
</section>

<style>
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
		display: flex;
		align-items: center;
		gap: 8px;
	}

	.btn-primary:hover {
		background: var(--mc-grass-dark);
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

	.server-grid {
		display: grid;
		grid-template-columns: repeat(auto-fill, minmax(min(350px, 100%), 1fr));
		gap: 20px;
	}

	.server-card {
		background: linear-gradient(160deg, rgba(26, 30, 47, 0.95), rgba(17, 20, 34, 0.95));
		border-radius: 16px;
		padding: 20px;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.35);
		border: 1px solid rgba(106, 176, 76, 0.15);
		display: flex;
		flex-direction: column;
		gap: 16px;
		cursor: pointer;
		transition: transform 0.2s ease, box-shadow 0.2s ease;
	}

	.server-card.creating {
		opacity: 0.6;
		cursor: progress;
		pointer-events: none;
	}

	.server-card:hover {
		transform: translateY(-3px);
		box-shadow: 0 28px 50px rgba(0, 0, 0, 0.45);
	}

	.server-card:focus-visible {
		outline: 2px solid rgba(106, 176, 76, 0.6);
		outline-offset: 4px;
	}

	.card-header {
		display: flex;
		justify-content: space-between;
		align-items: center;
		gap: 12px;
	}

	.card-header h2 {
		margin: 0 0 8px;
		font-size: 20px;
		font-weight: 600;
	}

	.server-icon-wrapper {
		flex-shrink: 0;
	}

	.server-icon {
		width: 48px;
		height: 48px;
		border-radius: 8px;
		border: 2px solid rgba(106, 176, 76, 0.3);
		image-rendering: pixelated;
		background: rgba(0, 0, 0, 0.3);
		box-shadow: 0 2px 8px rgba(0, 0, 0, 0.4);
	}

	.server-title {
		display: flex;
		align-items: center;
		gap: 10px;
		flex: 1;
	}

	.server-title h2 {
		margin: 0;
		font-size: 20px;
		font-weight: 600;
	}

	.card-meta {
		display: flex;
		gap: 8px;
		flex-wrap: wrap;
	}

	.badge {
		display: inline-flex;
		align-items: center;
		gap: 6px;
		background: rgba(106, 176, 76, 0.12);
		color: #d4f5dc;
		padding: 4px 10px;
		border-radius: 999px;
		font-size: 12px;
		font-weight: 500;
		border: 1px solid rgba(106, 176, 76, 0.3);
	}

	.badge-muted {
		background: rgba(88, 101, 242, 0.08);
		color: #c7cbe0;
		border-color: rgba(88, 101, 242, 0.25);
	}

	.badge-warning {
		background: rgba(255, 200, 87, 0.15);
		color: #f4c08e;
		border-color: rgba(255, 200, 87, 0.35);
	}

	.card-metrics {
		display: grid;
		grid-template-columns: repeat(auto-fit, minmax(140px, 1fr));
		gap: 12px;
	}

	.metric {
		background: rgba(20, 24, 39, 0.8);
		border-radius: 12px;
		padding: 12px;
		border: 1px solid rgba(42, 47, 71, 0.8);
		display: flex;
		flex-direction: column;
		gap: 4px;
	}

	.metric-label {
		display: block;
		font-size: 12px;
		color: #9aa2c5;
		margin-bottom: 4px;
	}

	.metric-value {
		font-size: 16px;
		color: #eef0f8;
		font-weight: 600;
	}

	.sparkline {
		width: 100%;
		height: 32px;
		opacity: 0.9;
	}

	.card-actions {
		display: flex;
		gap: 8px;
		flex-wrap: wrap;
	}

	.btn-action {
		background: #2b2f45;
		color: #d4d9f1;
		border: none;
		border-radius: 6px;
		padding: 8px 14px;
		font-family: inherit;
		font-size: 13px;
		font-weight: 500;
		cursor: pointer;
		transition: all 0.2s;
		text-decoration: none;
		display: inline-block;
	}

	.btn-action:hover:not(:disabled) {
		background: #3a3f5a;
	}

	.btn-action:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.import-actions {
		display: flex;
		gap: 8px;
		align-items: center;
	}

	.btn-delete-import {
		background: transparent;
		border: 1px solid rgba(255, 92, 92, 0.3);
		border-radius: 6px;
		padding: 6px 10px;
		font-size: 14px;
		cursor: pointer;
		transition: all 0.2s;
	}

	.btn-delete-import:hover:not(:disabled) {
		background: rgba(255, 92, 92, 0.15);
		border-color: rgba(255, 92, 92, 0.5);
	}

	.btn-delete-import:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.btn-success {
		background: rgba(106, 176, 76, 0.18);
		color: #b7f5a2;
	}

	.btn-success:hover:not(:disabled) {
		background: rgba(106, 176, 76, 0.28);
	}

	.btn-warning {
		background: rgba(255, 200, 87, 0.15);
		color: #ffc857;
	}

	.btn-warning:hover:not(:disabled) {
		background: rgba(255, 200, 87, 0.25);
	}

	.btn-danger {
		background: rgba(255, 92, 92, 0.15);
		color: #ff9f9f;
	}

	.btn-danger:hover:not(:disabled) {
		background: rgba(255, 92, 92, 0.25);
	}

	.import-section {
		margin-top: 48px;
		display: flex;
		flex-direction: column;
		gap: 20px;
	}

	.section-subtitle {
		color: #8e96bb;
		font-size: 13px;
	}

	.upload-zone {
		background: #1a1e2f;
		border-radius: 16px;
		padding: 24px;
		border: 2px dashed rgba(106, 176, 76, 0.2);
		display: flex;
		justify-content: space-between;
		align-items: center;
		gap: 16px;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.35);
	}

	.upload-zone.active {
		border-color: rgba(106, 176, 76, 0.6);
		background: rgba(106, 176, 76, 0.08);
	}

	.upload-zone h3 {
		margin: 0 0 6px;
		font-size: 18px;
	}

	.upload-zone p {
		margin: 0;
		color: #aab2d3;
		font-size: 13px;
	}

	.upload-progress {
		background: rgba(20, 24, 39, 0.8);
		border-radius: 12px;
		padding: 16px;
		border: 1px solid rgba(42, 47, 71, 0.8);
	}

	.progress-header {
		display: flex;
		justify-content: space-between;
		align-items: center;
		font-size: 13px;
		color: #c4cff5;
		margin-bottom: 10px;
		gap: 12px;
	}

	.imports-card {
		background: #1a1e2f;
		border-radius: 16px;
		padding: 20px;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.35);
		overflow-x: auto;
	}

	table {
		width: 100%;
		border-collapse: collapse;
	}

	th,
	td {
		padding: 12px 10px;
		text-align: left;
		border-bottom: 1px solid #2b2f45;
		color: #d4d9f1;
		font-size: 14px;
	}

	th {
		color: #9aa2c5;
		font-weight: 600;
	}

	.mono {
		font-family: 'Courier New', monospace;
		font-size: 12px;
	}

	.imports-card input[type='text'] {
		width: 100%;
		box-sizing: border-box;
	}

	.job-progress {
		background: #0f1320;
		border-radius: 999px;
		height: 8px;
		overflow: hidden;
		border: 1px solid rgba(42, 47, 71, 0.8);
	}

	.job-bar {
		height: 100%;
		background: rgba(124, 179, 255, 0.85);
		width: 0;
		transition: width 0.2s ease;
	}

	.job-meta {
		margin-top: 6px;
		font-size: 11px;
		color: #9aa2c5;
		text-transform: uppercase;
		letter-spacing: 0.04em;
	}

	.muted {
		color: #6a7192;
		font-size: 12px;
	}

	.error-text {
		color: #ff9f9f;
		font-size: 13px;
		margin: 0;
	}

	.empty-state {
		text-align: center;
		padding: 80px 20px;
		color: #8e96bb;
	}

	.empty-icon {
		font-size: 64px;
		margin-bottom: 16px;
	}

	.empty-state h2 {
		margin: 0 0 8px;
		color: #eef0f8;
	}

	.empty-state p {
		margin: 0 0 24px;
	}

	@media (max-width: 640px) {
		.page-header {
			flex-direction: column;
			align-items: flex-start;
		}

		.server-grid {
			grid-template-columns: 1fr;
		}
	}
</style>
