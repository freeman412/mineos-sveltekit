<script lang="ts">
	import { invalidateAll } from '$app/navigation';
	import * as api from '$lib/api/client';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	let deleting = $state<string | null>(null);
	let downloading = $state<string | null>(null);
	let confirmDelete: string | null = $state(null);

	function formatBytes(bytes: number): string {
		if (bytes === 0) return '0 B';
		const k = 1024;
		const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
		const i = Math.floor(Math.log(bytes) / Math.log(k));
		return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
	}

	function formatDate(dateStr: string | null): string {
		if (!dateStr) return 'Never';
		return new Date(dateStr).toLocaleString();
	}

	async function handleDownload(worldName: string) {
		downloading = worldName;
		try {
			await api.downloadWorld(fetch, data.server.name, worldName);
		} catch (err) {
			alert(err instanceof Error ? err.message : 'Failed to download world');
		} finally {
			downloading = null;
		}
	}

	async function handleDelete(worldName: string) {
		if (confirmDelete !== worldName) {
			confirmDelete = worldName;
			return;
		}

		deleting = worldName;
		try {
			const result = await api.deleteWorld(fetch, data.server.name, worldName);
			if (result.error) {
				alert(result.error);
			} else {
				await invalidateAll();
			}
		} catch (err) {
			alert(err instanceof Error ? err.message : 'Failed to delete world');
		} finally {
			deleting = null;
			confirmDelete = null;
		}
	}
</script>

<div class="worlds-page">
	<header class="page-header">
		<h2>World Management</h2>
		<p class="subtitle">Manage your Minecraft world folders</p>
	</header>

	{#if !data.worlds.data || data.worlds.data.length === 0}
		<div class="empty-state">
			<div class="empty-icon">🌍</div>
			<h3>No Worlds Found</h3>
			<p>
				No world folders detected. Worlds will appear here after the server generates them on first
				start.
			</p>
		</div>
	{:else}
		<div class="worlds-grid">
			{#each data.worlds.data as world}
				<div class="world-card">
					<div class="world-icon">
						{#if world.type === 'Overworld'}
							🌍
						{:else if world.type === 'Nether'}
							🔥
						{:else if world.type === 'The End'}
							🌌
						{:else}
							📁
						{/if}
					</div>

					<div class="world-info">
						<h3>{world.type}</h3>
						<p class="world-name">{world.name}</p>

						<div class="world-stats">
							<div class="stat">
								<span class="stat-label">Size:</span>
								<span class="stat-value">{formatBytes(world.sizeBytes)}</span>
							</div>
							<div class="stat">
								<span class="stat-label">Modified:</span>
								<span class="stat-value">{formatDate(world.lastModified)}</span>
							</div>
						</div>
					</div>

					<div class="world-actions">
						<button
							class="action-btn download"
							onclick={() => handleDownload(world.name)}
							disabled={downloading === world.name}
						>
							{downloading === world.name ? '⏳ Downloading...' : '📥 Download'}
						</button>

						<button
							class="action-btn delete"
							onclick={() => handleDelete(world.name)}
							disabled={deleting === world.name}
						>
							{#if confirmDelete === world.name}
								{deleting === world.name ? '⏳ Deleting...' : '⚠️ Confirm Delete?'}
							{:else}
								🗑️ Delete
							{/if}
						</button>
					</div>
				</div>
			{/each}
		</div>

		<div class="info-box">
			<h4>ℹ️ World Management Tips</h4>
			<ul>
				<li><strong>Download:</strong> Creates a ZIP archive of the world folder for backup</li>
				<li>
					<strong>Delete:</strong> Permanently removes the world folder (server will regenerate on
					next start)
				</li>
				<li>
					<strong>Upload:</strong> Upload functionality coming soon - manually copy worlds to server
					directory for now
				</li>
				<li><strong>Warning:</strong> Always stop the server before deleting or replacing worlds</li>
			</ul>
		</div>
	{/if}
</div>

<style>
	.worlds-page {
		display: flex;
		flex-direction: column;
		gap: 24px;
	}

	.page-header {
		margin-bottom: 8px;
	}

	.page-header h2 {
		margin: 0 0 8px;
		font-size: 28px;
		font-weight: 600;
		color: #eef0f8;
	}

	.subtitle {
		margin: 0;
		color: #9aa2c5;
		font-size: 14px;
	}

	.empty-state {
		text-align: center;
		padding: 80px 20px;
		background: linear-gradient(135deg, #1a1e2f 0%, #141827 100%);
		border-radius: 12px;
		border: 1px solid #2a2f47;
	}

	.empty-icon {
		font-size: 64px;
		margin-bottom: 20px;
	}

	.empty-state h3 {
		margin: 0 0 12px;
		font-size: 20px;
		color: #eef0f8;
	}

	.empty-state p {
		margin: 0;
		color: #9aa2c5;
		font-size: 14px;
		max-width: 500px;
		margin: 0 auto;
	}

	.worlds-grid {
		display: grid;
		grid-template-columns: repeat(auto-fill, minmax(350px, 1fr));
		gap: 20px;
	}

	.world-card {
		background: linear-gradient(135deg, #1a1e2f 0%, #141827 100%);
		border: 1px solid #2a2f47;
		border-radius: 12px;
		padding: 24px;
		display: flex;
		flex-direction: column;
		gap: 16px;
		transition: all 0.2s;
	}

	.world-card:hover {
		border-color: rgba(106, 176, 76, 0.4);
		box-shadow: 0 4px 12px rgba(106, 176, 76, 0.1);
	}

	.world-icon {
		font-size: 48px;
		text-align: center;
	}

	.world-info h3 {
		margin: 0 0 4px;
		font-size: 20px;
		color: #eef0f8;
		font-weight: 600;
	}

	.world-name {
		margin: 0 0 16px;
		color: #7c87b2;
		font-size: 13px;
		font-family: 'Cascadia Code', monospace;
	}

	.world-stats {
		display: flex;
		flex-direction: column;
		gap: 8px;
	}

	.stat {
		display: flex;
		justify-content: space-between;
		font-size: 14px;
	}

	.stat-label {
		color: #9aa2c5;
	}

	.stat-value {
		color: #eef0f8;
		font-weight: 500;
	}

	.world-actions {
		display: flex;
		gap: 12px;
		margin-top: 8px;
	}

	.action-btn {
		flex: 1;
		padding: 10px 16px;
		border-radius: 8px;
		border: none;
		font-size: 14px;
		font-weight: 500;
		cursor: pointer;
		transition: all 0.2s;
		font-family: inherit;
	}

	.action-btn:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.action-btn.download {
		background: rgba(106, 176, 76, 0.15);
		color: #b7f5a2;
		border: 1px solid rgba(106, 176, 76, 0.35);
	}

	.action-btn.download:hover:not(:disabled) {
		background: rgba(106, 176, 76, 0.25);
	}

	.action-btn.delete {
		background: rgba(234, 85, 83, 0.15);
		color: #ff9a98;
		border: 1px solid rgba(234, 85, 83, 0.35);
	}

	.action-btn.delete:hover:not(:disabled) {
		background: rgba(234, 85, 83, 0.25);
	}

	.info-box {
		background: rgba(111, 181, 255, 0.08);
		border: 1px solid rgba(111, 181, 255, 0.25);
		border-radius: 12px;
		padding: 20px 24px;
	}

	.info-box h4 {
		margin: 0 0 12px;
		font-size: 16px;
		color: #a6d5fa;
	}

	.info-box ul {
		margin: 0;
		padding-left: 20px;
		color: #9aa2c5;
		font-size: 14px;
		line-height: 1.8;
	}

	.info-box strong {
		color: #b7c7e8;
	}

	@media (max-width: 640px) {
		.worlds-grid {
			grid-template-columns: 1fr;
		}

		.world-actions {
			flex-direction: column;
		}
	}
</style>
