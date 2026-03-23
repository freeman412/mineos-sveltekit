<script lang="ts">
	import { invalidateAll } from '$app/navigation';
	import StatusBadge from './StatusBadge.svelte';
	import ProgressBar from './ProgressBar.svelte';
	import type { Profile } from '$lib/api/types';
	import * as api from '$lib/api/client';

	let {
		serverName,
		currentJar,
		currentServerType,
		onClose,
		onComplete
	}: {
		serverName: string;
		currentJar: string | null;
		currentServerType: string;
		onClose: () => void;
		onComplete: () => void;
	} = $props();

	type ServerType = 'vanilla' | 'paper' | 'spigot' | 'craftbukkit' | 'forge' | 'fabric' | 'bedrock';

	const serverTypes: { id: ServerType; name: string; category: string }[] = [
		{ id: 'vanilla', name: 'Vanilla', category: 'vanilla' },
		{ id: 'paper', name: 'Paper', category: 'plugins' },
		{ id: 'spigot', name: 'Spigot', category: 'plugins' },
		{ id: 'craftbukkit', name: 'CraftBukkit', category: 'plugins' },
		{ id: 'forge', name: 'Forge', category: 'mods' },
		{ id: 'fabric', name: 'Fabric', category: 'mods' },
	];

	// Detect current server category from jar name
	function detectCategory(jar: string | null, type: string): string {
		if (type === 'bedrock') return 'bedrock';
		const j = (jar ?? '').toLowerCase();
		if (j.includes('forge') || j.includes('neoforge')) return 'mods';
		if (j.includes('fabric') || j.includes('quilt')) return 'mods';
		if (j.includes('paper') || j.includes('spigot') || j.includes('purpur') || j.includes('bukkit')) return 'plugins';
		return 'vanilla';
	}

	const currentCategory = detectCategory(currentJar, currentServerType);

	// State
	let step = $state<'type' | 'version' | 'confirm' | 'installing'>('type');
	let selectedType = $state<ServerType | null>(null);
	let profiles = $state<Profile[]>([]);
	let loading = $state(false);
	let selectedProfileId = $state('');
	let error = $state('');

	// Version pagination
	let versionSearch = $state('');
	let versionPage = $state(0);
	const perPage = 12;

	const filteredProfiles = $derived.by(() => {
		if (!selectedType) return [];
		return profiles.filter((p) => {
			switch (selectedType) {
				case 'vanilla': return p.group === 'vanilla';
				case 'paper': return p.group === 'paper';
				case 'spigot': return p.group === 'spigot' || (p.group === 'vanilla' && p.type === 'release');
				case 'craftbukkit': return p.group === 'craftbukkit' || p.group === 'bukkit' || (p.group === 'vanilla' && p.type === 'release');
				case 'forge': return p.group === 'forge';
				case 'fabric': return p.group === 'fabric';
				case 'bedrock': return p.group === 'bedrock-server' || p.group === 'bedrock-server-preview';
				default: return false;
			}
		});
	});

	const searchedProfiles = $derived(
		versionSearch.trim()
			? filteredProfiles.filter((p) => p.version.includes(versionSearch.trim()))
			: filteredProfiles
	);
	const totalPages = $derived(Math.ceil(searchedProfiles.length / perPage));
	const pagedProfiles = $derived(
		searchedProfiles.slice(versionPage * perPage, (versionPage + 1) * perPage)
	);

	// Warning message based on switching categories
	function getWarning(from: string, toType: ServerType): string | null {
		const to = serverTypes.find((t) => t.id === toType)?.category ?? '';
		if (from === to) return null;
		if (from === 'vanilla') return null; // Nothing to lose

		if (from === 'plugins' && to === 'mods')
			return 'Your existing plugins will not load on a modded server. Consider Mohist if you need both mods and plugins.';
		if (from === 'plugins' && to === 'vanilla')
			return 'Your existing plugins will not load on a vanilla server.';
		if (from === 'mods' && to === 'plugins')
			return 'Your existing mods will not load on a plugin server.';
		if (from === 'mods' && to === 'vanilla')
			return 'Your existing mods will not load on a vanilla server.';
		if (to === 'bedrock' || from === 'bedrock')
			return 'Switching between Java and Bedrock is not supported — worlds and configs are incompatible.';

		return null;
	}

	const warning = $derived(selectedType ? getWarning(currentCategory, selectedType) : null);

	// Install state
	let installProgress = $state(0);
	let installStep = $state('');
	let installCompleted = $state(false);
	let installError = $state('');

	async function loadProfiles() {
		loading = true;
		try {
			const res = await fetch('/api/host/profiles');
			if (res.ok) {
				const data = await res.json();
				profiles = Array.isArray(data) ? data : (data.data ?? []);
			}
		} catch (err) {
			console.error('Failed to load profiles', err);
		} finally {
			loading = false;
		}
	}

	async function selectType(type: ServerType) {
		if (type === 'bedrock' && currentServerType !== 'bedrock') {
			error = 'Cannot switch a Java server to Bedrock. Create a new Bedrock server instead.';
			return;
		}
		if (currentServerType === 'bedrock' && type !== 'bedrock') {
			error = 'Cannot switch a Bedrock server to Java. Create a new Java server instead.';
			return;
		}
		selectedType = type;
		selectedProfileId = '';
		versionSearch = '';
		versionPage = 0;
		error = '';

		if (profiles.length === 0) {
			await loadProfiles();
		}

		// Forge and Fabric have their own install flows
		if (type === 'forge' || type === 'fabric') {
			await loadProfiles();
		}

		step = 'version';
	}

	async function doInstall() {
		if (!selectedType || !selectedProfileId) return;
		step = 'installing';
		installStep = 'Preparing...';
		installProgress = 0;
		installError = '';

		try {
			const selectedProfile = profiles.find((p) => p.id === selectedProfileId);
			const mcVersion = selectedProfile?.version ?? '';

			if (selectedType === 'forge') {
				// Use Forge install API
				installStep = 'Installing Forge...';
				const result = await api.installForge(fetch, mcVersion, selectedProfileId.replace(`forge-`, ''), serverName);
				if (result.error) throw new Error(result.error);
				if (result.data) {
					// Stream progress
					const source = new EventSource(`/api/forge/install/${result.data.installId}/stream`);
					source.onmessage = (event) => {
						const data = JSON.parse(event.data);
						installProgress = data.progress ?? 0;
						installStep = data.currentStep || 'Installing Forge...';
						if (data.status === 'completed') {
							source.close();
							installCompleted = true;
							installStep = 'Forge installed successfully!';
							installProgress = 100;
						} else if (data.status === 'failed') {
							source.close();
							installError = data.error || 'Forge installation failed';
						}
					};
					source.onerror = () => {
						source.close();
						if (!installCompleted) installError = 'Lost connection';
					};
					return;
				}
			} else if (selectedType === 'fabric') {
				// Use Fabric install API — need loader version
				installStep = 'Fetching Fabric loader...';
				const loaderRes = await api.getFabricLoaderVersions(fetch);
				const stableLoader = loaderRes.data?.find((l) => l.isStable)?.version;
				if (!stableLoader) throw new Error('No stable Fabric loader found');

				installStep = 'Installing Fabric...';
				const result = await api.installFabric(fetch, mcVersion, stableLoader, serverName);
				if (result.error) throw new Error(result.error);
				if (result.data) {
					const source = new EventSource(`/api/fabric/install/${result.data.installId}/stream`);
					source.onmessage = (event) => {
						const data = JSON.parse(event.data);
						installProgress = data.progress ?? 0;
						installStep = data.currentStep || 'Installing Fabric...';
						if (data.status === 'completed') {
							source.close();
							installCompleted = true;
							installStep = 'Fabric installed successfully!';
							installProgress = 100;
						} else if (data.status === 'failed') {
							source.close();
							installError = data.error || 'Fabric installation failed';
						}
					};
					source.onerror = () => {
						source.close();
						if (!installCompleted) installError = 'Lost connection';
					};
					return;
				}
			} else {
				// Standard profile: download if needed, then copy
				if (selectedProfile && !selectedProfile.downloaded) {
					installStep = 'Downloading server JAR...';
					installProgress = 30;
					const dlRes = await fetch(`/api/host/profiles/${selectedProfileId}/download`, { method: 'POST' });
					if (!dlRes.ok) {
						const err = await dlRes.json().catch(() => ({ error: 'Download failed' }));
						throw new Error(err.error || 'Download failed');
					}
				}

				installStep = 'Copying to server...';
				installProgress = 70;
				const copyRes = await fetch(`/api/host/profiles/${selectedProfileId}/copy-to-server`, {
					method: 'POST',
					headers: { 'Content-Type': 'application/json' },
					body: JSON.stringify({ serverName })
				});
				if (!copyRes.ok) {
					const err = await copyRes.json().catch(() => ({ error: 'Copy failed' }));
					throw new Error(err.error || 'Copy failed');
				}

				installCompleted = true;
				installStep = 'Server type changed successfully!';
				installProgress = 100;
			}
		} catch (err) {
			installError = err instanceof Error ? err.message : 'Installation failed';
		}
	}

	function finish() {
		invalidateAll();
		onComplete();
	}
</script>

<div class="modal-overlay" onclick={onClose} role="dialog">
	<!-- svelte-ignore a11y_click_events_have_key_events -->
	<!-- svelte-ignore a11y_no_static_element_interactions -->
	<div class="modal" onclick={(e) => e.stopPropagation()}>
		<div class="modal-header">
			<h2>Change Server Type</h2>
			<button class="close-btn" onclick={onClose}>&times;</button>
		</div>

		{#if step === 'type'}
			<div class="modal-body">
				<p class="subtitle">Select the new server type for <strong>{serverName}</strong></p>
				{#if error}
					<div class="error-box">{error}</div>
				{/if}
				<div class="type-list">
					{#each serverTypes as type}
						<button
							class="type-row"
							class:current={detectCategory(currentJar, currentServerType) === type.category && !selectedType}
							onclick={() => selectType(type.id)}
						>
							<span class="type-name">{type.name}</span>
							<span class="type-category">{type.category}</span>
						</button>
					{/each}
				</div>
			</div>

		{:else if step === 'version'}
			<div class="modal-body">
				<div class="step-header">
					<button class="back-btn" onclick={() => { step = 'type'; selectedType = null; }}>Back</button>
					<h3>Choose {serverTypes.find(t => t.id === selectedType)?.name} Version</h3>
				</div>

				{#if warning}
					<div class="warning-box">{warning}</div>
				{/if}

				{#if loading}
					<p class="loading">Loading versions...</p>
				{:else if filteredProfiles.length === 0 && (selectedType === 'spigot' || selectedType === 'craftbukkit')}
					<p class="empty">No built JARs yet.</p>
					<a href="/profiles/buildtools" class="btn-primary">Open BuildTools</a>
				{:else if filteredProfiles.length === 0}
					<p class="empty">No versions available.</p>
				{:else}
					<div class="version-search">
						<input
							type="text"
							placeholder="Search versions..."
							bind:value={versionSearch}
							oninput={() => versionPage = 0}
						/>
					</div>

					<div class="version-grid">
						{#each pagedProfiles as profile}
							<button
								class="version-card"
								class:selected={selectedProfileId === profile.id}
								onclick={() => selectedProfileId = profile.id}
							>
								<span class="ver-num">{profile.version}</span>
								{#if profile.downloaded}
									<StatusBadge variant="success" size="sm">Ready</StatusBadge>
								{:else}
									<StatusBadge variant="info" size="sm">Download</StatusBadge>
								{/if}
							</button>
						{/each}
					</div>

					{#if totalPages > 1}
						<div class="pagination">
							<button class="btn-page" disabled={versionPage === 0} onclick={() => versionPage--}>Prev</button>
							<span class="page-info">{versionPage + 1} / {totalPages}</span>
							<button class="btn-page" disabled={versionPage >= totalPages - 1} onclick={() => versionPage++}>Next</button>
						</div>
					{/if}

					<div class="action-bar">
						<button
							class="btn-primary"
							disabled={!selectedProfileId}
							onclick={doInstall}
						>
							Change to {serverTypes.find(t => t.id === selectedType)?.name}
						</button>
					</div>
				{/if}
			</div>

		{:else if step === 'installing'}
			<div class="modal-body installing">
				<div class="spinner"></div>
				<h3>{installStep}</h3>
				{#if installProgress > 0}
					<ProgressBar value={installProgress} color="green" size="md" showLabel />
				{/if}
				{#if installCompleted}
					<div class="success-box">
						Server type changed! Restart the server for changes to take effect.
					</div>
					<button class="btn-primary" onclick={finish}>Done</button>
				{:else if installError}
					<div class="error-box">{installError}</div>
					<button class="btn-secondary" onclick={() => { step = 'version'; installError = ''; }}>Try Again</button>
				{/if}
			</div>
		{/if}
	</div>
</div>

<style>
	.modal-overlay {
		position: fixed;
		inset: 0;
		background: rgba(0, 0, 0, 0.6);
		display: flex;
		align-items: center;
		justify-content: center;
		z-index: 1000;
		padding: 20px;
	}

	.modal {
		background: #141827;
		border: 1px solid #2a2f47;
		border-radius: 16px;
		width: 100%;
		max-width: 600px;
		max-height: 80vh;
		overflow-y: auto;
		box-shadow: 0 25px 50px rgba(0, 0, 0, 0.5);
	}

	.modal-header {
		display: flex;
		justify-content: space-between;
		align-items: center;
		padding: 20px 24px;
		border-bottom: 1px solid #2a2f47;
	}

	.modal-header h2 { margin: 0; font-size: 20px; }
	.close-btn { background: none; border: none; color: #8890b1; font-size: 24px; cursor: pointer; }
	.close-btn:hover { color: #eef0f8; }

	.modal-body { padding: 24px; }
	.subtitle { color: #8890b1; margin: 0 0 16px; font-size: 14px; }

	.type-list { display: flex; flex-direction: column; gap: 8px; }

	.type-row {
		display: flex;
		justify-content: space-between;
		align-items: center;
		padding: 14px 18px;
		background: rgba(30, 36, 58, 0.8);
		border: 1px solid #2a2f47;
		border-radius: 10px;
		color: #cdd3ee;
		cursor: pointer;
		transition: all 0.2s;
		font-size: 14px;
	}

	.type-row:hover { border-color: var(--mc-grass); background: rgba(106, 176, 76, 0.08); }
	.type-name { font-weight: 600; }
	.type-category { font-size: 12px; color: #8890b1; text-transform: uppercase; letter-spacing: 0.05em; }

	.step-header { display: flex; align-items: center; gap: 12px; margin-bottom: 16px; }
	.back-btn {
		background: none; border: 1px solid #2a2f47; color: #8890b1;
		padding: 6px 12px; border-radius: 6px; cursor: pointer; font-size: 13px;
	}
	.back-btn:hover { color: #eef0f8; border-color: #3a3f5a; }
	.step-header h3 { margin: 0; font-size: 18px; }

	.warning-box {
		padding: 12px 16px;
		background: rgba(251, 191, 36, 0.1);
		border: 1px solid rgba(251, 191, 36, 0.3);
		border-radius: 8px;
		color: #fbbf24;
		font-size: 13px;
		margin-bottom: 16px;
	}

	.error-box {
		padding: 12px 16px;
		background: rgba(239, 68, 68, 0.1);
		border: 1px solid rgba(239, 68, 68, 0.3);
		border-radius: 8px;
		color: #ef4444;
		font-size: 13px;
		margin-bottom: 16px;
	}

	.success-box {
		padding: 12px 16px;
		background: rgba(106, 176, 76, 0.1);
		border: 1px solid rgba(106, 176, 76, 0.3);
		border-radius: 8px;
		color: #6ab04c;
		font-size: 13px;
		margin-bottom: 16px;
	}

	.version-search { margin-bottom: 12px; }
	.version-search input {
		width: 100%;
		padding: 10px 14px;
		background: #0e1220;
		border: 1px solid #2a2f47;
		border-radius: 8px;
		color: #eef0f8;
		font-size: 14px;
		outline: none;
	}
	.version-search input:focus { border-color: var(--mc-grass); }

	.version-grid {
		display: grid;
		grid-template-columns: repeat(3, 1fr);
		gap: 8px;
		margin-bottom: 12px;
	}

	.version-card {
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 4px;
		padding: 12px 8px;
		background: #0e1220;
		border: 2px solid #2a2f47;
		border-radius: 10px;
		cursor: pointer;
		color: #cdd3ee;
		transition: all 0.15s;
		font-size: 14px;
	}
	.version-card:hover { border-color: #3a3f5a; }
	.version-card.selected { border-color: var(--mc-grass); background: rgba(106, 176, 76, 0.08); }
	.ver-num { font-weight: 600; }

	.pagination { display: flex; justify-content: center; align-items: center; gap: 12px; margin-bottom: 16px; }
	.btn-page {
		padding: 6px 14px; background: rgba(30, 36, 58, 0.8); border: 1px solid #2a2f47;
		border-radius: 6px; color: #cdd3ee; cursor: pointer; font-size: 13px;
	}
	.btn-page:hover:not(:disabled) { border-color: var(--mc-grass); }
	.btn-page:disabled { opacity: 0.4; cursor: not-allowed; }
	.page-info { color: #8890b1; font-size: 13px; }

	.action-bar { display: flex; justify-content: flex-end; }
	.btn-primary {
		padding: 10px 20px; background: var(--mc-grass); color: white;
		border: none; border-radius: 8px; font-weight: 600; cursor: pointer; font-size: 14px;
	}
	.btn-primary:disabled { opacity: 0.5; cursor: not-allowed; }
	.btn-secondary {
		padding: 10px 20px; background: #2a2f47; color: #cdd3ee;
		border: none; border-radius: 8px; cursor: pointer; font-size: 14px;
	}

	.installing { text-align: center; }
	.installing h3 { margin: 16px 0; }

	.spinner {
		width: 40px; height: 40px;
		border: 3px solid #2a2f47;
		border-top-color: var(--mc-grass);
		border-radius: 50%;
		animation: spin 0.8s linear infinite;
		margin: 0 auto;
	}

	@keyframes spin { to { transform: rotate(360deg); } }

	.loading, .empty { color: #8890b1; text-align: center; padding: 20px 0; }
</style>
