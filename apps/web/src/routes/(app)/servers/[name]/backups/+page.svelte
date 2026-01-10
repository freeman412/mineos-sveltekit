<script lang="ts">
	import { invalidateAll } from '$app/navigation';
	import type { PageData } from './$types';
	import type { LayoutData } from '../$types';

	let { data }: { data: PageData & { server: LayoutData['server'] } } = $props();

	let loading = $state(false);
	let actionLoading = $state<Record<string, boolean>>({});
	let backups = $state<any[]>([]);

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
				await loadBackups();
			} else {
				const errorData = await res.json().catch(() => ({}));
				alert(`Failed to create backup: ${errorData.error || res.statusText}`);
			}
		} catch (err) {
			alert(`Error: ${err instanceof Error ? err.message : 'Unknown error'}`);
		} finally {
			loading = false;
		}
	}

	async function restoreBackup(timestamp: string) {
		if (!data.server) return;
		if (!confirm(`Are you sure you want to restore from ${formatDate(timestamp)}? This will overwrite current server files.`)) {
			return;
		}

		actionLoading[timestamp] = true;
		try {
			const res = await fetch(`/api/servers/${data.server.name}/backups/restore`, {
				method: 'POST',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify({ timestamp })
			});

			if (res.ok) {
				alert('Server restored successfully');
				await loadBackups();
			} else {
				const errorData = await res.json().catch(() => ({}));
				alert(`Failed to restore backup: ${errorData.error || res.statusText}`);
			}
		} catch (err) {
			alert(`Error: ${err instanceof Error ? err.message : 'Unknown error'}`);
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
				alert(`Failed to prune backups: ${errorData.error || res.statusText}`);
			}
		} catch (err) {
			alert(`Error: ${err instanceof Error ? err.message : 'Unknown error'}`);
		} finally {
			loading = false;
		}
	}

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
