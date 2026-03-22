<script lang="ts">
	import { onMount } from 'svelte';
	import CurseForgeSearch from '$lib/components/CurseForgeSearch.svelte';
	import ModrinthSearch from '$lib/components/ModrinthSearch.svelte';
	import ProgressBar from '$lib/components/ProgressBar.svelte';
	import { modal } from '$lib/stores/modal';
	import { formatBytes, formatDate } from '$lib/utils/formatting';
	import type { PageData } from './$types';
	import type { LayoutData } from '../$types';
	import type { ClientPackageEntry, InstalledModWithModpack, InstalledModpack, ServerHeartbeat } from '$lib/api/types';

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
	let clientPackages = $state<ClientPackageEntry[]>([]);
	let clientPackageLoading = $state(false);
	let clientPackageCreating = $state(false);
	let clientPackageActions = $state<Record<string, boolean>>({});
	let packageFormat = $state<'curseforge' | 'mrpack'>('curseforge');
	let packageMcVersion = $state('');
	let packageModLoader = $state('');
	let packageModLoaderVersion = $state('');
	let showPackageOptions = $state(false);
	let serverVersion = $state<string | null>(null);
	let clientMods = $state<InstalledModWithModpack[]>([]);
	let clientModsLoading = $state(false);
	let clientModUploading = $state(false);
	let clientModUploadProgress = $state(0);
	let clientModUploadingFileName = $state('');
	let clientModIsDragging = $state(false);

	let detectedLoader = $state<string | null>(null);
	let detectedVersion = $state<string | null>(null);
	let loaderLoading = $state(true);
	let showLoaderPicker = $state(false);

	const loaderLogos: Record<string, string> = {
		forge: '/images/loaders/forge.png',
		fabric: '/images/loaders/fabric.png',
		neoforge: '/images/loaders/neoforge.png',
		quilt: '/images/loaders/quilt.svg'
	};

	const loaderNames: Record<string, string> = {
		forge: 'Forge',
		fabric: 'Fabric',
		neoforge: 'NeoForge',
		quilt: 'Quilt'
	};

	const isServerRunning = $derived(data.server?.status === 'running');

	onMount(async () => {
		loadServerVersion();

		const serverName = data.server?.name;
		if (!serverName) {
			loaderLoading = false;
			return;
		}

		try {
			const res = await fetch(`/api/servers/${serverName}/loader`);
			if (res.ok) {
				const info = await res.json();
				if (info.loader) {
					detectedLoader = info.loader;
					detectedVersion = info.version;
				} else {
					const stored = localStorage.getItem(`mineos-loader-${serverName}`);
					if (stored) {
						detectedLoader = stored;
					} else {
						showLoaderPicker = true;
					}
				}
			}
		} catch {
			showLoaderPicker = true;
		} finally {
			loaderLoading = false;
		}
	});

	function selectLoader(loader: string) {
		detectedLoader = loader;
		showLoaderPicker = false;
		localStorage.setItem(`mineos-loader-${data.server?.name}`, loader);
	}

	async function toggleMod(mod: any) {
		if (!data.server) return;
		const action = mod.isDisabled ? 'enable' : 'disable';
		const oldState = mod.isDisabled;
		const oldName = mod.fileName;

		// Optimistic update
		mod.isDisabled = !mod.isDisabled;

		try {
			const res = await fetch(
				`/api/servers/${data.server.name}/mods/${encodeURIComponent(oldName)}/${action}`,
				{ method: 'POST' }
			);
			if (!res.ok) {
				mod.isDisabled = oldState;
				const err = await res.json().catch(() => ({ error: 'Toggle failed' }));
				console.error(err.error);
			} else {
				const result = await res.json();
				mod.fileName = result.filename;
			}
		} catch {
			mod.isDisabled = oldState;
		}
	}

	$effect(() => {
		loadMods();
		loadClientMods();
		loadClientPackages();
	});

	async function loadServerVersion() {
		if (!data.server) return;
		try {
			const res = await fetch(`/api/servers/${data.server.name}/heartbeat`);
			if (res.ok) {
				const heartbeat: ServerHeartbeat = await res.json();
				serverVersion = heartbeat.ping?.serverVersion || null;
			}
		} catch (err) {
			console.error('Failed to load server version:', err);
		}
	}

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

	async function loadClientPackages() {
		if (!data.server) return;
		clientPackageLoading = true;
		try {
			const res = await fetch(`/api/servers/${data.server.name}/client-packages`);
			if (res.ok) {
				clientPackages = await res.json();
			}
		} catch (err) {
			console.error('Failed to load client packages:', err);
		} finally {
			clientPackageLoading = false;
		}
	}

	function reloadAll() {
		loadMods();
		loadClientMods();
		loadClientPackages();
	}

	async function loadClientMods() {
		if (!data.server) return;
		clientModsLoading = true;
		try {
			const res = await fetch(`/api/servers/${data.server.name}/client-mods`);
			if (res.ok) {
				clientMods = await res.json();
			}
		} catch (err) {
			console.error('Failed to load client mods:', err);
		} finally {
			clientModsLoading = false;
		}
	}

	async function uploadClientMod(file: File) {
		if (!data.server) return;
		const fileName = file.name.toLowerCase();
		if (!fileName.endsWith('.jar')) {
			await modal.error(`Only .jar files are supported for client mods. Got: ${file.name}`);
			return;
		}

		const serverName = data.server.name;
		clientModUploading = true;
		clientModUploadingFileName = file.name;
		clientModUploadProgress = 0;

		try {
			const form = new FormData();
			form.append('file', file);

			const xhr = new XMLHttpRequest();
			const uploadPromise = new Promise((resolve, reject) => {
				xhr.upload.addEventListener('progress', (e) => {
					if (e.lengthComputable) {
						clientModUploadProgress = Math.round((e.loaded / e.total) * 100);
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
				xhr.open('POST', `/api/servers/${serverName}/client-mods/upload`);
				xhr.send(form);
			});

			await uploadPromise;
			await loadClientMods();
		} catch (err) {
			await modal.error(err instanceof Error ? err.message : `Upload failed for ${file.name}`);
		} finally {
			clientModUploading = false;
			clientModUploadProgress = 0;
			clientModUploadingFileName = '';
		}
	}

	function handleClientModUpload(event: Event) {
		const input = event.target as HTMLInputElement;
		const files = Array.from(input.files || []);
		for (const file of files) {
			uploadClientMod(file);
		}
		input.value = '';
	}

	function handleClientModDrop(event: DragEvent) {
		event.preventDefault();
		clientModIsDragging = false;
		const files = Array.from(event.dataTransfer?.files || []);
		for (const file of files) {
			uploadClientMod(file);
		}
	}

	async function deleteClientMod(fileName: string) {
		if (!data.server) return;
		const confirmed = await modal.confirm(`Delete client mod "${fileName}"?`, 'Delete Client Mod');
		if (!confirmed) return;

		try {
			const res = await fetch(
				`/api/servers/${data.server.name}/client-mods/${encodeURIComponent(fileName)}`,
				{ method: 'DELETE' }
			);
			if (!res.ok) {
				const payload = await res.json().catch(() => ({}));
				await modal.error(payload.error || 'Failed to delete client mod');
			} else {
				await loadClientMods();
			}
		} catch (err) {
			await modal.error(err instanceof Error ? err.message : 'Delete failed');
		}
	}

	async function createClientPackage() {
		if (!data.server) return;
		clientPackageCreating = true;
		try {
			const body: Record<string, string> = { format: packageFormat };
			if (packageMcVersion.trim()) body.minecraftVersion = packageMcVersion.trim();
			if (packageModLoader.trim()) body.modLoader = packageModLoader.trim();
			if (packageModLoaderVersion.trim()) body.modLoaderVersion = packageModLoaderVersion.trim();

			const res = await fetch(`/api/servers/${data.server.name}/client-packages`, {
				method: 'POST',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify(body)
			});
			if (res.ok) {
				await loadClientPackages();
			} else {
				const errorData = await res.json().catch(() => ({}));
				await modal.error(errorData.error || 'Failed to create client package');
			}
		} catch (err) {
			await modal.error(err instanceof Error ? err.message : 'Failed to create client package');
		} finally {
			clientPackageCreating = false;
		}
	}

	async function deleteClientPackage(filename: string) {
		if (!data.server) return;
		const confirmed = await modal.confirm(
			`Delete client package "${filename}"? This cannot be undone.`,
			'Delete Client Package'
		);
		if (!confirmed) return;

		clientPackageActions[filename] = true;
		try {
			const res = await fetch(`/api/servers/${data.server.name}/client-packages/${filename}`, {
				method: 'DELETE'
			});
			if (res.ok) {
				await loadClientPackages();
			} else {
				const errorData = await res.json().catch(() => ({}));
				await modal.error(errorData.error || 'Failed to delete client package');
			}
		} catch (err) {
			await modal.error(err instanceof Error ? err.message : 'Failed to delete client package');
		} finally {
			delete clientPackageActions[filename];
			clientPackageActions = { ...clientPackageActions };
		}
	}

	function getClientPackageShareUrl(filename: string) {
		const path = `/client-packages/${encodeURIComponent(
			data.server?.name ?? ''
		)}/${encodeURIComponent(filename)}`;
		if (typeof window === 'undefined') return path;
		return new URL(path, window.location.origin).toString();
	}

	function downloadClientPackage(filename: string) {
		if (!data.server) return;
		window.location.href = `/api/servers/${data.server.name}/client-packages/${filename}/download?raw=1`;
	}

	async function copyClientPackageLink(filename: string) {
		try {
			const url = getClientPackageShareUrl(filename);
			await navigator.clipboard.writeText(url);
			await modal.success('Share link copied to clipboard.');
		} catch (err) {
			await modal.error(err instanceof Error ? err.message : 'Failed to copy link');
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
				reloadAll();
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

		const serverName = data.server.name;
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

				xhr.open('POST', `/api/servers/${serverName}/mods/upload`);
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
		<h2>Mods</h2>
		<p class="subtitle">Upload, manage, and install mods for this server</p>
	</div>

	{#if loaderLoading}
		<div class="loader-banner loading">Detecting mod loader...</div>
	{:else if showLoaderPicker}
		<div class="loader-picker">
			<h3>Select your mod loader</h3>
			<p>We couldn't detect a mod loader for this server. Choose one to filter compatible mods.</p>
			<div class="loader-options">
				{#each ['forge', 'fabric', 'neoforge', 'quilt'] as loader}
					<button class="loader-option" onclick={() => selectLoader(loader)}>
						<img src={loaderLogos[loader]} alt={loaderNames[loader]} class="loader-logo" />
						<span>{loaderNames[loader]}</span>
					</button>
				{/each}
			</div>
		</div>
	{:else if detectedLoader}
		<div class="loader-banner">
			<img src={loaderLogos[detectedLoader]} alt={loaderNames[detectedLoader] ?? detectedLoader} class="loader-logo" />
			<span class="loader-name">{loaderNames[detectedLoader] ?? detectedLoader}</span>
			{#if detectedVersion}
				<span class="loader-version">{detectedVersion}</span>
			{/if}
		</div>
	{/if}

	<!-- Server Mods -->
	<div class="mods-section">
		<div class="section-header">
			<h3>Server Mods</h3>
			<div class="section-actions">
				{#if groupedMods.standalone.length > 0}
					<button
						class="delete-all-btn"
						onclick={deleteAllMods}
						disabled={deletingAll || isServerRunning}
						title={isServerRunning
							? 'Stop server to delete mods'
							: `Delete all ${groupedMods.standalone.length} standalone mods`}
					>
						{deletingAll ? 'Deleting...' : `Delete All (${groupedMods.standalone.length})`}
					</button>
				{/if}
				<label class="upload-button small">
					<input
						type="file"
						accept=".jar,.zip,.tar,.tar.gz,.tgz"
						multiple
						onchange={handleUpload}
						disabled={uploading}
					/>
					{uploading ? 'Uploading...' : 'Upload'}
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
				<strong>Drag & drop</strong> mod files here (.jar, .zip, .tar, .tar.gz)
			</div>
		</div>

		{#if uploading}
			<div class="upload-progress-container">
				<div class="progress-header">
					<span class="progress-title">Uploading: {uploadingFileName}</span>
				</div>
				<ProgressBar value={uploadProgress} color="green" size="sm" showLabel />
				{#if uploadProgress === 100}
					<p class="progress-message">Processing and extracting files...</p>
				{/if}
			</div>
		{/if}

		<!-- Installed Modpacks -->
		{#if modpacks.length > 0}
			<div class="subsection">
				<h4>Installed Modpacks</h4>
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
												<label class="mod-toggle" title={mod.isDisabled ? 'Enable mod' : 'Disable mod'}>
													<input
														type="checkbox"
														checked={!mod.isDisabled}
														onchange={() => toggleMod(mod)}
													/>
													<span class="toggle-slider"></span>
												</label>
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

		<!-- All Mod Files -->
		<details class="server-mods-details">
			<summary>
				<h4>All Files{#if !loading && mods.length > 0} <span class="mod-count">({mods.length})</span>{/if}</h4>
			</summary>
			{#if loading}
				<p class="muted">Loading mods...</p>
			{:else if mods.length === 0}
				<p class="muted">No server mods installed.</p>
			{:else}
				<div class="mod-list">
					<table>
						<thead>
							<tr>
								<th>Name</th>
								<th>Source</th>
								<th>Size</th>
								<th>Modified</th>
								<th>Actions</th>
							</tr>
						</thead>
						<tbody>
							{#each mods as mod}
								<tr class:modpack-mod={!!mod.modpackName}>
									<td>
										<label class="mod-toggle" title={mod.isDisabled ? 'Enable mod' : 'Disable mod'}>
											<input
												type="checkbox"
												checked={!mod.isDisabled}
												onchange={() => toggleMod(mod)}
											/>
											<span class="toggle-slider"></span>
										</label>
										<span class:disabled={mod.isDisabled}>{mod.fileName}</span>
									</td>
									<td>
										{#if mod.modpackName}
											<span class="source-badge modpack">{mod.modpackName}</span>
										{:else}
											<span class="source-badge manual">Manual</span>
										{/if}
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
		</details>

		<!-- Install from CurseForge -->
		<div class="subsection">
			<h4>Install from CurseForge</h4>
			<CurseForgeSearch serverName={data.server?.name ?? ''} {serverVersion} loader={detectedLoader} onInstallComplete={reloadAll} />
		</div>

		<!-- Install from Modrinth -->
		<div class="subsection">
			<h4>Install from Modrinth</h4>
			<ModrinthSearch serverName={data.server?.name ?? ''} {serverVersion} loader={detectedLoader} onInstallComplete={reloadAll} />
		</div>
	</div>

	<!-- Client Distribution -->
	<div class="mods-section">
		<div class="section-header">
			<h3>Client Distribution</h3>
			<p class="muted">
				Manage client-only mods and generate downloadable packages for players to import into their launcher.
			</p>
		</div>

		<!-- Client-Only Mods -->
		<div class="subsection">
			<div class="client-mods-header">
				<h4>Client-Only Mods{#if clientMods.length > 0} <span class="mod-count">({clientMods.length})</span>{/if}</h4>
				<label class="upload-button small">
					<input
						type="file"
						accept=".jar"
						multiple
						onchange={handleClientModUpload}
						disabled={clientModUploading}
					/>
					{clientModUploading ? 'Uploading...' : 'Upload'}
				</label>
			</div>
			<p class="muted">
				These mods are included in client packages but not loaded by the server.
				Useful for minimaps, shaders, performance mods, and visual enhancements.
			</p>

			<!-- svelte-ignore a11y_no_static_element_interactions -->
			<div
				class="upload-drop"
				class:active={clientModIsDragging}
				ondragover={(event) => {
					event.preventDefault();
					clientModIsDragging = true;
				}}
				ondragleave={() => {
					clientModIsDragging = false;
				}}
				ondrop={handleClientModDrop}
			>
				<div class="drop-content">
					<strong>Drag & drop</strong> client mod files here (.jar)
				</div>
			</div>

			{#if clientModUploading}
				<div class="upload-progress-container">
					<div class="progress-header">
						<span class="progress-title">Uploading: {clientModUploadingFileName}</span>
					</div>
					<ProgressBar value={clientModUploadProgress} color="green" size="sm" showLabel />
				</div>
			{/if}

			{#if clientModsLoading}
				<p class="muted">Loading client mods...</p>
			{:else if clientMods.length === 0}
				<p class="muted">No client-only mods yet. They are added automatically when installing modpacks, or you can upload them manually.</p>
			{:else}
				<div class="mod-list">
					<table>
						<thead>
							<tr>
								<th>Name</th>
								<th>Source</th>
								<th>Size</th>
								<th>Modified</th>
								<th>Actions</th>
							</tr>
						</thead>
						<tbody>
							{#each clientMods as mod}
								<tr>
									<td>
										<span class:disabled={mod.isDisabled}>{mod.fileName}</span>
									</td>
									<td>
										{#if mod.modpackName}
											<span class="source-badge modpack">{mod.modpackName}</span>
										{:else}
											<span class="source-badge manual">Manual</span>
										{/if}
									</td>
									<td>{formatBytes(mod.sizeBytes)}</td>
									<td>{new Date(mod.modifiedAt).toLocaleString()}</td>
									<td class="actions">
										<a
											class="btn-action"
											href={`/api/servers/${data.server?.name}/client-mods/${encodeURIComponent(mod.fileName)}/download`}
											download={mod.fileName}
										>
											Download
										</a>
										<button
											class="btn-action danger"
											onclick={() => deleteClientMod(mod.fileName)}
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

		<!-- Generate Package -->
		<div class="subsection">
			<h4>Generate Package</h4>
			<p class="muted">
				Bundles all server mods, client-only mods, configs, resource packs, and other assets into a single file.
				Share the download link with friends.
			</p>

			<div class="format-selector">
				<button class="format-btn" class:active={packageFormat === 'curseforge'} onclick={() => packageFormat = 'curseforge'}>
					CurseForge (.zip)
				</button>
				<button class="format-btn" class:active={packageFormat === 'mrpack'} onclick={() => packageFormat = 'mrpack'}>
					Modrinth (.mrpack)
				</button>
			</div>

			<button class="toggle-options" onclick={() => showPackageOptions = !showPackageOptions}>
				{showPackageOptions ? 'Hide' : 'Show'} Advanced Options
			</button>

			{#if showPackageOptions}
				<div class="package-options">
					<p class="muted" style="margin-bottom: 12px;">
						These are auto-detected when possible. Only fill in if auto-detection fails or you need to override.
					</p>
					<div class="option-row">
						<label for="pkg-mc-version">Minecraft Version</label>
						<input id="pkg-mc-version" type="text" bind:value={packageMcVersion} placeholder="e.g. 1.20.1 (auto-detected)" />
					</div>
					<div class="option-row">
						<label for="pkg-mod-loader">Mod Loader</label>
						<select id="pkg-mod-loader" bind:value={packageModLoader}>
							<option value="">Auto-detect</option>
							<option value="forge">Forge</option>
							<option value="neoforge">NeoForge</option>
							<option value="fabric">Fabric</option>
							<option value="quilt">Quilt</option>
						</select>
					</div>
					<div class="option-row">
						<label for="pkg-loader-version">Loader Version</label>
						<input id="pkg-loader-version" type="text" bind:value={packageModLoaderVersion} placeholder="e.g. 47.2.0 (auto-detected)" />
					</div>
				</div>
			{/if}

			<button class="btn-action generate-btn" onclick={createClientPackage} disabled={clientPackageCreating}>
				{clientPackageCreating ? 'Generating...' : `Generate ${packageFormat === 'mrpack' ? '.mrpack' : 'CurseForge'} Package`}
			</button>

			{#if clientPackageLoading}
				<p class="muted">Loading packages...</p>
			{:else if clientPackages.length === 0}
				<p class="muted">No packages generated yet.</p>
			{:else}
				<div class="mod-list">
					<table>
						<thead>
							<tr>
								<th>Format</th>
								<th>Filename</th>
								<th>Created</th>
								<th>Size</th>
								<th>Actions</th>
							</tr>
						</thead>
						<tbody>
							{#each clientPackages as pkg}
								<tr>
									<td>
										<span class="format-badge" class:mrpack={pkg.format === 'mrpack'} class:curseforge={pkg.format === 'curseforge'}>
											{pkg.format === 'mrpack' ? 'Modrinth' : 'CurseForge'}
										</span>
									</td>
									<td>{pkg.filename}</td>
									<td>{formatDate(pkg.time)}</td>
									<td>{formatBytes(pkg.size)}</td>
									<td class="actions">
										<button class="btn-action" onclick={() => downloadClientPackage(pkg.filename)}>
											Download
										</button>
										<button class="btn-action" onclick={() => copyClientPackageLink(pkg.filename)}>
											Copy Link
										</button>
										<button
											class="btn-action danger"
											onclick={() => deleteClientPackage(pkg.filename)}
											disabled={clientPackageActions[pkg.filename]}
										>
											{clientPackageActions[pkg.filename] ? 'Deleting...' : 'Delete'}
										</button>
									</td>
								</tr>
							{/each}
						</tbody>
					</table>
				</div>
			{/if}
		</div>
	</div>
</div>

<style>
	.page {
		display: flex;
		flex-direction: column;
		gap: 24px;
	}

	.page-header h2 {
		margin: 0 0 4px;
		font-size: 24px;
		font-weight: 600;
	}

	.subtitle {
		margin: 0;
		color: #aab2d3;
		font-size: 14px;
	}

	.section-header {
		display: flex;
		justify-content: space-between;
		align-items: flex-start;
		gap: 12px;
		flex-wrap: wrap;
	}

	.section-header h3 {
		margin: 0;
	}

	.section-actions {
		display: flex;
		gap: 8px;
		align-items: center;
	}

	.subsection {
		background: #141827;
		border-radius: 12px;
		padding: 16px;
		display: flex;
		flex-direction: column;
		gap: 12px;
	}

	.subsection h4 {
		margin: 0;
		font-size: 15px;
		font-weight: 600;
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

	.mods-section {
		background: #1a1e2f;
		border-radius: 16px;
		padding: 20px;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.35);
		display: flex;
		flex-direction: column;
		gap: 16px;
	}

	.client-mods-header {
		display: flex;
		justify-content: space-between;
		align-items: center;
		gap: 12px;
	}

	.upload-button.small {
		padding: 6px 14px;
		font-size: 13px;
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

	.server-mods-details summary {
		cursor: pointer;
		list-style: none;
		user-select: none;
	}

	.server-mods-details summary::-webkit-details-marker {
		display: none;
	}

	.server-mods-details summary h4 {
		display: inline-flex;
		align-items: center;
		gap: 8px;
		margin: 0;
		font-size: 15px;
		font-weight: 600;
	}

	.server-mods-details summary h4::before {
		content: '\25B6';
		font-size: 10px;
		color: #8890b1;
		transition: transform 0.2s;
	}

	.server-mods-details[open] summary h4::before {
		transform: rotate(90deg);
	}

	.mod-count {
		font-weight: 400;
		color: #8890b1;
		font-size: 14px;
	}

	.modpack-mod td {
		opacity: 0.55;
	}

	.modpack-mod:hover td {
		opacity: 0.85;
	}

	.source-badge {
		font-size: 11px;
		padding: 2px 8px;
		border-radius: 4px;
		font-weight: 600;
		white-space: nowrap;
	}

	.source-badge.modpack {
		background: rgba(138, 118, 255, 0.15);
		color: #b0a0ff;
	}

	.source-badge.manual {
		background: rgba(255, 255, 255, 0.06);
		color: #8890b1;
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
		padding: 6px 14px;
		font-size: 13px;
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

	.progress-message {
		margin: 8px 0 0;
		color: #9aa2c5;
		font-size: 13px;
		font-style: italic;
	}

	.format-selector {
		display: flex;
		gap: 8px;
	}

	.format-btn {
		background: #2b2f45;
		color: #9aa2c5;
		border: 1px solid #2a2f47;
		border-radius: 8px;
		padding: 8px 16px;
		font-family: inherit;
		font-size: 13px;
		font-weight: 500;
		cursor: pointer;
		transition: all 0.2s;
	}

	.format-btn.active {
		background: rgba(90, 107, 255, 0.2);
		color: #a4b0ff;
		border-color: #5a6bff;
	}

	.toggle-options {
		background: none;
		border: none;
		color: #8890b1;
		font-family: inherit;
		font-size: 13px;
		cursor: pointer;
		padding: 0;
		text-decoration: underline;
		text-underline-offset: 3px;
		align-self: flex-start;
	}

	.toggle-options:hover {
		color: #d4d9f1;
	}

	.package-options {
		background: #0d0f16;
		border-radius: 12px;
		padding: 16px;
		display: flex;
		flex-direction: column;
		gap: 12px;
	}

	.option-row {
		display: flex;
		align-items: center;
		gap: 12px;
	}

	.option-row label {
		width: 140px;
		flex-shrink: 0;
		font-size: 13px;
		color: #9aa2c5;
	}

	.option-row input,
	.option-row select {
		flex: 1;
		background: #0d0f16;
		border: 1px solid #2a2f47;
		border-radius: 6px;
		padding: 8px 12px;
		color: #eef0f8;
		font-family: inherit;
		font-size: 13px;
	}

	.option-row select {
		cursor: pointer;
	}

	.generate-btn {
		align-self: flex-start;
		padding: 10px 20px;
		font-size: 14px;
		font-weight: 600;
		background: var(--mc-grass);
		color: white;
	}

	.format-badge {
		display: inline-block;
		padding: 3px 8px;
		border-radius: 4px;
		font-size: 11px;
		font-weight: 600;
		text-transform: uppercase;
		letter-spacing: 0.05em;
	}

	.format-badge.curseforge {
		background: rgba(241, 100, 54, 0.2);
		color: #f1a472;
	}

	.format-badge.mrpack {
		background: rgba(30, 200, 100, 0.2);
		color: #7ae68d;
	}

	.loader-banner {
		display: flex;
		align-items: center;
		gap: 12px;
		padding: 12px 20px;
		background: rgba(22, 27, 46, 0.9);
		border: 1px solid rgba(42, 47, 71, 0.8);
		border-radius: 12px;
		margin-bottom: 16px;
	}

	.loader-banner.loading {
		color: #8890b1;
		font-style: italic;
	}

	.loader-logo {
		width: 28px;
		height: 28px;
		object-fit: contain;
	}

	.loader-name {
		font-weight: 600;
		font-size: 16px;
		color: #eef0f8;
	}

	.loader-version {
		color: #8890b1;
		font-size: 14px;
	}

	.loader-picker {
		padding: 24px;
		background: rgba(22, 27, 46, 0.9);
		border: 1px solid rgba(42, 47, 71, 0.8);
		border-radius: 12px;
		margin-bottom: 16px;
		text-align: center;
	}

	.loader-picker h3 { margin: 0 0 8px; font-size: 18px; }
	.loader-picker p { margin: 0 0 20px; color: #8890b1; font-size: 14px; }

	.loader-options {
		display: flex;
		gap: 16px;
		justify-content: center;
		flex-wrap: wrap;
	}

	.loader-option {
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 8px;
		padding: 16px 24px;
		background: rgba(30, 36, 58, 0.8);
		border: 1px solid rgba(62, 69, 100, 0.6);
		border-radius: 12px;
		color: #cdd3ee;
		cursor: pointer;
		transition: all 0.2s;
		font-size: 14px;
		font-weight: 500;
	}

	.loader-option:hover {
		border-color: var(--mc-grass);
		background: rgba(106, 176, 76, 0.1);
	}

	.mod-toggle {
		position: relative;
		display: inline-block;
		width: 36px;
		height: 20px;
		flex-shrink: 0;
	}

	.mod-toggle input { opacity: 0; width: 0; height: 0; }

	.toggle-slider {
		position: absolute;
		inset: 0;
		background: #2a2f47;
		border-radius: 20px;
		cursor: pointer;
		transition: background 0.2s;
	}

	.toggle-slider::before {
		content: '';
		position: absolute;
		height: 14px;
		width: 14px;
		left: 3px;
		bottom: 3px;
		background: #8890b1;
		border-radius: 50%;
		transition: transform 0.2s, background 0.2s;
	}

	.mod-toggle input:checked + .toggle-slider {
		background: rgba(106, 176, 76, 0.3);
	}

	.mod-toggle input:checked + .toggle-slider::before {
		transform: translateX(16px);
		background: var(--mc-grass);
	}
</style>
