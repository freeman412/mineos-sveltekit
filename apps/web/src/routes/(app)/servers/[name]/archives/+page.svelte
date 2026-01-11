<script lang="ts">
	import type { PageData } from './$types';
	import type { LayoutData } from '../$types';
	import { modal } from '$lib/stores/modal';

	let { data }: { data: PageData & { server: LayoutData['server'] } } = $props();

	let loading = $state(false);
	let actionLoading = $state<Record<string, boolean>>({});
	let archives = $state<any[]>([]);

	$effect(() => {
		loadArchives();
	});

	async function loadArchives() {
		if (!data.server) return;
		try {
			const res = await fetch(`/api/servers/${data.server.name}/archives`);
			if (res.ok) {
				archives = await res.json();
			}
		} catch (err) {
			console.error('Failed to load archives:', err);
		}
	}

	async function createArchive() {
		if (!data.server) return;
		loading = true;
		try {
			const res = await fetch(`/api/servers/${data.server.name}/archives`, { method: 'POST' });
			if (res.ok) {
				await loadArchives();
			} else {
				const errorData = await res.json().catch(() => ({}));
				await modal.error(`Failed to create archive: ${errorData.error || res.statusText}`);
			}
		} catch (err) {
			await modal.error(`Error: ${err instanceof Error ? err.message : 'Unknown error'}`);
		} finally {
			loading = false;
		}
	}

	async function deleteArchive(filename: string) {
		if (!data.server) return;
		const confirmed = await modal.confirm(`Are you sure you want to delete archive "${filename}"?`, 'Delete Archive');
		if (!confirmed) return;

		actionLoading[filename] = true;
		try {
			const res = await fetch(`/api/servers/${data.server.name}/archives/${filename}`, {
				method: 'DELETE'
			});

			if (res.ok) {
				await loadArchives();
			} else {
				const errorData = await res.json().catch(() => ({}));
				await modal.error(`Failed to delete archive: ${errorData.error || res.statusText}`);
			}
		} catch (err) {
			await modal.error(`Error: ${err instanceof Error ? err.message : 'Unknown error'}`);
		} finally {
			delete actionLoading[filename];
			actionLoading = { ...actionLoading };
		}
	}

	function downloadArchive(filename: string) {
		if (!data.server) return;
		window.location.href = `/api/servers/${data.server.name}/archives/${filename}/download`;
	}

	const formatDate = (dateStr: string) => {
		const date = new Date(dateStr);
		return date.toLocaleDateString() + ' ' + date.toLocaleTimeString();
	};

	const formatBytes = (bytes: number) => {
		const units = ['B', 'KB', 'MB', 'GB', 'TB'];
		const i = Math.floor(Math.log(bytes) / Math.log(1024));
		return `${(bytes / Math.pow(1024, i)).toFixed(1)} ${units[i]}`;
	};
</script>

<div class="page">
	<div class="page-header">
		<div>
			<h2>Archives</h2>
			<p class="subtitle">Compressed server archives (.tar.gz)</p>
		</div>
		<button class="btn-primary" onclick={createArchive} disabled={loading}>
			{loading ? 'Creating...' : '+ Create Archive'}
		</button>
	</div>

	{#if archives.length === 0}
		<div class="empty-state">
			<p class="empty-icon">📦</p>
			<h3>No archives yet</h3>
			<p>Create an archive to download or backup your entire server</p>
			<button class="btn-primary" onclick={createArchive} disabled={loading}>Create Archive</button>
		</div>
	{:else}
		<div class="archive-list">
			<table>
				<thead>
					<tr>
						<th>Filename</th>
						<th>Created</th>
						<th>Size</th>
						<th>Actions</th>
					</tr>
				</thead>
				<tbody>
					{#each archives as archive}
						<tr>
							<td>{archive.filename}</td>
							<td>{formatDate(archive.time)}</td>
							<td>{formatBytes(archive.size)}</td>
							<td>
								<div class="action-buttons">
									<button
										class="btn-action btn-download"
										onclick={() => downloadArchive(archive.filename)}
									>
										Download
									</button>
									<button
										class="btn-action btn-danger"
										onclick={() => deleteArchive(archive.filename)}
										disabled={actionLoading[archive.filename]}
									>
										{actionLoading[archive.filename] ? 'Deleting...' : 'Delete'}
									</button>
								</div>
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

	.archive-list {
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

	.action-buttons {
		display: flex;
		gap: 8px;
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

	.btn-download {
		background: rgba(122, 230, 141, 0.15);
		color: #7ae68d;
	}

	.btn-download:hover:not(:disabled) {
		background: rgba(122, 230, 141, 0.25);
	}

	.btn-danger {
		background: rgba(255, 92, 92, 0.15);
		color: #ff9f9f;
	}

	.btn-danger:hover:not(:disabled) {
		background: rgba(255, 92, 92, 0.25);
	}

	@media (max-width: 640px) {
		.page-header {
			flex-direction: column;
			align-items: flex-start;
		}

		.page-header button {
			width: 100%;
		}
	}
</style>
