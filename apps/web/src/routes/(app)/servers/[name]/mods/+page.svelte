<script lang="ts">
	import CurseForgeSearch from '$lib/components/CurseForgeSearch.svelte';
	import type { PageData } from './$types';
	import type { LayoutData } from '../$layout';
	import type { InstalledModWithModpack, InstalledModpack } from '$lib/api/types';
	import { modal } from '$lib/stores/modal';

	let { data }: { data: PageData & { server: LayoutData['server'] } } = $props();

	let mods = $state<InstalledModWithModpack[]>([]);
	let modpacks = $state<InstalledModpack[]>([]);
	let loading = $state(false);
	let uninstallingModpack = $state<number | null>(null);
	let uploading = $state(false);
	let isDragging = $state(false);

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
		if (!file.name.toLowerCase().endsWith('.jar') && !file.name.toLowerCase().endsWith('.zip')) {
			await modal.error('Only .jar or .zip files are supported.');
			return;
		}

		uploading = true;
		try {
			const form = new FormData();
			form.append('file', file);
			const res = await fetch(`/api/servers/${data.server.name}/mods/upload`, {
				method: 'POST',
				body: form
			});
			if (!res.ok) {
				const payload = await res.json().catch(() => ({}));
				await modal.error(payload.error || 'Failed to upload mod');
			} else {
				await loadMods();
			}
		} catch (err) {
			await modal.error(err instanceof Error ? err.message : 'Upload failed');
		} finally {
			uploading = false;
		}
	}

	async function handleUpload(event: Event) {
		const input = event.target as HTMLInputElement;
		const file = input.files?.[0];
		if (!file) return;
		await uploadModFile(file);
		input.value = '';
	}

	async function handleDrop(event: DragEvent) {
		event.preventDefault();
		isDragging = false;
		const file = event.dataTransfer?.files?.[0];
		if (!file) return;
		await uploadModFile(file);
	}

	async function deleteMod(fileName: string) {
		if (!data.server) return;
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

	const formatBytes = (bytes: number) => {
		if (!bytes) return '0 B';
		const units = ['B', 'KB', 'MB', 'GB', 'TB'];
		const i = Math.floor(Math.log(bytes) / Math.log(1024));
		return `${(bytes / Math.pow(1024, i)).toFixed(1)} ${units[i]}`;
	};
</script>

<div class="page">
	<div class="page-header">
		<div>
			<h2>Mods</h2>
			<p class="subtitle">Upload, manage, and install mods for this server</p>
		</div>
		<div class="action-buttons">
			<label class="upload-button">
				<input type="file" accept=".jar,.zip" onchange={handleUpload} disabled={uploading} />
				{uploading ? 'Uploading...' : 'Upload Mod'}
			</label>
		</div>
	</div>

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
			<strong>Drag & drop</strong> a mod file here (.jar or .zip), or use Upload Mod.
		</div>
	</div>

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
								disabled={uninstallingModpack === modpack.id}
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
									<button class="btn-action danger" onclick={() => deleteMod(mod.fileName)}>
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
</style>
