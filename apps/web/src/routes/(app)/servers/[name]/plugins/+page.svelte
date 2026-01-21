<script lang="ts">
	import { modal } from '$lib/stores/modal';
	import { formatBytes, formatDate } from '$lib/utils/formatting';
	import ModrinthPluginSearch from '$lib/components/ModrinthPluginSearch.svelte';
	import type { PageData } from './$types';
	import type { LayoutData } from '../$layout';
	import type { InstalledPlugin } from '$lib/api/types';

	let { data }: { data: PageData & { server: LayoutData['server'] } } = $props();

	let plugins = $state<InstalledPlugin[]>([]);
	let loading = $state(false);
	let uploading = $state(false);
	let isDragging = $state(false);
	let uploadProgress = $state(0);
	let uploadingFileName = $state('');
	let toggling = $state<Record<string, boolean>>({});
	let deleting = $state<Record<string, boolean>>({});

	const isServerRunning = $derived(data.server?.status === 'running');

	$effect(() => {
		loadPlugins();
	});

	async function loadPlugins() {
		if (!data.server) return;
		loading = true;
		try {
			const res = await fetch(`/api/servers/${data.server.name}/plugins`);
			if (res.ok) {
				plugins = await res.json();
			}
		} catch (err) {
			console.error('Failed to load plugins:', err);
		} finally {
			loading = false;
		}
	}

	function displayName(fileName: string) {
		return fileName.replace(/\.disabled$/i, '');
	}

	async function uploadPluginFile(file: File) {
		if (!data.server) return;
		const fileName = file.name.toLowerCase();
		if (!fileName.endsWith('.jar')) {
			await modal.error(`Only .jar plugin files are supported. Got: ${file.name}`);
			return;
		}

		uploading = true;
		uploadingFileName = file.name;
		uploadProgress = 0;

		try {
			const form = new FormData();
			form.append('file', file);

			const xhr = new XMLHttpRequest();
			const uploadPromise = new Promise((resolve, reject) => {
				xhr.upload.addEventListener('progress', (e) => {
					if (e.lengthComputable) {
						uploadProgress = Math.round((e.loaded / e.total) * 100);
					}
				});

				xhr.addEventListener('load', () => {
					if (xhr.status >= 200 && xhr.status < 300) {
						resolve(xhr.response);
					} else {
						reject(new Error(`Upload failed: ${xhr.statusText}`));
					}
				});

				xhr.addEventListener('error', () => reject(new Error('Upload failed')));
				xhr.addEventListener('abort', () => reject(new Error('Upload cancelled')));

				xhr.open('POST', `/api/servers/${data.server.name}/plugins/upload`);
				xhr.send(form);
			});

			await uploadPromise;
			await loadPlugins();
		} catch (err) {
			await modal.error(err instanceof Error ? err.message : `Upload failed for ${file.name}`);
		} finally {
			uploading = false;
			uploadProgress = 0;
			uploadingFileName = '';
		}
	}

	async function handleUpload(event: Event) {
		const input = event.target as HTMLInputElement;
		const files = Array.from(input.files || []);
		if (files.length === 0) return;

		for (const file of files) {
			await uploadPluginFile(file);
		}
		input.value = '';
	}

	async function handleDrop(event: DragEvent) {
		event.preventDefault();
		isDragging = false;
		const files = Array.from(event.dataTransfer?.files || []);
		if (files.length === 0) return;

		for (const file of files) {
			await uploadPluginFile(file);
		}
	}

	async function togglePlugin(plugin: InstalledPlugin) {
		if (!data.server) return;
		const action = plugin.isDisabled ? 'enable' : 'disable';
		toggling[plugin.fileName] = true;
		toggling = { ...toggling };

		try {
			const res = await fetch(
				`/api/servers/${data.server.name}/plugins/${encodeURIComponent(plugin.fileName)}/${action}`,
				{ method: 'POST' }
			);
			if (!res.ok) {
				const error = await res.json().catch(() => ({ error: 'Failed to update plugin' }));
				await modal.error(error.error || 'Failed to update plugin');
			} else {
				await loadPlugins();
			}
		} finally {
			delete toggling[plugin.fileName];
			toggling = { ...toggling };
		}
	}

	async function deletePlugin(plugin: InstalledPlugin) {
		if (!data.server) return;
		const confirmed = await modal.confirm(
			`Delete plugin "${displayName(plugin.fileName)}"? This cannot be undone.`,
			'Delete Plugin'
		);
		if (!confirmed) return;

		deleting[plugin.fileName] = true;
		deleting = { ...deleting };

		try {
			const res = await fetch(
				`/api/servers/${data.server.name}/plugins/${encodeURIComponent(plugin.fileName)}`,
				{ method: 'DELETE' }
			);
			if (!res.ok) {
				const error = await res.json().catch(() => ({ error: 'Failed to delete plugin' }));
				await modal.error(error.error || 'Failed to delete plugin');
			} else {
				await loadPlugins();
			}
		} finally {
			delete deleting[plugin.fileName];
			deleting = { ...deleting };
		}
	}
</script>

<div class="page">
	<div class="page-header">
		<div>
			<h2>Plugins</h2>
			<p class="subtitle">Upload and manage server-side plugins.</p>
		</div>
		<div class="action-buttons">
			<label class="upload-button">
				<input type="file" accept=".jar" multiple onchange={handleUpload} disabled={uploading} />
				{uploading ? 'Uploading...' : 'Upload Plugins'}
			</label>
		</div>
	</div>

	<div class="notice">
		<p>
			Changes apply after a restart. {isServerRunning ? 'Server is currently running.' : 'Server is stopped.'}
		</p>
	</div>

	<!-- svelte-ignore a11y_no_static_element_interactions -->
	<div
		class="upload-drop"
		class:active={isDragging}
		ondragover={(event) => {
			event.preventDefault();
			isDragging = true;
		}}
		ondragleave={() => {
			isDragging = false;
		}}
		ondrop={handleDrop}
	>
		<div class="drop-content">
			<strong>Drag & drop</strong> plugin jars here, or use Upload Plugins.
		</div>
	</div>

	{#if uploading}
		<div class="upload-progress-container">
			<div class="progress-header">
				<span class="progress-title">Uploading: {uploadingFileName}</span>
			</div>
			<div class="progress-bar">
				<div class="progress-fill" style={`width: ${uploadProgress}%`}></div>
			</div>
		</div>
	{/if}

	<div class="plugins-section">
		<h3>Installed Plugins</h3>
		{#if loading}
			<p class="muted">Loading plugins...</p>
		{:else if plugins.length === 0}
			<p class="muted">No plugins installed yet.</p>
		{:else}
			<div class="plugin-list">
				<table>
					<thead>
						<tr>
							<th>Name</th>
							<th>Status</th>
							<th>Size</th>
							<th>Modified</th>
							<th>Actions</th>
						</tr>
					</thead>
					<tbody>
						{#each plugins as plugin}
							<tr class:disabled={plugin.isDisabled}>
								<td>{displayName(plugin.fileName)}</td>
								<td>
									<span class={`state ${plugin.isDisabled ? 'disabled' : 'enabled'}`}>
										{plugin.isDisabled ? 'Disabled' : 'Enabled'}
									</span>
								</td>
								<td>{formatBytes(plugin.sizeBytes)}</td>
								<td>{formatDate(plugin.modifiedAt)}</td>
								<td class="actions">
									<a
										class="btn-action"
										href={`/api/servers/${data.server?.name}/plugins/${encodeURIComponent(
											plugin.fileName
										)}/download`}
										download={plugin.fileName}
									>
										Download
									</a>
									<button
										class="btn-action"
										onclick={() => togglePlugin(plugin)}
										disabled={toggling[plugin.fileName]}
									>
										{plugin.isDisabled ? 'Enable' : 'Disable'}
									</button>
									<button
										class="btn-action danger"
										onclick={() => deletePlugin(plugin)}
										disabled={deleting[plugin.fileName]}
									>
										{deleting[plugin.fileName] ? 'Deleting...' : 'Delete'}
									</button>
								</td>
							</tr>
						{/each}
					</tbody>
				</table>
			</div>
		{/if}
	</div>

	<div class="modrinth-section">
		<h3>Install from Modrinth</h3>
		<ModrinthPluginSearch serverName={data.server?.name ?? ''} onInstallComplete={loadPlugins} />
	</div>
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

	.notice {
		background: rgba(106, 176, 76, 0.12);
		border: 1px solid rgba(106, 176, 76, 0.25);
		border-radius: 12px;
		padding: 12px 16px;
		color: #cfe9c3;
	}

	.notice p {
		margin: 0;
		font-size: 13px;
	}

	.upload-drop {
		border: 2px dashed #2a2f47;
		border-radius: 16px;
		padding: 18px;
		text-align: center;
		color: #9aa2c5;
		background: rgba(20, 24, 39, 0.6);
		transition: border-color 0.2s, background 0.2s, color 0.2s;
	}

	.upload-drop.active {
		border-color: #7ae68d;
		background: rgba(122, 230, 141, 0.08);
		color: #d4f5dc;
	}

	.drop-content strong {
		color: #eef0f8;
	}

	.action-buttons {
		display: flex;
		gap: 12px;
	}

	.upload-button {
		background: var(--mc-grass);
		color: white;
		border-radius: 8px;
		padding: 10px 20px;
		font-size: 14px;
		font-weight: 600;
		cursor: pointer;
	}

	.upload-button input {
		display: none;
	}

	.upload-progress-container {
		background: #1a1e2f;
		border-radius: 12px;
		padding: 16px 20px;
		box-shadow: 0 10px 20px rgba(0, 0, 0, 0.25);
	}

	.progress-header {
		margin-bottom: 10px;
	}

	.progress-title {
		color: #eef0f8;
		font-size: 14px;
		font-weight: 600;
	}

	.progress-bar {
		background: #2a2f47;
		border-radius: 6px;
		height: 6px;
		overflow: hidden;
	}

	.progress-fill {
		background: var(--mc-grass);
		height: 100%;
		transition: width 0.2s ease;
	}

	.plugins-section {
		background: #1a1e2f;
		border-radius: 16px;
		padding: 20px;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.35);
		display: flex;
		flex-direction: column;
		gap: 16px;
	}

	.modrinth-section {
		display: flex;
		flex-direction: column;
		gap: 16px;
	}

	.plugin-list table {
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

	tr.disabled td {
		opacity: 0.6;
	}

	.actions {
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
		text-decoration: none;
	}

	.btn-action.danger {
		background: rgba(255, 92, 92, 0.2);
		color: #ff9f9f;
	}

	.btn-action:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.state {
		padding: 4px 10px;
		border-radius: 10px;
		font-size: 12px;
		font-weight: 600;
	}

	.state.enabled {
		background: rgba(106, 176, 76, 0.18);
		color: #b7f5a2;
	}

	.state.disabled {
		background: rgba(255, 92, 92, 0.15);
		color: #ff9f9f;
	}

	.muted {
		color: #8890b1;
		margin: 0;
	}

	@media (max-width: 720px) {
		.actions {
			flex-wrap: wrap;
		}

		th:nth-child(3),
		td:nth-child(3),
		th:nth-child(4),
		td:nth-child(4) {
			display: none;
		}
	}
</style>
