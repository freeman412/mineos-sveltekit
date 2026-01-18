<script lang="ts">
	import { invalidateAll } from '$app/navigation';
	import * as api from '$lib/api/client';
	import { modal } from '$lib/stores/modal';
	import { formatBytes, formatDate } from '$lib/utils/formatting';
	import ProgressBar from '$lib/components/ProgressBar.svelte';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	let deleting = $state<string | null>(null);
	let downloading = $state<string | null>(null);
	let uploading = $state<string | null>(null);
	let uploadProgress = $state<number>(0);
	let confirmDelete: string | null = $state(null);
	let uploadingNew = $state(false);
	let uploadNewProgress = $state<number>(0);

	const isServerRunning = $derived(data.server?.status === 'running');

	async function handleDownload(worldName: string) {
		downloading = worldName;
		try {
			await api.downloadWorld(fetch, data.server.name, worldName);
		} catch (err) {
			await modal.error(err instanceof Error ? err.message : 'Failed to download world');
		} finally {
			downloading = null;
		}
	}

	async function handleDelete(worldName: string) {
		if (isServerRunning) {
			await modal.error('Cannot delete worlds while server is running. Please stop the server first.');
			return;
		}

		if (confirmDelete !== worldName) {
			confirmDelete = worldName;
			return;
		}

		deleting = worldName;
		try {
			const result = await api.deleteWorld(fetch, data.server.name, worldName);
			if (result.error) {
				await modal.error(result.error);
			} else {
				await invalidateAll();
			}
		} catch (err) {
			await modal.error(err instanceof Error ? err.message : 'Failed to delete world');
		} finally {
			deleting = null;
			confirmDelete = null;
		}
	}

	async function handleUpload(worldName: string) {
		if (isServerRunning) {
			await modal.error('Cannot upload worlds while server is running. Please stop the server first.');
			return;
		}

		const confirmed = await modal.confirm(
			`Upload a new world to replace "${worldName}"? This will permanently delete the existing world folder.`,
			'Replace World'
		);
		if (!confirmed) return;

		const input = document.createElement('input');
		input.type = 'file';
		input.accept = '.zip';
		input.onchange = async (e) => {
			const file = (e.target as HTMLInputElement).files?.[0];
			if (!file) return;

			if (!file.name.endsWith('.zip')) {
				await modal.error('Only ZIP files are supported');
				return;
			}

			uploading = worldName;
			uploadProgress = 0;

			try {
				const xhr = new XMLHttpRequest();
				const uploadPromise = new Promise<void>((resolve, reject) => {
					xhr.upload.addEventListener('progress', (e) => {
						if (e.lengthComputable) {
							uploadProgress = Math.round((e.loaded / e.total) * 100);
						}
					});

					xhr.addEventListener('load', () => {
						if (xhr.status >= 200 && xhr.status < 300) {
							resolve();
						} else {
							try {
								const errorData = JSON.parse(xhr.responseText);
								reject(new Error(errorData.error || `Upload failed with status ${xhr.status}`));
							} catch {
								reject(new Error(`Upload failed with status ${xhr.status}`));
							}
						}
					});

					xhr.addEventListener('error', () => reject(new Error('Network error during upload')));
					xhr.addEventListener('abort', () => reject(new Error('Upload cancelled')));

					const formData = new FormData();
					formData.append('file', file);

					xhr.open('POST', `/api/servers/${data.server.name}/worlds/${worldName}/upload`);
					xhr.send(formData);
				});

				await uploadPromise;
				await invalidateAll();
			} catch (err) {
				await modal.error(err instanceof Error ? err.message : 'Failed to upload world');
			} finally {
				uploading = null;
				uploadProgress = 0;
			}
		};
		input.click();
	}

	async function handleUploadNew() {
		if (isServerRunning) {
			await modal.error('Cannot upload worlds while server is running. Please stop the server first.');
			return;
		}

		const input = document.createElement('input');
		input.type = 'file';
		input.accept = '.zip';
		input.onchange = async (e) => {
			const file = (e.target as HTMLInputElement).files?.[0];
			if (!file) return;

			if (!file.name.endsWith('.zip')) {
				await modal.error('Only ZIP files are supported');
				return;
			}

			uploadingNew = true;
			uploadNewProgress = 0;

			try {
				const xhr = new XMLHttpRequest();
				const uploadPromise = new Promise<void>((resolve, reject) => {
					xhr.upload.addEventListener('progress', (e) => {
						if (e.lengthComputable) {
							uploadNewProgress = Math.round((e.loaded / e.total) * 100);
						}
					});

					xhr.addEventListener('load', () => {
						if (xhr.status >= 200 && xhr.status < 300) {
							resolve();
						} else {
							try {
								const errorData = JSON.parse(xhr.responseText);
								reject(new Error(errorData.error || `Upload failed with status ${xhr.status}`));
							} catch {
								reject(new Error(`Upload failed with status ${xhr.status}`));
							}
						}
					});

					xhr.addEventListener('error', () => reject(new Error('Network error during upload')));
					xhr.addEventListener('abort', () => reject(new Error('Upload cancelled')));

					const formData = new FormData();
					formData.append('file', file);

					xhr.open('POST', `/api/servers/${data.server.name}/worlds/upload`);
					xhr.send(formData);
				});

				await uploadPromise;
				await invalidateAll();
			} catch (err) {
				await modal.error(err instanceof Error ? err.message : 'Failed to upload world');
			} finally {
				uploadingNew = false;
				uploadNewProgress = 0;
			}
		};
		input.click();
	}
</script>

<div class="worlds-page">
	<header class="page-header">
		<div>
			<h2>World Management</h2>
			<p class="subtitle">Manage your Minecraft world folders</p>
		</div>
		<button
			class="btn-primary"
			onclick={handleUploadNew}
			disabled={uploadingNew || isServerRunning}
			title={isServerRunning ? 'Stop server first' : 'Upload world from ZIP'}
		>
			{#if uploadingNew}
				📤 Uploading... {uploadNewProgress}%
			{:else}
				📤 Upload World
			{/if}
		</button>
	</header>

	{#if isServerRunning}
		<div class="warning-banner">
			<div class="warning-icon">⚠️</div>
			<div class="warning-content">
				<strong>Server is running</strong>
				<p>Upload and delete operations are disabled while the server is running. Stop the server to modify worlds.</p>
			</div>
		</div>
	{/if}

	{#if data.worlds.error}
		<div class="empty-state error">
			<div class="empty-icon">❌</div>
			<h3>Error Loading Worlds</h3>
			<p>{data.worlds.error}</p>
		</div>
	{:else if !data.worlds.data || data.worlds.data.length === 0}
		<div class="empty-state">
			<div class="empty-icon">🌍</div>
			<h3>No Worlds Found</h3>
			<p>
				No world folders detected. Upload a world ZIP file or start the server to generate new worlds.
			</p>
			<button
				class="btn-primary"
				onclick={handleUploadNew}
				disabled={uploadingNew || isServerRunning}
				title={isServerRunning ? 'Stop server first' : 'Upload world from ZIP'}
			>
				{#if uploadingNew}
					📤 Uploading... {uploadNewProgress}%
				{:else}
					📤 Upload World
				{/if}
			</button>
			{#if uploadingNew && uploadNewProgress > 0}
				<div class="upload-progress-bar">
					<ProgressBar value={uploadNewProgress} color="green" size="sm" />
				</div>
			{/if}
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
							disabled={downloading === world.name || uploading === world.name}
						>
							{downloading === world.name ? '⏳ Downloading...' : '💾 Backup'}
						</button>

						<button
							class="action-btn upload"
							onclick={() => handleUpload(world.name)}
							disabled={uploading === world.name || isServerRunning}
							title={isServerRunning ? 'Stop server first' : 'Replace world from ZIP'}
						>
							{#if uploading === world.name}
								📤 Uploading... {uploadProgress}%
							{:else}
								📤 Replace
							{/if}
						</button>

						<button
							class="action-btn delete"
							onclick={() => handleDelete(world.name)}
							disabled={deleting === world.name || isServerRunning}
							title={isServerRunning ? 'Stop server first' : 'Delete world'}
						>
							{#if confirmDelete === world.name}
								{deleting === world.name ? '⏳ Deleting...' : '⚠️ Confirm?'}
							{:else}
								🗑️ Delete
							{/if}
						</button>
					</div>

					{#if uploading === world.name && uploadProgress > 0}
						<div class="upload-progress">
							<ProgressBar value={uploadProgress} color="green" size="sm" />
						</div>
					{/if}
				</div>
			{/each}
		</div>

		<div class="info-box">
			<h4>ℹ️ World Management Tips</h4>
			<ul>
				<li><strong>Upload New:</strong> Upload a world ZIP file to add a new world to the server (auto-detects world name from ZIP structure)</li>
				<li><strong>Backup:</strong> Downloads a ZIP archive of the world folder for safekeeping</li>
				<li><strong>Replace:</strong> Upload a ZIP file to replace an existing world (server must be stopped)</li>
				<li>
					<strong>Delete:</strong> Permanently removes the world folder (server will regenerate on
					next start)
				</li>
				<li><strong>Warning:</strong> Always stop the server before uploading or deleting worlds</li>
				<li>
					<strong>Vanilla Servers:</strong> Use separate world folders named "world", "world_nether", and "world_the_end"
				</li>
				<li>
					<strong>Paper/Spigot:</strong> Use a single "world" folder containing DIM-1 (Nether) and DIM1 (End) subfolders
				</li>
				<li>
					<strong>World Types:</strong> Overworld (🌍), Nether (🔥), The End (🌌), Custom (📁)
				</li>
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
		display: flex;
		justify-content: space-between;
		align-items: center;
		gap: 24px;
		margin-bottom: 8px;
	}

	.page-header h2 {
		margin: 0 0 8px;
		font-size: 28px;
		font-weight: 600;
		color: #eef0f8;
	}

	.btn-primary {
		background: var(--mc-grass);
		color: #fff;
		border: none;
		border-radius: 8px;
		padding: 12px 24px;
		font-size: 14px;
		font-weight: 600;
		cursor: pointer;
		transition: all 0.2s;
		font-family: inherit;
		white-space: nowrap;
		flex-shrink: 0;
	}

	.btn-primary:hover:not(:disabled) {
		background: var(--mc-grass-dark);
	}

	.btn-primary:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.subtitle {
		margin: 0;
		color: #9aa2c5;
		font-size: 14px;
	}

	.warning-banner {
		display: flex;
		align-items: flex-start;
		gap: 16px;
		padding: 16px 20px;
		background: rgba(255, 183, 77, 0.08);
		border: 1px solid rgba(255, 183, 77, 0.3);
		border-radius: 12px;
		margin-bottom: 24px;
	}

	.warning-icon {
		font-size: 24px;
		flex-shrink: 0;
	}

	.warning-content {
		flex: 1;
	}

	.warning-content strong {
		display: block;
		color: #ffcf89;
		font-size: 15px;
		margin-bottom: 4px;
	}

	.warning-content p {
		margin: 0;
		color: #c9a877;
		font-size: 14px;
		line-height: 1.5;
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
		margin: 0 0 24px;
		color: #9aa2c5;
		font-size: 14px;
		max-width: 500px;
	}

	.upload-progress-bar {
		margin-top: 16px;
		width: 100%;
		max-width: 400px;
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
		display: grid;
		grid-template-columns: repeat(3, 1fr);
		gap: 8px;
		margin-top: 8px;
	}

	.action-btn {
		padding: 10px 12px;
		border-radius: 8px;
		border: none;
		font-size: 13px;
		font-weight: 500;
		cursor: pointer;
		transition: all 0.2s;
		font-family: inherit;
		white-space: nowrap;
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

	.action-btn.upload {
		background: rgba(111, 181, 255, 0.15);
		color: #a6d5fa;
		border: 1px solid rgba(111, 181, 255, 0.35);
	}

	.action-btn.upload:hover:not(:disabled) {
		background: rgba(111, 181, 255, 0.25);
	}

	.action-btn.delete {
		background: rgba(234, 85, 83, 0.15);
		color: #ff9a98;
		border: 1px solid rgba(234, 85, 83, 0.35);
	}

	.action-btn.delete:hover:not(:disabled) {
		background: rgba(234, 85, 83, 0.25);
	}

	.upload-progress {
		margin-top: 12px;
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
		.page-header {
			flex-direction: column;
			align-items: flex-start;
		}

		.worlds-grid {
			grid-template-columns: 1fr;
		}

		.world-actions {
			flex-direction: column;
		}
	}
</style>
