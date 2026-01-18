<script lang="ts">
	import CurseForgeSearch from '$lib/components/CurseForgeSearch.svelte';
	import { modal } from '$lib/stores/modal';
	import { formatBytes } from '$lib/utils/formatting';
	import type { PageData } from './$types';
	import type { LayoutData } from '../$layout';
	import type { InstalledModWithModpack, InstalledModpack } from '$lib/api/types';

	let { data }: { data: PageData & { server: LayoutData['server'] } } = $props();

	let mods = $state<InstalledModWithModpack[]>([]);
	let modpacks = $state<InstalledModpack[]>([]);
	let loading = $state(false);
	let uninstallingModpack = $state<number | null>(null);
	let uploading = $state(false);
	let isDragging = $state(false);
	let uploadProgress = $state(0);
	let uploadingFileName = $state('');
	let deletingAll = $state(false);

	const isServerRunning = $derived(data.server?.status === 'running');

	$effect(() => {
		loadMods();
	});

	async function loadMods() {
		if (!data.server) return;
		loading = true;
		try {
			// Load mods with modpack associations
			const modsRes = await fetch(`/api/servers/${data.server.name}/mods/with-modpacks`);
			if (modsRes.ok) {
				mods = await modsRes.json();
			}
			// Load installed modpacks
			const modpacksRes = await fetch(`/api/servers/${data.server.name}/modpacks`);
			if (modpacksRes.ok) {
				modpacks = await modpacksRes.json();
			}
		} catch (err) {
			console.error('Failed to load mods:', err);
		} finally {
			loading = false;
		}
	}

	async function uninstallModpack(modpackId: number, modpackName: string) {
		if (!data.server) return;

		// Check if server is running
		if (isServerRunning) {
			await modal.error('Cannot uninstall modpacks while server is running. Stop the server first.');
			return;
		}

		const confirmed = await modal.confirm(
			`Uninstall modpack "${modpackName}"? This will remove all ${modpacks.find(m => m.id === modpackId)?.modCount ?? 0} mods from this modpack.`,
			'Uninstall Modpack'
		);
		if (!confirmed) return;

		uninstallingModpack = modpackId;
		try {
			const res = await fetch(`/api/servers/${data.server.name}/modpacks/${modpackId}`, {
				method: 'DELETE'
			});
			if (!res.ok) {
				const payload = await res.json().catch(() => ({}));
				await modal.error(payload.error || 'Failed to uninstall modpack');
			} else {
				await loadMods();
			}
		} catch (err) {
			await modal.error(err instanceof Error ? err.message : 'Uninstall failed');
		} finally {
			uninstallingModpack = null;
		}
	}

	// Group mods by modpack
	const groupedMods = $derived.by(() => {
		const grouped: Record<number, InstalledModWithModpack[]> = {};
		const standalone: InstalledModWithModpack[] = [];

		for (const mod of mods) {
			if (mod.modpackId) {
				if (!grouped[mod.modpackId]) {
					grouped[mod.modpackId] = [];
				}
				grouped[mod.modpackId].push(mod);
			} else {
				standalone.push(mod);
			}
		}

		return { grouped, standalone };
	});

	async function uploadModFile(file: File) {
		if (!data.server) return;
		const fileName = file.name.toLowerCase();
		const validExtensions = ['.jar', '.zip', '.tar', '.tar.gz', '.tgz'];
		const isValid = validExtensions.some((ext) => fileName.endsWith(ext));

		if (!isValid) {
			await modal.error(`Only .jar, .zip, .tar, and .tar.gz files are supported. Got: ${file.name}`);
			return;
		}

		uploading = true;
		uploadingFileName = file.name;
		uploadProgress = 0;

		try {
			const form = new FormData();
			form.append('file', file);

			// Create XMLHttpRequest for progress tracking
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

				xhr.open('POST', `/api/servers/${data.server.name}/mods/upload`);
				xhr.send(form);
			});

			await uploadPromise;
			await loadMods();
		} catch (err) {
			await modal.error(err instanceof Error ? err.message : `Upload failed for ${file.name}`);
		} finally {
			uploading = false;
			uploadProgress = 0;
			uploadingFileName = '';
		}
	}

	async function deleteAllMods() {
		if (!data.server || isServerRunning) return;

		const standaloneMods = groupedMods.standalone;
		if (standaloneMods.length === 0) {
			await modal.error('No standalone mods to delete');
			return;
		}

		const confirmed = await modal.confirm(
			`Delete all ${standaloneMods.length} standalone mod(s) from server "${data.server.name}"? This cannot be undone.`,
			'Delete All Mods'
		);
		if (!confirmed) return;

		deletingAll = true;
		try {
			let successCount = 0;
			let failCount = 0;

			for (const mod of standaloneMods) {
				try {
					const res = await fetch(
						`/api/servers/${data.server.name}/mods/${encodeURIComponent(mod.fileName)}`,
						{ method: 'DELETE' }
					);
					if (res.ok) {
						successCount++;
					} else {
						failCount++;
					}
				} catch {
					failCount++;
				}
			}

			await loadMods();

			if (failCount > 0) {
				await modal.error(
					`Deleted ${successCount} mods, but ${failCount} failed to delete.`
				);
			}
		} catch (err) {
			await modal.error(err instanceof Error ? err.message : 'Delete all failed');
		} finally {
			deletingAll = false;
		}
	}

	async function handleUpload(event: Event) {
		const input = event.target as HTMLInputElement;
		const files = Array.from(input.files || []);
		if (files.length === 0) return;

		for (const file of files) {
			await uploadModFile(file);
		}
		input.value = '';
	}

	async function handleDrop(event: DragEvent) {
		event.preventDefault();
		isDragging = false;
		const files = Array.from(event.dataTransfer?.files || []);
		if (files.length === 0) return;

		for (const file of files) {
			await uploadModFile(file);
		}
	}

	async function deleteMod(fileName: string) {
		if (!data.server) return;

		// Check if server is running
		if (isServerRunning) {
			await modal.error('Cannot delete mods while server is running. Stop the server first.');
			return;
		}

		const confirmed = await modal.confirm(`Delete ${fileName}?`, 'Delete Mod');
		if (!confirmed) return;

		try {
			const res = await fetch(`/api/servers/${data.server.name}/mods/${encodeURIComponent(fileName)}`, {
				method: 'DELETE'
			});
			if (!res.ok) {
				const payload = await res.json().catch(() => ({}));
				await modal.error(payload.error || 'Failed to delete mod');
			} else {
				await loadMods();
			}
		} catch (err) {
			await modal.error(err instanceof Error ? err.message : 'Delete failed');
		}
	}

</script>

<div class="page">
	<div class="page-header">
		<div>
			<h2>Mods</h2>
			<p class="subtitle">Upload, manage, and install mods for this server</p>
		</div>
		<div class="action-buttons">
			{#if groupedMods.standalone.length > 0}
				<button
					class="delete-all-btn"
					onclick={deleteAllMods}
					disabled={deletingAll || isServerRunning}
					title={isServerRunning
						? 'Stop server to delete mods'
						: `Delete all ${groupedMods.standalone.length} standalone mods from this server`}
				>
					{deletingAll ? 'Deleting...' : `Delete All Mods (${groupedMods.standalone.length})`}
				</button>
			{/if}
			<label class="upload-button">
				<input
					type="file"
					accept=".jar,.zip,.tar,.tar.gz,.tgz"
					multiple
					onchange={handleUpload}
					disabled={uploading}
				/>
				{uploading ? 'Uploading...' : 'Upload Mods'}
			</label>
		</div>
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
			<strong>Drag & drop</strong> mod files here (.jar, .zip, .tar, .tar.gz), or use Upload Mods button
			above.
		</div>
	</div>

	<!-- Upload Progress -->
	{#if uploading}
		<div class="upload-progress-container">
			<div class="progress-header">
				<span class="progress-title">Uploading: {uploadingFileName}</span>
				<span class="progress-percent">{uploadProgress}%</span>
			</div>
			<div class="progress-bar-bg">
				<div class="progress-bar-fill" style="width: {uploadProgress}%"></div>
			</div>
			{#if uploadProgress === 100}
				<p class="progress-message">Processing and extracting files...</p>
			{/if}
		</div>
	{/if}

	<!-- Installed Modpacks -->
	{#if modpacks.length > 0}
		<div class="mods-section">
			<h3>Installed Modpacks</h3>
			<div class="modpack-list">
				{#each modpacks as modpack}
					<div class="modpack-card">
						<div class="modpack-header">
							{#if modpack.logoUrl}
								<img class="modpack-logo" src={modpack.logoUrl} alt={modpack.name} />
							{:else}
								<div class="modpack-logo-placeholder"></div>
							{/if}
							<div class="modpack-info">
								<h4>{modpack.name}</h4>
								<p class="modpack-meta">
									{modpack.modCount} mods
									{#if modpack.version}
										<span class="separator">|</span>
										{modpack.version}
									{/if}
								</p>
							</div>
							<button
								class="btn-action danger"
								onclick={() => uninstallModpack(modpack.id, modpack.name)}
								disabled={uninstallingModpack === modpack.id || isServerRunning}
								title={isServerRunning ? 'Stop server to uninstall' : ''}
							>
								{uninstallingModpack === modpack.id ? 'Removing...' : 'Uninstall'}
							</button>
						</div>
						{#if groupedMods.grouped[modpack.id]?.length}
							<details class="modpack-mods">
								<summary>View {groupedMods.grouped[modpack.id].length} mods</summary>
								<ul class="mod-file-list">
									{#each groupedMods.grouped[modpack.id] as mod}
										<li class:disabled={mod.isDisabled}>
											<span class="mod-name">{mod.fileName}</span>
											<span class="mod-size">{formatBytes(mod.sizeBytes)}</span>
										</li>
									{/each}
								</ul>
							</details>
						{/if}
					</div>
				{/each}
			</div>
		</div>
	{/if}

	<!-- Standalone Mods -->
	<div class="mods-section">
		<h3>Standalone Mods</h3>
		{#if loading}
			<p class="muted">Loading mods...</p>
		{:else if groupedMods.standalone.length === 0}
			<p class="muted">No standalone mods installed.</p>
		{:else}
			<div class="mod-list">
				<table>
					<thead>
						<tr>
							<th>Name</th>
							<th>Size</th>
							<th>Modified</th>
							<th>Actions</th>
						</tr>
					</thead>
					<tbody>
						{#each groupedMods.standalone as mod}
							<tr>
								<td>
									<span class:disabled={mod.isDisabled}>{mod.fileName}</span>
								</td>
								<td>{formatBytes(mod.sizeBytes)}</td>
								<td>{new Date(mod.modifiedAt).toLocaleString()}</td>
								<td class="actions">
									<a
										class="btn-action"
										href={`/api/servers/${data.server?.name}/mods/${encodeURIComponent(
											mod.fileName
										)}/download`}
										download={mod.fileName}
									>
										Download
									</a>
									<button
										class="btn-action danger"
										onclick={() => deleteMod(mod.fileName)}
										disabled={isServerRunning}
										title={isServerRunning ? 'Stop server to delete' : ''}
									>
										Delete
									</button>
								</td>
							</tr>
						{/each}
					</tbody>
				</table>
			</div>
		{/if}
	</div>

	<!-- CurseForge Install -->
	<div class="curseforge-section">
		<h3>Install from CurseForge</h3>
		<CurseForgeSearch serverName={data.server?.name ?? ''} onInstallComplete={loadMods} />
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

	.action-buttons {
		display: flex;
		gap: 12px;
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

	.mods-section, .curseforge-section {
		background: #1a1e2f;
		border-radius: 16px;
		padding: 20px;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.35);
		display: flex;
		flex-direction: column;
		gap: 16px;
	}

	.mod-list table {
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

	.disabled {
		opacity: 0.6;
	}

	.muted {
		color: #8890b1;
		margin: 0;
	}

	.curseforge-section h3 {
		margin: 0 0 16px 0;
	}

	.modpack-list {
		display: flex;
		flex-direction: column;
		gap: 16px;
	}

	.modpack-card {
		background: #141827;
		border-radius: 12px;
		padding: 16px;
		border: 1px solid #2a2f47;
	}

	.modpack-header {
		display: flex;
		align-items: center;
		gap: 16px;
	}

	.modpack-logo {
		width: 48px;
		height: 48px;
		border-radius: 8px;
		object-fit: cover;
		background: #1a1f33;
	}

	.modpack-logo-placeholder {
		width: 48px;
		height: 48px;
		border-radius: 8px;
		background: #1a1f33;
	}

	.modpack-info {
		flex: 1;
	}

	.modpack-info h4 {
		margin: 0 0 4px;
		font-size: 16px;
	}

	.modpack-meta {
		margin: 0;
		color: #8890b1;
		font-size: 13px;
	}

	.modpack-meta .separator {
		margin: 0 6px;
		opacity: 0.5;
	}

	.modpack-mods {
		margin-top: 12px;
		padding-top: 12px;
		border-top: 1px solid #2a2f47;
	}

	.modpack-mods summary {
		cursor: pointer;
		color: #9aa2c5;
		font-size: 13px;
		margin-bottom: 8px;
	}

	.mod-file-list {
		list-style: none;
		padding: 0;
		margin: 0;
		display: flex;
		flex-direction: column;
		gap: 4px;
		max-height: 200px;
		overflow-y: auto;
	}

	.mod-file-list li {
		display: flex;
		justify-content: space-between;
		align-items: center;
		padding: 6px 8px;
		border-radius: 6px;
		background: #0d0f16;
		font-size: 12px;
	}

	.mod-file-list li.disabled {
		opacity: 0.5;
	}

	.mod-file-list .mod-name {
		color: #d4d9f1;
		flex: 1;
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
	}

	.mod-file-list .mod-size {
		color: #8890b1;
		margin-left: 12px;
		flex-shrink: 0;
	}

	.delete-all-btn {
		background: rgba(255, 92, 92, 0.15);
		color: #ff9f9f;
		border: 1px solid rgba(255, 92, 92, 0.3);
		border-radius: 8px;
		padding: 10px 20px;
		font-size: 14px;
		font-weight: 600;
		cursor: pointer;
		transition: all 0.2s;
	}

	.delete-all-btn:hover:not(:disabled) {
		background: rgba(255, 92, 92, 0.25);
	}

	.delete-all-btn:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.upload-progress-container {
		background: #1a1e2f;
		border-radius: 12px;
		padding: 16px 20px;
		box-shadow: 0 10px 20px rgba(0, 0, 0, 0.25);
	}

	.progress-header {
		display: flex;
		justify-content: space-between;
		align-items: center;
		margin-bottom: 12px;
	}

	.progress-title {
		color: #eef0f8;
		font-size: 14px;
		font-weight: 600;
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
		flex: 1;
	}

	.progress-percent {
		color: #9aa2c5;
		font-size: 14px;
		font-weight: 600;
		margin-left: 12px;
	}

	.progress-bar-bg {
		height: 8px;
		background: #141827;
		border-radius: 999px;
		overflow: hidden;
		border: 1px solid #2a2f47;
	}

	.progress-bar-fill {
		height: 100%;
		background: linear-gradient(90deg, var(--mc-grass), #7ae68d);
		transition: width 0.3s ease;
		border-radius: 999px;
	}

	.progress-message {
		margin: 8px 0 0;
		color: #9aa2c5;
		font-size: 13px;
		font-style: italic;
	}
</style>
