<script lang="ts">
	import { goto, invalidateAll } from '$app/navigation';
	import * as api from '$lib/api/client';
	import type { PageData } from './$types';
	import type { ForgeVersion } from '$lib/api/types';
	import type { NeoForgeVersion } from '$lib/api/types';
	import CategorySelect, { type ServerCategory } from './steps/CategorySelect.svelte';
	import ImplementationSelect, { type Implementation } from './steps/ImplementationSelect.svelte';
	import VersionSelect from './steps/VersionSelect.svelte';
	import ServerName from './steps/ServerName.svelte';
	import Creating from './steps/Creating.svelte';

	let { data }: { data: PageData } = $props();

	// Wizard state
	type WizardStep = 'category' | 'implementation' | 'version' | 'name' | 'creating';
	let step = $state<WizardStep>('category');

	let category = $state<ServerCategory | null>(null);
	let implementation = $state<Implementation | 'vanilla' | 'bedrock' | 'template' | null>(null);
	let serverName = $state('');
	let createError = $state('');

	// Version selection state
	let selectedProfileId = $state('');
	let selectedMcVersion = $state('');
	let selectedLoaderVersion = $state('');
	let selectedForgeVersion = $state<ForgeVersion | null>(null);
	let selectedNeoForgeVersion = $state<NeoForgeVersion | null>(null);
	let cloneSource = $state('');

	// BuildTools state (for Spigot/CraftBukkit)
	let buildToolsRunId = $state('');

	// Creating state
	let installStreamUrl = $state('');
	let simpleProgress = $state(0);
	let simpleStepText = $state('');
	let createCompleted = $state(false);

	function selectCategory(cat: ServerCategory) {
		category = cat;
		// Categories that skip implementation selection
		if (cat === 'vanilla') {
			implementation = 'vanilla';
			step = 'version';
		} else if (cat === 'bedrock') {
			implementation = 'bedrock';
			step = 'version';
		} else if (cat === 'template') {
			implementation = 'template';
			step = 'version';
		} else {
			step = 'implementation';
		}
	}

	function selectImplementation(impl: Implementation) {
		implementation = impl;
		step = 'version';
	}

	function selectVersion(selection: Record<string, any>) {
		if (selection.profileId) selectedProfileId = selection.profileId;
		if (selection.minecraftVersion) selectedMcVersion = selection.minecraftVersion;
		if (selection.loaderVersion) selectedLoaderVersion = selection.loaderVersion;
		if (selection.forgeVersion) selectedForgeVersion = selection.forgeVersion;
		if (selection.neoForgeVersion) selectedNeoForgeVersion = selection.neoForgeVersion;
		if (selection.cloneSource) cloneSource = selection.cloneSource;
		step = 'name';
	}

	function goBackFromImpl() {
		step = 'category';
		category = null;
		implementation = null;
	}

	function goBackFromVersion() {
		if (category === 'plugins' || category === 'mods') {
			step = 'implementation';
			implementation = null;
		} else {
			step = 'category';
			category = null;
			implementation = null;
		}
	}

	function goBackFromName() {
		step = 'version';
	}

	async function createServer() {
		createError = '';
		const name = serverName.trim();
		if (!name || !implementation) return;

		step = 'creating';

		// Handle template/clone
		if (implementation === 'template' && cloneSource) {
			simpleStepText = 'Cloning server...';
			simpleProgress = 10;
			const result = await api.cloneServer(fetch, cloneSource, { newName: name });
			if (result.error) {
				createError = result.error;
				return;
			}
			simpleProgress = 100;
			createCompleted = true;
			return;
		}

		// Create the server first
		simpleStepText = 'Creating server...';
		simpleProgress = 5;
		const serverType = implementation === 'bedrock' ? 'bedrock' : 'java';
		const createResult = await api.createServer(fetch, {
			name,
			ownerUid: 1000,
			ownerGid: 1000,
			serverType
		});
		if (createResult.error) {
			createError = createResult.error;
			return;
		}

		// For modloaders, trigger installation
		if (implementation === 'forge' && selectedForgeVersion) {
			const result = await api.installForge(
				fetch,
				selectedMcVersion,
				selectedForgeVersion.forgeVersion,
				name
			);
			if (result.error) {
				createError = result.error;
				return;
			}
			if (result.data) {
				installStreamUrl = `/api/forge/install/${result.data.installId}/stream`;
			}
		} else if (implementation === 'neoforge' && selectedNeoForgeVersion) {
			const result = await api.installNeoForge(
				fetch,
				selectedMcVersion,
				selectedNeoForgeVersion.neoForgeVersion,
				name
			);
			if (result.error) {
				createError = result.error;
				return;
			}
			if (result.data) {
				installStreamUrl = `/api/neoforge/install/${result.data.installId}/stream`;
			}
		} else if (implementation === 'fabric') {
			const result = await api.installFabric(
				fetch,
				selectedMcVersion,
				selectedLoaderVersion,
				name
			);
			if (result.error) {
				createError = result.error;
				return;
			}
			if (result.data) {
				installStreamUrl = `/api/fabric/install/${result.data.installId}/stream`;
			}
		} else if (implementation === 'quilt') {
			const result = await api.installQuilt(
				fetch,
				selectedMcVersion,
				selectedLoaderVersion,
				name
			);
			if (result.error) {
				createError = result.error;
				return;
			}
			if (result.data) {
				installStreamUrl = `/api/quilt/install/${result.data.installId}/stream`;
			}
		} else if (selectedProfileId) {
			const needsBuildTools = implementation === 'spigot' || implementation === 'craftbukkit';

			if (needsBuildTools) {
				// Spigot/CraftBukkit require BuildTools to compile the server JAR
				const selectedProfile = data.profiles.data?.find((p) => p.id === selectedProfileId);
				const version = selectedProfile?.version ?? selectedMcVersion;
				// The BuildTools output profile ID follows this pattern
				selectedProfileId = `${implementation}-${version}`;

				const btResponse = await fetch('/api/host/profiles/buildtools', {
					method: 'POST',
					headers: { 'Content-Type': 'application/json' },
					body: JSON.stringify({ group: implementation, version })
				});

				if (btResponse.ok) {
					const btResult = await btResponse.json();
					buildToolsRunId = btResult.runId;
					installStreamUrl = `/api/host/buildtools/${btResult.runId}/stream`;
				} else {
					const err = await btResponse.json().catch(() => ({ error: 'Failed to start BuildTools' }));
					createError = err.error || 'Failed to start BuildTools';
				}
				return;
			}

			// Profile-based install (vanilla, paper, bedrock)
			simpleStepText = 'Downloading and configuring...';
			simpleProgress = 20;

			const profile = data.profiles.data?.find((p) => p.id === selectedProfileId);
			if (profile && !profile.downloaded) {
				const dlResult = await api.downloadProfile(fetch, selectedProfileId);
				if (dlResult.error) {
					createError = dlResult.error;
					return;
				}
			}

			simpleProgress = 60;
			simpleStepText = 'Copying server files...';

			const copyResult = await api.copyProfileToServer(fetch, selectedProfileId, name);
			if (copyResult.error) {
				createError = copyResult.error;
				return;
			}

			simpleProgress = 100;
			createCompleted = true;
			return;
		}

		// For modloaders, completion is handled by InstallProgress component
		if (!installStreamUrl) {
			simpleProgress = 100;
			createCompleted = true;
		}
	}

	function viewServer() {
		goto(`/servers/${encodeURIComponent(serverName.trim())}`);
	}
</script>

<svelte:head>
	<title>New Server | MineOS</title>
</svelte:head>

<div class="wizard">
	<div class="wizard-container">
		{#if step === 'category'}
			<CategorySelect onselect={selectCategory} />
		{:else if step === 'implementation' && (category === 'plugins' || category === 'mods')}
			<ImplementationSelect
				{category}
				onselect={selectImplementation}
				onback={goBackFromImpl}
			/>
		{:else if step === 'version' && implementation}
			<VersionSelect
				{implementation}
				profiles={data.profiles.data ?? []}
				servers={data.servers.data ?? []}
				onselect={selectVersion}
				onback={goBackFromVersion}
			/>
		{:else if step === 'name'}
			<ServerName
				value={serverName}
				error={createError}
				onchange={(v) => (serverName = v)}
				oncreate={createServer}
				onback={goBackFromName}
			/>
		{:else if step === 'creating'}
			<Creating
				implementation={implementation ?? 'unknown'}
				serverName={serverName}
				streamUrl={installStreamUrl || undefined}
				progress={simpleProgress}
				stepText={simpleStepText}
				completed={createCompleted}
				error={createError || undefined}
				onviewserver={viewServer}
			/>
		{/if}
	</div>
</div>

<style>
	.wizard {
		display: flex;
		justify-content: center;
		padding: 2rem;
	}

	.wizard-container {
		width: 100%;
		max-width: 640px;
	}
</style>
