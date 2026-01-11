<script lang="ts">
	import { invalidateAll } from '$app/navigation';
	import type { PageData } from './$types';
	import type { LayoutData } from '../$types';
	import { modal } from '$lib/stores/modal';

	let { data }: { data: PageData & { server: LayoutData['server'] } } = $props();

	let loading = $state(false);
	let actionLoading = $state<Record<string, boolean>>({});
	let backups = $state<any[]>([]);
	let jobStatus = $state<JobProgress | null>(null);
	let jobError = $state<string | null>(null);
	let jobEventSource: EventSource | null = null;

	type JobProgress = {
		jobId: string;
		type: string;
		serverName: string;
		status: string;
		percentage: number;
		message: string | null;
		timestamp: string;
	};

	$effect(() => {
		loadBackups();
	});

	async function loadBackups() {
		if (!data.server) return;
		try {
			const res = await fetch(`/api/servers/${data.server.name}/backups`);
			if (res.ok) {
				backups = await res.json();
			}
		} catch (err) {
			console.error('Failed to load backups:', err);
		}
	}

	async function createBackup() {
		if (!data.server) return;
		loading = true;
		try {
			const res = await fetch(`/api/servers/${data.server.name}/backups`, { method: 'POST' });
			if (res.ok) {
				const payload = await res.json().catch(() => null);
				if (payload?.jobId) {
					startJobStream(payload.jobId);
				}
			} else {
				const errorData = await res.json().catch(() => ({}));
				await modal.error(`Failed to create backup: ${errorData.error || res.statusText}`);
			}
		} catch (err) {
			await modal.error(`Error: ${err instanceof Error ? err.message : 'Unknown error'}`);
		} finally {
			loading = false;
		}
	}

	async function restoreBackup(timestamp: string) {
		if (!data.server) return;
		const confirmed = await modal.confirm(`Are you sure you want to restore from ${formatDate(timestamp)}? This will overwrite current server files.`, 'Restore Backup');
		if (!confirmed) return;

		actionLoading[timestamp] = true;
		try {
			const res = await fetch(`/api/servers/${data.server.name}/backups/restore`, {
				method: 'POST',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify({ timestamp })
			});

			if (res.ok) {
				await modal.success('Server restored successfully');
				await loadBackups();
			} else {
				const errorData = await res.json().catch(() => ({}));
				await modal.error(`Failed to restore backup: ${errorData.error || res.statusText}`);
			}
		} catch (err) {
			await modal.error(`Error: ${err instanceof Error ? err.message : 'Unknown error'}`);
		} finally {
			delete actionLoading[timestamp];
			actionLoading = { ...actionLoading };
		}
	}

	async function pruneBackups() {
		if (!data.server) return;
		const keepCount = prompt('How many backups should we keep?', '5');
		if (!keepCount) return;

		loading = true;
		try {
			const res = await fetch(`/api/servers/${data.server.name}/backups/prune?keepCount=${keepCount}`, {
				method: 'DELETE'
			});

			if (res.ok) {
				await loadBackups();
			} else {
				const errorData = await res.json().catch(() => ({}));
				await modal.error(`Failed to prune backups: ${errorData.error || res.statusText}`);
			}
		} catch (err) {
			await modal.error(`Error: ${err instanceof Error ? err.message : 'Unknown error'}`);
		} finally {
			loading = false;
		}
	}

	function startJobStream(jobId: string) {
		jobError = null;
		jobStatus = {
			jobId,
			type: 'backup',
			serverName: data.server?.name ?? '',
			status: 'queued',
			percentage: 0,
			message: 'Queued',
			timestamp: new Date().toISOString()
		};

		if (jobEventSource) {
			jobEventSource.close();
		}

		jobEventSource = new EventSource(`/api/jobs/${jobId}/stream`);
		jobEventSource.onmessage = (event) => {
			try {
				jobStatus = JSON.parse(event.data) as JobProgress;
				if (jobStatus.status === 'completed' || jobStatus.status === 'failed') {
					jobEventSource?.close();
					jobEventSource = null;
					loadBackups();
				}
			} catch {
				jobError = 'Failed to parse job status';
			}
		};
		jobEventSource.onerror = () => {
			jobError = 'Job stream error';
			jobEventSource?.close();
			jobEventSource = null;
		};
	}

	$effect(() => {
		return () => {
			jobEventSource?.close();
		};
	});

	const formatDate = (dateStr: string) => {
		const date = new Date(dateStr);
		return date.toLocaleDateString() + ' ' + date.toLocaleTimeString();
	};

	const formatBytes = (bytes: number | null) => {
		if (!bytes) return 'N/A';
		const units = ['B', 'KB', 'MB', 'GB', 'TB'];
		const i = Math.floor(Math.log(bytes) / Math.log(1024));
		return `${(bytes / Math.pow(1024, i)).toFixed(1)} ${units[i]}`;
	};
</script>

<div class="page">
	<div class="page-header">
		<div>
			<h2>Backups</h2>
			<p class="subtitle">Incremental backups using rdiff-backup</p>
		</div>
		<div class="action-buttons">
			<button class="btn-primary" onclick={createBackup} disabled={loading}>
				{loading ? 'Creating...' : '+ Create Backup'}
			</button>
			<button class="btn-secondary" onclick={pruneBackups} disabled={loading}>
				Prune Old Backups
			</button>
		</div>
	</div>

	{#if jobStatus}
		<div class="job-card">
			<div class="job-header">
				<h3>Backup Job</h3>
				<span class="job-status">{jobStatus.status}</span>
			</div>
			<div class="job-details">
				<div class="job-progress">
					<div class="job-progress-bar" style={`width: ${jobStatus.percentage}%`}></div>
				</div>
				<p class="job-message">{jobStatus.message ?? 'Working...'}</p>
				<p class="job-meta">Job ID: {jobStatus.jobId}</p>
				{#if jobError}
					<p class="job-error">{jobError}</p>
				{/if}
			</div>
		</div>
	{/if}

	{#if backups.length === 0}
		<div class="empty-state">
			<p class="empty-icon">💾</p>
			<h3>No backups yet</h3>
			<p>Create your first backup to get started</p>
			<button class="btn-primary" onclick={createBackup} disabled={loading}>Create Backup</button>
		</div>
	{:else}
		<div class="backup-list">
			<table>
				<thead>
					<tr>
						<th>Date & Time</th>
						<th>Type</th>
						<th>Size</th>
						<th>Actions</th>
					</tr>
				</thead>
				<tbody>
					{#each backups as backup}
						<tr>
							<td>{formatDate(backup.time)}</td>
							<td><span class="badge">{backup.step}</span></td>
							<td>{formatBytes(backup.size)}</td>
							<td>
								<button
									class="btn-action"
									onclick={() => restoreBackup(backup.time)}
									disabled={actionLoading[backup.time]}
								>
									{actionLoading[backup.time] ? 'Restoring...' : 'Restore'}
								</button>
							</td>
						</tr>
					{/each}
				</tbody>
			</table>
		</div>
	{/if}
</div>

<style>
	.page {
		display: flex;
		flex-direction: column;
		gap: 24px;
	}

	.page-header {
		display: flex;
		justify-content: space-between;
		align-items: center;
		gap: 24px;
		flex-wrap: wrap;
	}

	h2 {
		margin: 0 0 8px;
		font-size: 24px;
		font-weight: 600;
	}

	.subtitle {
		margin: 0;
		color: #aab2d3;
		font-size: 14px;
	}

	.action-buttons {
		display: flex;
		gap: 12px;
	}

	.btn-primary {
		background: #5865f2;
		color: white;
		border: none;
		border-radius: 8px;
		padding: 10px 20px;
		font-family: inherit;
		font-size: 14px;
		font-weight: 600;
		cursor: pointer;
		transition: background 0.2s;
	}

	.btn-primary:hover:not(:disabled) {
		background: #4752c4;
	}

	.btn-primary:disabled {
		opacity: 0.6;
		cursor: not-allowed;
	}

	.btn-secondary {
		background: #2b2f45;
		color: #d4d9f1;
		border: none;
		border-radius: 8px;
		padding: 10px 20px;
		font-family: inherit;
		font-size: 14px;
		font-weight: 500;
		cursor: pointer;
		transition: background 0.2s;
	}

	.btn-secondary:hover:not(:disabled) {
		background: #3a3f5a;
	}

	.btn-secondary:disabled {
		opacity: 0.6;
		cursor: not-allowed;
	}

	.empty-state {
		background: #1a1e2f;
		border-radius: 16px;
		padding: 60px 40px;
		text-align: center;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.35);
	}

	.empty-icon {
		font-size: 64px;
		margin-bottom: 16px;
	}

	.empty-state h3 {
		margin: 0 0 8px;
		color: #eef0f8;
	}

	.empty-state p {
		margin: 0 0 24px;
		color: #8890b1;
	}

	.backup-list {
		background: #1a1e2f;
		border-radius: 16px;
		padding: 20px;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.35);
		overflow-x: auto;
	}

	.job-card {
		background: #161b2c;
		border-radius: 16px;
		padding: 18px 20px;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.35);
		border: 1px solid #2a2f47;
	}

	.job-header {
		display: flex;
		justify-content: space-between;
		align-items: center;
		margin-bottom: 12px;
	}

	.job-header h3 {
		margin: 0;
		font-size: 16px;
	}

	.job-status {
		font-size: 12px;
		text-transform: uppercase;
		letter-spacing: 0.08em;
		color: #98a2c9;
	}

	.job-details {
		display: flex;
		flex-direction: column;
		gap: 8px;
	}

	.job-progress {
		height: 8px;
		background: #252a3d;
		border-radius: 999px;
		overflow: hidden;
	}

	.job-progress-bar {
		height: 100%;
		background: linear-gradient(90deg, #5865f2, #45c6f7);
		transition: width 0.2s ease;
	}

	.job-message {
		margin: 0;
		color: #d4d9f1;
		font-size: 14px;
	}

	.job-meta {
		margin: 0;
		color: #8e96bb;
		font-size: 12px;
	}

	.job-error {
		margin: 0;
		color: #ff9f9f;
		font-size: 12px;
	}

	table {
		width: 100%;
		border-collapse: collapse;
	}

	th,
	td {
		padding: 12px 16px;
		text-align: left;
		border-bottom: 1px solid #2a2f47;
	}

	th {
		font-size: 12px;
		text-transform: uppercase;
		letter-spacing: 0.12em;
		color: #8890b1;
		font-weight: 600;
	}

	td {
		color: #eef0f8;
	}

	tr:last-child td {
		border-bottom: none;
	}

	.badge {
		display: inline-block;
		padding: 4px 10px;
		border-radius: 12px;
		font-size: 12px;
		font-weight: 500;
		background: rgba(88, 101, 242, 0.15);
		color: #5865f2;
	}

	.btn-action {
		background: #2b2f45;
		color: #d4d9f1;
		border: none;
		border-radius: 6px;
		padding: 6px 14px;
		font-family: inherit;
		font-size: 13px;
		font-weight: 500;
		cursor: pointer;
		transition: background 0.2s;
	}

	.btn-action:hover:not(:disabled) {
		background: #3a3f5a;
	}

	.btn-action:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}


	@media (max-width: 640px) {
		.page-header {
			flex-direction: column;
			align-items: flex-start;
		}

		.action-buttons {
			width: 100%;
			flex-direction: column;
		}

		.action-buttons button {
			width: 100%;
		}
	}
</style>
