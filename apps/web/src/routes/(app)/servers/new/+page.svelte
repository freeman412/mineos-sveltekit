<script lang="ts">
	import { goto, invalidateAll } from '$app/navigation';
	import type { PageData } from './$types';
	import * as api from '$lib/api/client';
	import type { CurseForgeSearchResult, ForgeVersion } from '$lib/api/types';
	import ProgressBar from '$lib/components/ProgressBar.svelte';
	import StatusBadge from '$lib/components/StatusBadge.svelte';

	let { data }: { data: PageData } = $props();

	// Server types available
	type ServerType = 'vanilla' | 'paper' | 'spigot' | 'craftbukkit' | 'forge' | 'curseforge' | 'bedrock' | 'clone';

	interface ServerTypeOption {
		id: ServerType;
		name: string;
		description: string;
		icon: string;
		color: string;
		features: string[];
	}

	const serverTypes: ServerTypeOption[] = [
		{
			id: 'vanilla',
			name: 'Vanilla',
			description: 'Official Minecraft server from Mojang',
			icon: '🎮',
			color: '#4ade80',
			features: ['Official', 'Pure gameplay', 'Most stable']
		},
		{
			id: 'paper',
			name: 'Paper',
			description: 'High-performance fork with plugin support',
			icon: '📄',
			color: '#60a5fa',
			features: ['Fast', 'Plugins', 'Optimized']
		},
		{
			id: 'spigot',
			name: 'Spigot',
			description: 'Popular server with extensive plugin ecosystem',
			icon: '🔧',
			color: '#fbbf24',
			features: ['Plugins', 'Community', 'Customizable']
		},
		{
			id: 'craftbukkit',
			name: 'CraftBukkit',
			description: 'Classic modded server platform',
			icon: '🪣',
			color: '#f97316',
			features: ['Classic', 'Reliable', 'Plugins']
		},
		{
			id: 'forge',
			name: 'Forge',
			description: 'Modded Minecraft with mod support',
			icon: '🔥',
			color: '#ef4444',
			features: ['Mods', 'Modpacks', 'Extensive']
		},
		{
			id: 'curseforge',
			name: 'CurseForge',
			description: 'Install modpacks directly from CurseForge',
			icon: '🎯',
			color: '#a855f7',
			features: ['Modpacks', 'Easy setup', 'Popular packs']
		},
		{
			id: 'bedrock',
			name: 'Bedrock',
			description: 'Official Bedrock Dedicated Server for cross-platform play',
			icon: '🪨',
			color: '#3b82f6',
			features: ['Cross-platform', 'Native binary', 'Mobile/Console']
		},
		{
			id: 'clone',
			name: 'Template',
			description: 'Clone an existing server as your starting point',
			icon: 'T',
			color: '#22d3ee',
			features: ['Duplicate', 'Fast setup', 'Preserve config']
		}
	];

	// Wizard state
	type WizardStep = 'type' | 'version' | 'name' | 'creating';
	let step = $state<WizardStep>('type');
	let selectedType = $state<ServerType | null>(null);
	let selectedProfileId = $state('');
	let serverName = $state('');
	let createError = $state('');
	let downloadingProfile = $state(false);
	let buildToolsRunning = $state(false);
	let buildToolsProgress = $state('');

	// Clone state
	let cloneSource = $state('');

	// CurseForge state
	let curseforgeQuery = $state('');
	let curseforgeSearching = $state(false);
	let curseforgeResults = $state<CurseForgeSearchResult | null>(null);
	let curseforgeError = $state('');
	let selectedModpack = $state<{ id: number; name: string; fileId?: number } | null>(null);

	// Forge state
	let forgeVersions = $state<ForgeVersion[]>([]);
	let forgeLoading = $state(false);
	let forgeError = $state('');
	let selectedForgeMcVersion = $state('');
	let selectedForgeVersion = $state<ForgeVersion | null>(null);
	let forgeInstallId = $state('');
	let forgeInstallProgress = $state(0);
	let forgeInstallStep = $state('');
	let forgeInstallOutput = $state('');
	let forgeOutputExpanded = $state(false);
	let forgeWatching = $state(false);
	let forgeInstallCompleted = $state(false);
	let forgeWatchError = $state('');

	// Modpack install state
	let modpackInstallJobId = $state('');
	let modpackInstallProgress = $state(0);
	let modpackInstallStep = $state('');
	let modpackInstallCurrentMod = $state('');
	let modpackInstallModIndex = $state(0);
	let modpackInstallTotalMods = $state(0);
	let modpackInstallOutput = $state<string[]>([]);
	let modpackOutputExpanded = $state(false);
	let modpackWatching = $state(false);
	let modpackInstallCompleted = $state(false);
	let modpackWatchError = $state('');
	let modpackStreamCleanup: (() => void) | null = null;

	// Filter profiles by selected type
	const filteredProfiles = $derived.by(() => {
		if (!data.profiles.data || !selectedType) return [];

		return data.profiles.data.filter((p) => {
			switch (selectedType) {
				case 'vanilla':
					return p.group === 'vanilla';
				case 'paper':
					return p.group === 'paper';
				case 'spigot':
					return p.group === 'spigot';
				case 'craftbukkit':
					return p.group === 'craftbukkit' || p.group === 'bukkit';
				case 'bedrock':
					return p.group === 'bedrock-server';
				default:
					return false;
			}
		});
	});

	// Available Forge Minecraft versions (from API)
	const forgeMinecraftVersions = $derived.by(() => {
		if (!forgeVersions.length) return [];
		const versions = [...new Set(forgeVersions.map(v => v.minecraftVersion))];
		return versions.sort((a, b) => {
			const [aMajor, aMinor, aPatch] = a.split('.').map(Number);
			const [bMajor, bMinor, bPatch] = b.split('.').map(Number);
			if (aMajor !== bMajor) return bMajor - aMajor;
			if (aMinor !== bMinor) return bMinor - aMinor;
			return (bPatch || 0) - (aPatch || 0);
		});
	});

	// Forge versions for selected Minecraft version
	const forgeVersionsForMc = $derived.by(() => {
		if (!selectedForgeMcVersion || !forgeVersions.length) return [];
		return forgeVersions.filter(v => v.minecraftVersion === selectedForgeMcVersion);
	});

	const cloneServers = $derived.by(() => {
		if (!data.servers?.data) return [];
		return [...data.servers.data].sort((a, b) => a.name.localeCompare(b.name));
	});

	async function selectServerType(type: ServerType) {
		selectedType = type;
		selectedProfileId = '';
		selectedModpack = null;
		curseforgeResults = null;
		createError = '';
		selectedForgeMcVersion = '';
		selectedForgeVersion = null;
		forgeError = '';
		serverName = '';
		cloneSource = '';

		// Auto-advance for types that need version selection
		if (type === 'curseforge') {
			step = 'version';
		} else if (type === 'clone') {
			step = 'version';
		} else if (type === 'forge') {
			step = 'version';
			// Fetch Forge versions if not already loaded
			if (forgeVersions.length === 0) {
				await loadForgeVersions();
			}
		} else if (['vanilla', 'paper', 'spigot', 'craftbukkit', 'bedrock'].includes(type)) {
			step = 'version';
		}
	}

	async function loadForgeVersions() {
		forgeLoading = true;
		forgeError = '';
		try {
			const result = await api.getForgeVersions(fetch);
			if (result.error) {
				forgeError = result.error;
			} else if (result.data) {
				forgeVersions = result.data;
			}
		} catch (err) {
			forgeError = err instanceof Error ? err.message : 'Failed to load Forge versions';
		} finally {
			forgeLoading = false;
		}
	}

	function selectForgeMcVersion(version: string) {
		selectedForgeMcVersion = version;
		selectedForgeVersion = null;
		// Auto-select recommended version if available
		const recommended = forgeVersionsForMc.find(v => v.isRecommended);
		if (recommended) {
			selectedForgeVersion = recommended;
		}
	}

	function selectForgeVersion(version: ForgeVersion) {
		selectedForgeVersion = version;
	}

	function selectProfile(profileId: string) {
		selectedProfileId = profileId;
	}

	type Variant = 'success' | 'error' | 'warning' | 'info' | 'neutral';

	function getStatusMeta(status?: string): { label: string; variant: Variant; pulse: boolean } {
		const value = (status ?? '').toLowerCase();
		if (value === 'running' || value === 'up') {
			return { label: 'Running', variant: 'success', pulse: true };
		}
		if (value === 'stopped' || value === 'down') {
			return { label: 'Stopped', variant: 'warning', pulse: false };
		}
		return { label: status || 'Unknown', variant: 'neutral', pulse: false };
	}

	async function searchCurseForge() {
		if (!curseforgeQuery.trim()) return;

		curseforgeSearching = true;
		curseforgeError = '';
		curseforgeResults = null;

		try {
			const result = await api.searchCurseForge(fetch, curseforgeQuery.trim(), 4471); // 4471 = modpacks
			if (result.error) {
				curseforgeError = result.error;
			} else if (result.data) {
				curseforgeResults = result.data;
			}
		} catch (err) {
			curseforgeError = err instanceof Error ? err.message : 'Search failed';
		} finally {
			curseforgeSearching = false;
		}
	}

	function selectModpack(modpack: { id: number; name: string; fileId?: number }) {
		selectedModpack = modpack;
	}

	function validateServerName(): boolean {
		if (!serverName.trim()) {
			createError = 'Server name is required';
			return false;
		}
		if (!/^[a-zA-Z0-9_\- ]+$/.test(serverName)) {
			createError = 'Server name can only contain letters, numbers, spaces, hyphens, and underscores';
			return false;
		}
		createError = '';
		return true;
	}

	function canProceedToName(): boolean {
		if (selectedType === 'curseforge') {
			return selectedModpack !== null;
		}
		if (selectedType === 'clone') {
			return cloneSource !== '';
		}
		if (selectedType === 'forge') {
			return selectedForgeVersion !== null;
		}
		return selectedProfileId !== '';
	}

	function goToNameStep() {
		if (canProceedToName()) {
			step = 'name';
		}
	}

	async function handleCreate() {
		if (!validateServerName()) return;
		await createServer();
	}

	async function createServer() {
		step = 'creating';
		createError = '';

		try {
			if (selectedType === 'clone') {
				if (!cloneSource) {
					throw new Error('Select a server to clone');
				}
				const result = await api.cloneServer(fetch, cloneSource, { newName: serverName.trim() });
				if (result.error) {
					throw new Error(result.error);
				}
				await invalidateAll();
				goto(`/servers/${encodeURIComponent(serverName.trim())}`);
				return;
			}

			// Handle BuildTools for Spigot/CraftBukkit
			if ((selectedType === 'spigot' || selectedType === 'craftbukkit') && selectedProfileId) {
				const profile = data.profiles.data?.find((p) => p.id === selectedProfileId);
				if (profile && !profile.downloaded && profile.type === 'buildtools') {
					buildToolsRunning = true;
					buildToolsProgress = 'Starting BuildTools...';
					// BuildTools logic would go here - for now just download
				}
			}

			// Download profile if needed
			if (selectedProfileId) {
				const profile = data.profiles.data?.find((p) => p.id === selectedProfileId);
				if (profile && !profile.downloaded) {
					downloadingProfile = true;
					const downloadResult = await fetch(`/api/host/profiles/${selectedProfileId}/download`, {
						method: 'POST'
					});
					downloadingProfile = false;

					if (!downloadResult.ok) {
						const error = await downloadResult
							.json()
							.catch(() => ({ error: 'Failed to download profile' }));
						throw new Error(error.error || 'Failed to download profile');
					}
				}
			}

			// Create the server
			const createResult = await api.createServer(fetch, {
				name: serverName.trim(),
				ownerUid: 1000,
				ownerGid: 1000,
				serverType: selectedType === 'bedrock' ? 'bedrock' : 'java'
			});

			if (createResult.error) {
				throw new Error(createResult.error);
			}

			// Copy profile to server if selected
			if (selectedProfileId) {
				const copyResult = await fetch(`/api/host/profiles/${selectedProfileId}/copy-to-server`, {
					method: 'POST',
					headers: { 'Content-Type': 'application/json' },
					body: JSON.stringify({ serverName: serverName.trim() })
				});

				if (!copyResult.ok) {
					const error = await copyResult
						.json()
						.catch(() => ({ error: 'Failed to copy profile to server' }));
					console.warn('Profile copy failed:', error);
				}
			}

			// Handle Forge installation
			if (selectedType === 'forge' && selectedForgeVersion) {
				forgeInstallStep = 'Starting Forge installation...';
				forgeInstallProgress = 0;
				forgeInstallCompleted = false;
				forgeWatchError = '';

				const installResult = await api.installForge(
					fetch,
					selectedForgeVersion.minecraftVersion,
					selectedForgeVersion.forgeVersion,
					serverName.trim()
				);

				if (installResult.error) {
					throw new Error(installResult.error);
				}

				if (installResult.data) {
					forgeInstallId = installResult.data.installId;
					await invalidateAll();
					return;
				}
			}

			// Handle CurseForge modpack installation
			if (selectedType === 'curseforge' && selectedModpack) {
				modpackInstallStep = 'Starting modpack installation...';
				modpackInstallProgress = 0;
				modpackInstallCompleted = false;
				modpackWatchError = '';
				modpackInstallOutput = [];

				const installResult = await api.installModpack(
					fetch,
					serverName.trim(),
					selectedModpack.id,
					selectedModpack.name,
					selectedModpack.fileId
				);

				if (installResult.error) {
					throw new Error(installResult.error);
				}

				if (installResult.data) {
					modpackInstallJobId = installResult.data.jobId;
					await invalidateAll();
					return;
				}
			}

			await invalidateAll();
			goto('/servers');
		} catch (err) {
			createError = err instanceof Error ? err.message : 'Failed to create server';
			step = 'name';
			buildToolsRunning = false;
			downloadingProfile = false;
			forgeInstallId = '';
			forgeWatching = false;
			forgeInstallCompleted = false;
			forgeWatchError = '';
		}
	}

	async function pollForgeInstallation(installId: string) {
		while (true) {
			const statusResult = await api.getForgeInstallStatus(fetch, installId);

			if (statusResult.error) {
				throw new Error(statusResult.error);
			}

			if (statusResult.data) {
				forgeInstallProgress = statusResult.data.progress;
				forgeInstallStep = statusResult.data.currentStep || 'Installing...';
				if (statusResult.data.output) {
					forgeInstallOutput = statusResult.data.output;
				}

				if (statusResult.data.status === 'completed') {
					return;
				}

				if (statusResult.data.status === 'failed') {
					throw new Error(statusResult.data.error || 'Forge installation failed');
				}
			}

			await new Promise((resolve) => setTimeout(resolve, 1000));
		}
	}

	function startForgeWatch() {
		if (!forgeInstallId || forgeWatching) return;
		forgeWatching = true;
		forgeWatchError = '';
		pollForgeInstallation(forgeInstallId)
			.then(() => {
				forgeInstallCompleted = true;
			})
			.catch((err) => {
				forgeWatchError = err instanceof Error ? err.message : 'Forge installation failed';
			});
	}

	function sendForgeToBackground() {
		forgeWatching = false;
		goto('/servers');
	}

	function startModpackWatch() {
		if (!modpackInstallJobId || modpackWatching) return;
		modpackWatching = true;
		modpackWatchError = '';

		modpackStreamCleanup = api.streamModpackInstall(
			serverName.trim(),
			modpackInstallJobId,
			(progress) => {
				modpackInstallProgress = progress.percentage;
				modpackInstallStep = progress.currentStep || 'Installing...';
				modpackInstallCurrentMod = progress.currentModName || '';
				modpackInstallModIndex = progress.currentModIndex;
				modpackInstallTotalMods = progress.totalMods;
				if (progress.outputLines.length > 0) {
					modpackInstallOutput = progress.outputLines;
				}
			},
			(error) => {
				modpackWatchError = error;
			},
			() => {
				modpackInstallCompleted = true;
			}
		);
	}

	function sendModpackToBackground() {
		if (modpackStreamCleanup) {
			modpackStreamCleanup();
			modpackStreamCleanup = null;
		}
		modpackWatching = false;
		goto('/servers');
	}

	function goToCreatedServer() {
		if (modpackStreamCleanup) {
			modpackStreamCleanup();
			modpackStreamCleanup = null;
		}
		goto(`/servers/${encodeURIComponent(serverName.trim())}`);
	}

	function goBack() {
		if (step === 'version') {
			step = 'type';
			selectedType = null;
		} else if (step === 'name') {
			step = 'version';
		}
	}

	function getSelectedTypeInfo(): ServerTypeOption | undefined {
		return serverTypes.find((t) => t.id === selectedType);
	}
</script>

<div class="page-container">
	<div class="page-header">
		<h1>Create New Server</h1>
		<p class="subtitle">Set up your perfect Minecraft server in just a few steps</p>
	</div>

	<div class="wizard-card">
		<!-- Progress Steps -->
		<div class="steps-indicator">
			<div class="step" class:active={step === 'type'} class:completed={step !== 'type'}>
				<div class="step-number">1</div>
				<div class="step-label">Server Type</div>
			</div>
			<div class="step-divider" class:completed={step !== 'type'}></div>
			<div class="step" class:active={step === 'version'} class:completed={step === 'name' || step === 'creating'}>
				<div class="step-number">2</div>
				<div class="step-label">{selectedType === 'curseforge' ? 'Modpack' : selectedType === 'clone' ? 'Template' : 'Version'}</div>
			</div>
			<div class="step-divider" class:completed={step === 'name' || step === 'creating'}></div>
			<div class="step" class:active={step === 'name'} class:completed={step === 'creating'}>
				<div class="step-number">3</div>
				<div class="step-label">Name</div>
			</div>
			<div class="step-divider" class:completed={step === 'creating'}></div>
			<div class="step" class:active={step === 'creating'}>
				<div class="step-number">4</div>
				<div class="step-label">Create</div>
			</div>
		</div>

		<div class="wizard-content">
			<!-- Step 1: Choose Server Type -->
			{#if step === 'type'}
				<div class="step-content">
					<h2>Choose Your Server Type</h2>
					<p class="step-description">
						Select the type of Minecraft server you want to create
					</p>

					<div class="server-type-grid">
						{#each serverTypes as type}
							<button
								class="type-card"
								class:selected={selectedType === type.id}
								onclick={() => selectServerType(type.id)}
								style="--card-color: {type.color}"
							>
								<div class="type-icon">{type.icon}</div>
								<div class="type-info">
									<h3>{type.name}</h3>
									<p>{type.description}</p>
									<div class="type-features">
										{#each type.features as feature}
											<span class="feature-tag">{feature}</span>
										{/each}
									</div>
								</div>
								{#if selectedType === type.id}
									<div class="selected-check">✓</div>
								{/if}
							</button>
						{/each}
					</div>
				</div>

			<!-- Step 2: Choose Version/Modpack -->
			{:else if step === 'version'}
				<div class="step-content">
					{#if selectedType === 'curseforge'}
						<!-- CurseForge Modpack Search -->
						<h2>Find a Modpack</h2>
						<p class="step-description">
							Search for modpacks on CurseForge
						</p>

						<div class="search-section">
							<div class="search-row">
								<input
									type="text"
									placeholder="Search modpacks (e.g., All the Mods, SkyFactory, RLCraft)..."
									bind:value={curseforgeQuery}
									onkeydown={(e) => e.key === 'Enter' && searchCurseForge()}
									class="search-input"
								/>
								<button
									class="btn-primary"
									onclick={searchCurseForge}
									disabled={curseforgeSearching || !curseforgeQuery.trim()}
								>
									{curseforgeSearching ? 'Searching...' : 'Search'}
								</button>
							</div>

							{#if curseforgeError}
								<div class="error-box">{curseforgeError}</div>
							{/if}

							{#if curseforgeResults && curseforgeResults.results.length > 0}
								<div class="modpack-grid">
									{#each curseforgeResults.results as modpack}
										<button
											class="modpack-card"
											class:selected={selectedModpack?.id === modpack.id}
											onclick={() => selectModpack({ id: modpack.id, name: modpack.name, fileId: modpack.latestFileId ?? undefined })}
										>
											{#if modpack.logoUrl}
												<img src={modpack.logoUrl} alt={modpack.name} class="modpack-thumb" />
											{:else}
												<div class="modpack-thumb placeholder">📦</div>
											{/if}
											<div class="modpack-info">
												<h4>{modpack.name}</h4>
												<p>{modpack.summary}</p>
												<span class="download-count">{(modpack.downloadCount / 1000000).toFixed(1)}M downloads</span>
											</div>
											{#if selectedModpack?.id === modpack.id}
												<div class="selected-check">✓</div>
											{/if}
										</button>
									{/each}
								</div>
							{:else if curseforgeResults && curseforgeResults.results.length === 0}
								<div class="empty-results">
									<p>No modpacks found. Try a different search term.</p>
								</div>
							{/if}
						</div>

					{:else if selectedType === 'clone'}
						<h2>Select Template</h2>
						<p class="step-description">
							Choose an existing server to clone
						</p>

						{#if !data.servers}
							<div class="error-box">Failed to load servers.</div>
						{:else if data.servers.error}
							<div class="error-box">{data.servers.error}</div>
						{:else if data.servers.data && data.servers.data.length === 0}
							<div class="empty-results">
								<p>No servers available to clone yet.</p>
							</div>
						{:else if data.servers.data}
							<div class="template-grid">
								{#each cloneServers as server}
									{@const statusMeta = getStatusMeta(server.status)}
									<button
										class="template-card"
										class:selected={cloneSource === server.name}
										onclick={() => (cloneSource = server.name)}
									>
										<div class="template-info">
											<h4>{server.name}</h4>
											<p>Ready to duplicate</p>
										</div>
										<StatusBadge variant={statusMeta.variant} size="sm" pulse={statusMeta.pulse}>
											{statusMeta.label}
										</StatusBadge>
									</button>
								{/each}
							</div>
						{/if}

					{:else if selectedType === 'forge'}
						<!-- Forge Version Selection -->
						<h2>Choose Forge Version</h2>
						<p class="step-description">
							First select a Minecraft version, then choose a Forge build
						</p>

						{#if forgeLoading}
							<div class="loading-section">
								<div class="spinner small"></div>
								<span>Loading Forge versions...</span>
							</div>
						{:else if forgeError}
							<div class="error-box">{forgeError}</div>
							<button class="btn-secondary" onclick={loadForgeVersions}>Try Again</button>
						{:else if forgeMinecraftVersions.length === 0}
							<div class="empty-results">
								<p>No Forge versions available. Try again later.</p>
							</div>
						{:else}
							<div class="forge-selector">
								<!-- Minecraft Version Column -->
								<div class="forge-column">
									<h3>Minecraft Version</h3>
									<div class="forge-version-list">
										{#each forgeMinecraftVersions as version}
											<button
												class="forge-version-item"
												class:selected={selectedForgeMcVersion === version}
												onclick={() => selectForgeMcVersion(version)}
											>
												<span class="version-number">{version}</span>
												{#if forgeVersions.some(v => v.minecraftVersion === version && v.isRecommended)}
													<span class="badge recommended">Recommended</span>
												{/if}
											</button>
										{/each}
									</div>
								</div>

								<!-- Forge Build Column -->
								<div class="forge-column">
									<h3>Forge Build</h3>
									{#if !selectedForgeMcVersion}
										<div class="empty-column">
											<p>Select a Minecraft version first</p>
										</div>
									{:else if forgeVersionsForMc.length === 0}
										<div class="empty-column">
											<p>No Forge builds available for {selectedForgeMcVersion}</p>
										</div>
									{:else}
										<div class="forge-version-list">
											{#each forgeVersionsForMc as version}
												<button
													class="forge-version-item"
													class:selected={selectedForgeVersion?.forgeVersion === version.forgeVersion}
													onclick={() => selectForgeVersion(version)}
												>
													<span class="forge-build">{version.forgeVersion}</span>
													<div class="forge-badges">
														{#if version.isRecommended}
															<span class="badge recommended">Recommended</span>
														{/if}
														{#if version.isLatest}
															<span class="badge latest">Latest</span>
														{/if}
													</div>
												</button>
											{/each}
										</div>
									{/if}
								</div>
							</div>

							{#if selectedForgeVersion}
								<div class="forge-selection-summary">
									<span class="summary-label">Selected:</span>
									<span class="summary-value">Minecraft {selectedForgeVersion.minecraftVersion} with Forge {selectedForgeVersion.forgeVersion}</span>
								</div>
							{/if}
						{/if}

					{:else}
						<!-- Standard Version Selection -->
						<h2>Choose {getSelectedTypeInfo()?.name} Version</h2>
						<p class="step-description">
							Select a Minecraft version for your server
						</p>

						{#if filteredProfiles.length === 0}
							<div class="empty-results">
								<p>No profiles available for this server type.</p>
							</div>
						{:else}
							<div class="version-grid">
								{#each filteredProfiles as profile}
									<button
										class="version-card"
										class:selected={selectedProfileId === profile.id}
										class:downloaded={profile.downloaded}
										onclick={() => selectProfile(profile.id)}
									>
										<span class="version-number">{profile.version}</span>
										<div class="version-meta">
											{#if profile.downloaded}
												<StatusBadge variant="success" size="sm">Ready</StatusBadge>
											{:else}
												<StatusBadge variant="info" size="sm">Will Download</StatusBadge>
											{/if}
										</div>
										{#if selectedProfileId === profile.id}
											<div class="selected-check">✓</div>
										{/if}
									</button>
								{/each}
							</div>
						{/if}
					{/if}
				</div>

			<!-- Step 3: Server Name -->
			{:else if step === 'name'}
				<div class="step-content">
					<h2>Name Your Server</h2>
					<p class="step-description">
						Choose a name to identify your server in the dashboard
					</p>

					<div class="name-section">
						<div class="selected-summary">
							<span class="summary-icon">{getSelectedTypeInfo()?.icon}</span>
							<div class="summary-details">
								<strong>{getSelectedTypeInfo()?.name}</strong>
								{#if selectedType === 'curseforge' && selectedModpack}
									<span>{selectedModpack.name}</span>
								{:else if selectedType === 'forge' && selectedForgeVersion}
									<span>Minecraft {selectedForgeVersion.minecraftVersion} - Forge {selectedForgeVersion.forgeVersion}</span>
								{:else if selectedType === 'clone' && cloneSource}
									<span>Template: {cloneSource}</span>
								{:else if selectedProfileId}
									{@const profile = data.profiles.data?.find(p => p.id === selectedProfileId)}
									{#if profile}
										<span>Version {profile.version}</span>
									{/if}
								{/if}
							</div>
						</div>

						<div class="form-field">
							<label for="server-name">Server Name</label>
				<!-- svelte-ignore a11y_autofocus -->
							<input
								type="text"
								id="server-name"
								bind:value={serverName}
								placeholder="my-awesome-server"
								autofocus
								onkeydown={(e: KeyboardEvent) => e.key === 'Enter' && handleCreate()}
							/>
							{#if createError}
								<span class="error-text">{createError}</span>
							{/if}
						</div>
					</div>
				</div>

			<!-- Step 4: Creating -->
			{:else if step === 'creating'}
				<div class="step-content creating">
					<div class="spinner"></div>
					<h2>
						{#if buildToolsRunning}
							Building with BuildTools...
						{:else if downloadingProfile}
							Downloading server JAR...
						{:else if forgeInstallId}
							Installing Forge...
						{:else if modpackInstallJobId}
							Installing Modpack...
						{:else}
							Creating your server...
						{/if}
					</h2>
					<p class="step-description">
						{#if buildToolsRunning}
							{buildToolsProgress || 'This may take several minutes'}
						{:else if forgeInstallId}
							{forgeInstallStep || 'Preparing installation...'}
						{:else if modpackInstallJobId}
							{modpackInstallStep || 'Preparing modpack installation...'}
						{:else}
							This will only take a moment
						{/if}
					</p>

					<!-- Modpack installation progress -->
					{#if modpackInstallJobId}
						<p class="step-hint">
							Modpack installation runs in the background. You can leave this page and track it in Notifications.
						</p>
						{#if modpackInstallTotalMods > 0}
							<div class="mod-progress-info">
								<span class="mod-counter">Mod {modpackInstallModIndex} of {modpackInstallTotalMods}</span>
								{#if modpackInstallCurrentMod}
									<span class="current-mod">{modpackInstallCurrentMod}</span>
								{/if}
							</div>
						{/if}
						{#if modpackInstallProgress > 0}
							<ProgressBar value={modpackInstallProgress} color="blue" size="md" showLabel />
						{/if}
						<div class="install-actions">
							{#if !modpackWatching && !modpackInstallCompleted}
								<button class="btn-secondary" onclick={startModpackWatch}>
									Stay and watch
								</button>
							{/if}
							<button class="btn-primary" onclick={sendModpackToBackground}>
								{modpackInstallCompleted ? 'Complete Install' : 'Send to background'}
							</button>
						</div>
						{#if modpackInstallCompleted}
							<div class="success-box">
								Modpack installed successfully!
								<button class="btn-link" onclick={goToCreatedServer}>Go to server</button>
							</div>
						{:else if modpackWatchError}
							<div class="error-box">{modpackWatchError}</div>
						{/if}
						{#if modpackInstallOutput.length > 0}
							<div class="output-section">
								<button
									class="output-toggle"
									onclick={() => modpackOutputExpanded = !modpackOutputExpanded}
								>
									<span class="toggle-icon">{modpackOutputExpanded ? '▼' : '▶'}</span>
									<span>Installation Log ({modpackInstallOutput.length} lines)</span>
								</button>
								{#if modpackOutputExpanded}
									<div class="output-content">
										<pre>{modpackInstallOutput.join('\n')}</pre>
									</div>
								{/if}
							</div>
						{/if}
					{/if}

					<!-- Forge installation progress -->
					{#if forgeInstallId}
						<p class="step-hint">
							Forge install runs in the background. You can leave this page and track it in
							Notifications.
						</p>
					{/if}
					{#if forgeInstallId && forgeInstallProgress > 0}
						<ProgressBar value={forgeInstallProgress} color="green" size="md" showLabel />
					{/if}
					{#if forgeInstallId}
						<div class="install-actions">
							{#if !forgeWatching && !forgeInstallCompleted}
								<button class="btn-secondary" onclick={startForgeWatch}>
									Stay and watch
								</button>
							{/if}
							<button class="btn-primary" onclick={sendForgeToBackground}>
								{forgeInstallCompleted ? 'Complete Install' : 'Send to background'}
							</button>
						</div>
					{/if}
					{#if forgeInstallCompleted}
						<div class="success-box">Forge install completed.</div>
					{:else if forgeWatchError}
						<div class="error-box">{forgeWatchError}</div>
					{/if}
					{#if forgeInstallId && forgeInstallOutput}
						<div class="output-section">
							<button
								class="output-toggle"
								onclick={() => forgeOutputExpanded = !forgeOutputExpanded}
							>
								<span class="toggle-icon">{forgeOutputExpanded ? '▼' : '▶'}</span>
								<span>Installer Output</span>
							</button>
							{#if forgeOutputExpanded}
								<div class="output-content">
									<pre>{forgeInstallOutput}</pre>
								</div>
							{/if}
						</div>
					{/if}
				</div>
			{/if}
		</div>

		<!-- Navigation Buttons -->
		<div class="wizard-actions">
			{#if step === 'type'}
				<a href="/servers" class="btn-secondary">Cancel</a>
				<div></div>
			{:else if step === 'version'}
				<button class="btn-secondary" onclick={goBack}>Back</button>
				<button
					class="btn-primary"
					onclick={goToNameStep}
					disabled={!canProceedToName()}
				>
					Next
				</button>
			{:else if step === 'name'}
				<button class="btn-secondary" onclick={goBack}>Back</button>
				<button class="btn-primary" onclick={handleCreate} disabled={!serverName.trim()}>
					Create Server
				</button>
			{/if}
		</div>
	</div>
</div>

<style>
	.page-container {
		max-width: 1100px;
		margin: 0 auto;
	}

	.page-header {
		margin-bottom: 32px;
	}

	h1 {
		margin: 0 0 8px;
		font-size: 32px;
		font-weight: 600;
	}

	.subtitle {
		margin: 0;
		color: #aab2d3;
		font-size: 15px;
	}

	.template-grid {
		display: grid;
		grid-template-columns: repeat(auto-fill, minmax(260px, 1fr));
		gap: 14px;
	}

	.template-card {
		background: #141827;
		border: 2px solid #2a2f47;
		border-radius: 12px;
		padding: 16px;
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 12px;
		cursor: pointer;
		transition: all 0.2s;
		text-align: left;
		font-family: inherit;
	}

	.template-card:hover {
		border-color: #22d3ee;
		background: rgba(34, 211, 238, 0.08);
	}

	.template-card.selected {
		border-color: #22d3ee;
		background: rgba(34, 211, 238, 0.12);
	}

	.template-info h4 {
		margin: 0 0 4px;
		font-size: 16px;
		color: #eef0f8;
	}

	.template-info p {
		margin: 0;
		font-size: 12px;
		color: #9aa2c5;
	}

	.wizard-card {
		background: #1a1e2f;
		border-radius: 16px;
		padding: 32px;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.35);
	}

	/* Steps Indicator */
	.steps-indicator {
		display: flex;
		align-items: center;
		justify-content: center;
		margin-bottom: 40px;
	}

	.step {
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 8px;
	}

	.step-number {
		width: 40px;
		height: 40px;
		border-radius: 50%;
		background: #2b2f45;
		color: #9aa2c5;
		display: flex;
		align-items: center;
		justify-content: center;
		font-weight: 600;
		font-size: 16px;
		transition: all 0.3s;
	}

	.step.active .step-number {
		background: #5865f2;
		color: white;
		box-shadow: 0 0 20px rgba(88, 101, 242, 0.4);
	}

	.step.completed .step-number {
		background: rgba(122, 230, 141, 0.2);
		color: #7ae68d;
	}

	.step-label {
		font-size: 13px;
		color: #9aa2c5;
	}

	.step.active .step-label {
		color: #eef0f8;
		font-weight: 500;
	}

	.step-divider {
		width: 50px;
		height: 2px;
		background: #2b2f45;
		margin: 0 12px;
		margin-bottom: 28px;
		transition: background 0.3s;
	}

	.step-divider.completed {
		background: rgba(122, 230, 141, 0.3);
	}

	/* Content */
	.wizard-content {
		min-height: 450px;
	}

	.step-content {
		animation: fadeIn 0.3s;
	}

	.step-content.creating {
		display: flex;
		flex-direction: column;
		align-items: center;
		justify-content: center;
		min-height: 450px;
	}

	@keyframes fadeIn {
		from {
			opacity: 0;
			transform: translateY(10px);
		}
		to {
			opacity: 1;
			transform: translateY(0);
		}
	}

	.step-content h2 {
		margin: 0 0 8px;
		font-size: 24px;
	}

	.step-description {
		margin: 0 0 28px;
		color: #9aa2c5;
		font-size: 14px;
	}

	.step-hint {
		margin: -18px 0 24px;
		color: #7f88ac;
		font-size: 13px;
		text-align: center;
		max-width: 520px;
	}

	.install-actions {
		display: flex;
		gap: 12px;
		align-items: center;
		justify-content: center;
		margin: 0 0 18px;
		flex-wrap: wrap;
	}

	.mod-progress-info {
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 4px;
		margin-bottom: 16px;
	}

	.mod-counter {
		font-size: 14px;
		font-weight: 600;
		color: #a855f7;
	}

	.current-mod {
		font-size: 12px;
		color: #9aa2c5;
		max-width: 400px;
		text-align: center;
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
	}

	.btn-link {
		background: none;
		border: none;
		color: #5865f2;
		text-decoration: underline;
		cursor: pointer;
		font-family: inherit;
		font-size: 14px;
		padding: 4px 8px;
		margin-left: 8px;
	}

	.btn-link:hover {
		color: #4752c4;
	}

	.success-box {
		background: rgba(106, 176, 76, 0.1);
		border: 1px solid rgba(106, 176, 76, 0.3);
		border-radius: 12px;
		padding: 14px 18px;
		color: #b7f5a2;
		margin: 0 0 18px;
	}

	/* Server Type Grid */
	.server-type-grid {
		display: grid;
		grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
		gap: 16px;
	}

	.type-card {
		background: #141827;
		border: 2px solid #2a2f47;
		border-radius: 14px;
		padding: 20px;
		cursor: pointer;
		transition: all 0.2s;
		display: flex;
		align-items: flex-start;
		gap: 16px;
		text-align: left;
		font-family: inherit;
		position: relative;
		overflow: hidden;
	}

	.type-card::before {
		content: '';
		position: absolute;
		top: 0;
		left: 0;
		right: 0;
		height: 3px;
		background: var(--card-color);
		opacity: 0;
		transition: opacity 0.2s;
	}

	.type-card:hover {
		border-color: var(--card-color);
		background: rgba(255, 255, 255, 0.02);
	}

	.type-card:hover::before {
		opacity: 1;
	}

	.type-card.selected {
		border-color: var(--card-color);
		background: rgba(255, 255, 255, 0.03);
	}

	.type-card.selected::before {
		opacity: 1;
	}

	.type-icon {
		font-size: 36px;
		flex-shrink: 0;
	}

	.type-info {
		flex: 1;
		min-width: 0;
	}

	.type-info h3 {
		margin: 0 0 4px;
		font-size: 18px;
		font-weight: 600;
		color: #eef0f8;
	}

	.type-info p {
		margin: 0 0 12px;
		font-size: 13px;
		color: #9aa2c5;
	}

	.type-features {
		display: flex;
		flex-wrap: wrap;
		gap: 6px;
	}

	.feature-tag {
		background: rgba(255, 255, 255, 0.08);
		padding: 3px 10px;
		border-radius: 999px;
		font-size: 11px;
		color: #d4d9f1;
	}

	.selected-check {
		position: absolute;
		top: 12px;
		right: 12px;
		width: 24px;
		height: 24px;
		background: #5865f2;
		border-radius: 50%;
		display: flex;
		align-items: center;
		justify-content: center;
		color: white;
		font-size: 14px;
		font-weight: bold;
	}

	/* Version Grid */
	.version-grid {
		display: grid;
		grid-template-columns: repeat(auto-fill, minmax(140px, 1fr));
		gap: 12px;
	}

	.version-card {
		background: #141827;
		border: 2px solid #2a2f47;
		border-radius: 12px;
		padding: 16px;
		cursor: pointer;
		transition: all 0.2s;
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 8px;
		font-family: inherit;
		position: relative;
	}

	.version-card:hover {
		border-color: #5865f2;
		background: rgba(88, 101, 242, 0.05);
	}

	.version-card.selected {
		border-color: #5865f2;
		background: rgba(88, 101, 242, 0.1);
	}

	.version-number {
		font-size: 20px;
		font-weight: 600;
		color: #eef0f8;
	}

	.version-meta {
		display: flex;
		align-items: center;
		gap: 8px;
	}

	/* Search Section */
	.search-section {
		display: flex;
		flex-direction: column;
		gap: 20px;
	}

	.search-row {
		display: flex;
		gap: 12px;
	}

	.search-input {
		flex: 1;
		background: #141827;
		border: 1px solid #2a2f47;
		border-radius: 10px;
		padding: 14px 18px;
		color: #eef0f8;
		font-family: inherit;
		font-size: 15px;
	}

	.search-input:focus {
		outline: none;
		border-color: #5865f2;
	}

	/* Modpack Grid */
	.modpack-grid {
		display: grid;
		grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
		gap: 16px;
		max-height: 400px;
		overflow-y: auto;
		padding: 4px;
	}

	.modpack-card {
		background: #141827;
		border: 2px solid #2a2f47;
		border-radius: 12px;
		padding: 16px;
		cursor: pointer;
		transition: all 0.2s;
		display: flex;
		gap: 14px;
		text-align: left;
		font-family: inherit;
		position: relative;
	}

	.modpack-card:hover {
		border-color: #a855f7;
		background: rgba(168, 85, 247, 0.05);
	}

	.modpack-card.selected {
		border-color: #a855f7;
		background: rgba(168, 85, 247, 0.1);
	}

	.modpack-thumb {
		width: 64px;
		height: 64px;
		border-radius: 8px;
		object-fit: cover;
		flex-shrink: 0;
	}

	.modpack-thumb.placeholder {
		background: #2b2f45;
		display: flex;
		align-items: center;
		justify-content: center;
		font-size: 24px;
	}

	.modpack-info {
		flex: 1;
		min-width: 0;
	}

	.modpack-info h4 {
		margin: 0 0 4px;
		font-size: 15px;
		font-weight: 600;
		color: #eef0f8;
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
	}

	.modpack-info p {
		margin: 0 0 8px;
		font-size: 12px;
		color: #9aa2c5;
		display: -webkit-box;
		-webkit-line-clamp: 2;
		line-clamp: 2;
		-webkit-box-orient: vertical;
		overflow: hidden;
	}

	.download-count {
		font-size: 11px;
		color: #7c85a8;
	}

	/* Name Section */
	.name-section {
		max-width: 500px;
	}

	.selected-summary {
		display: flex;
		align-items: center;
		gap: 14px;
		background: #141827;
		border-radius: 12px;
		padding: 16px 20px;
		margin-bottom: 24px;
	}

	.summary-icon {
		font-size: 32px;
	}

	.summary-details {
		display: flex;
		flex-direction: column;
		gap: 2px;
	}

	.summary-details strong {
		font-size: 16px;
		color: #eef0f8;
	}

	.summary-details span {
		font-size: 13px;
		color: #9aa2c5;
	}

	.form-field {
		display: flex;
		flex-direction: column;
		gap: 8px;
	}

	.form-field label {
		font-size: 14px;
		font-weight: 500;
		color: #aab2d3;
	}

	.form-field input {
		background: #141827;
		border: 1px solid #2a2f47;
		border-radius: 10px;
		padding: 14px 18px;
		color: #eef0f8;
		font-family: inherit;
		font-size: 16px;
		transition: border-color 0.2s;
	}

	.form-field input:focus {
		outline: none;
		border-color: #5865f2;
	}

	.error-text {
		color: #ff9f9f;
		font-size: 13px;
	}

	.error-box {
		background: rgba(255, 92, 92, 0.1);
		border: 1px solid rgba(255, 92, 92, 0.3);
		border-radius: 12px;
		padding: 14px 18px;
		color: #ff9f9f;
	}

	.empty-results {
		text-align: center;
		padding: 40px 20px;
		color: #9aa2c5;
	}

	/* Loading Section */
	.loading-section {
		display: flex;
		align-items: center;
		justify-content: center;
		gap: 16px;
		padding: 60px 20px;
		color: #9aa2c5;
	}

	/* Forge Selector */
	.forge-selector {
		display: grid;
		grid-template-columns: 1fr 1fr;
		gap: 24px;
		margin-bottom: 20px;
	}

	.forge-column {
		background: #141827;
		border-radius: 12px;
		padding: 16px;
	}

	.forge-column h3 {
		margin: 0 0 12px;
		font-size: 14px;
		font-weight: 600;
		color: #aab2d3;
		text-transform: uppercase;
		letter-spacing: 0.05em;
	}

	.forge-version-list {
		display: flex;
		flex-direction: column;
		gap: 6px;
		max-height: 350px;
		overflow-y: auto;
	}

	.forge-version-item {
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 12px;
		background: #1a1e2f;
		border: 1px solid #2a2f47;
		border-radius: 8px;
		padding: 12px 16px;
		cursor: pointer;
		transition: all 0.2s;
		font-family: inherit;
		text-align: left;
	}

	.forge-version-item:hover {
		border-color: #ef4444;
		background: rgba(239, 68, 68, 0.05);
	}

	.forge-version-item.selected {
		border-color: #ef4444;
		background: rgba(239, 68, 68, 0.1);
	}

	.forge-version-item .version-number {
		font-size: 16px;
		font-weight: 600;
		color: #eef0f8;
	}

	.forge-build {
		font-size: 14px;
		font-weight: 500;
		color: #eef0f8;
		font-family: 'JetBrains Mono', monospace;
	}

	.forge-badges {
		display: flex;
		gap: 6px;
	}

	.badge {
		padding: 2px 8px;
		border-radius: 999px;
		font-size: 10px;
		font-weight: 600;
	}

	.badge.recommended {
		background: rgba(34, 197, 94, 0.2);
		color: #22c55e;
	}

	.badge.latest {
		background: rgba(59, 130, 246, 0.2);
		color: #3b82f6;
	}

	.empty-column {
		display: flex;
		align-items: center;
		justify-content: center;
		min-height: 200px;
		color: #7c85a8;
		font-size: 14px;
	}

	.forge-selection-summary {
		display: flex;
		align-items: center;
		gap: 10px;
		background: rgba(239, 68, 68, 0.1);
		border: 1px solid rgba(239, 68, 68, 0.3);
		border-radius: 10px;
		padding: 12px 16px;
	}

	.summary-label {
		font-size: 13px;
		color: #9aa2c5;
	}

	.summary-value {
		font-size: 14px;
		font-weight: 500;
		color: #eef0f8;
	}

	/* Output Section */
	.output-section {
		margin-top: 24px;
		width: 100%;
		max-width: 600px;
	}

	.output-toggle {
		display: flex;
		align-items: center;
		gap: 8px;
		background: #1a1e2f;
		border: 1px solid #2a2f47;
		border-radius: 8px;
		padding: 10px 14px;
		color: #9aa2c5;
		font-family: inherit;
		font-size: 13px;
		cursor: pointer;
		transition: all 0.2s;
		width: 100%;
	}

	.output-toggle:hover {
		border-color: #5865f2;
		color: #eef0f8;
	}

	.toggle-icon {
		font-size: 10px;
		transition: transform 0.2s;
	}

	.output-content {
		margin-top: 8px;
		background: #0d1017;
		border: 1px solid #2a2f47;
		border-radius: 8px;
		padding: 12px;
		max-height: 300px;
		overflow-y: auto;
		scrollbar-width: thin;
		scrollbar-color: #3a3f5a #1a1e2f;
	}

	.output-content::-webkit-scrollbar {
		width: 8px;
	}

	.output-content::-webkit-scrollbar-track {
		background: #1a1e2f;
		border-radius: 4px;
	}

	.output-content::-webkit-scrollbar-thumb {
		background: #3a3f5a;
		border-radius: 4px;
	}

	.output-content::-webkit-scrollbar-thumb:hover {
		background: #4a5070;
	}

	.output-content pre {
		margin: 0;
		font-family: 'JetBrains Mono', 'Consolas', monospace;
		font-size: 11px;
		line-height: 1.5;
		color: #9aa2c5;
		white-space: pre-wrap;
		word-break: break-word;
	}

	/* Spinner */
	.spinner {
		width: 48px;
		height: 48px;
		border: 4px solid #2b2f45;
		border-top-color: #5865f2;
		border-radius: 50%;
		animation: spin 0.8s linear infinite;
		margin-bottom: 24px;
	}

	.spinner.small {
		width: 24px;
		height: 24px;
		border-width: 3px;
		margin-bottom: 0;
	}

	@keyframes spin {
		to {
			transform: rotate(360deg);
		}
	}

	/* Actions */
	.wizard-actions {
		display: flex;
		gap: 12px;
		justify-content: space-between;
		margin-top: 32px;
		padding-top: 24px;
		border-top: 1px solid #2a2f47;
	}

	.btn-primary {
		background: #5865f2;
		color: white;
		border: none;
		border-radius: 10px;
		padding: 12px 28px;
		font-family: inherit;
		font-size: 15px;
		font-weight: 600;
		cursor: pointer;
		transition: background 0.2s;
	}

	.btn-primary:hover:not(:disabled) {
		background: #4752c4;
	}

	.btn-primary:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.btn-secondary {
		background: #2b2f45;
		color: #d4d9f1;
		border: none;
		border-radius: 10px;
		padding: 12px 24px;
		font-family: inherit;
		font-size: 15px;
		font-weight: 500;
		cursor: pointer;
		transition: background 0.2s;
		text-decoration: none;
	}

	.btn-secondary:hover {
		background: #3a3f5a;
	}

	/* Responsive */
	@media (max-width: 768px) {
		.template-grid {
			grid-template-columns: 1fr;
		}

		.wizard-card {
			padding: 24px 20px;
		}

		.steps-indicator {
			overflow-x: auto;
			justify-content: flex-start;
			padding-bottom: 10px;
		}

		.step-divider {
			width: 30px;
			min-width: 30px;
		}

		.step-label {
			font-size: 11px;
			white-space: nowrap;
		}

		.server-type-grid {
			grid-template-columns: 1fr;
		}

		.version-grid {
			grid-template-columns: repeat(2, 1fr);
		}

		.modpack-grid {
			grid-template-columns: 1fr;
		}

		.search-row {
			flex-direction: column;
		}

		.forge-selector {
			grid-template-columns: 1fr;
		}

		.forge-version-list {
			max-height: 250px;
		}

		.wizard-actions {
			flex-direction: column-reverse;
		}
	}
</style>
