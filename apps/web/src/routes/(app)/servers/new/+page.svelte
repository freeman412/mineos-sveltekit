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

	// Implementation step selection (for Next button)
	let selectedImpl = $state<Implementation | null>(null);

	// Version step readiness
	let versionConfirm: (() => void) | null = $state(null);

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
		selectedImpl = null;
	}

	function goBackFromVersion() {
		versionConfirm = null;
		if (category === 'plugins' || category === 'mods') {
			step = 'implementation';
			implementation = null;
			selectedImpl = null;
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
				step = 'name';
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
			step = 'name';
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
				step = 'name';
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
				step = 'name';
				return;
			}
			if (result.data) {
				installStreamUrl = `/api/neoforge/install/${result.data.installId}/stream`;
			}
		} else if (implementation === 'fabric') {
			simpleStepText = 'Downloading Fabric server JAR...';
			simpleProgress = 20;
			const result = await api.installFabric(
				fetch,
				selectedMcVersion,
				selectedLoaderVersion,
				name
			);
			if (result.error) {
				createError = result.error;
				step = 'name';
				return;
			}
			// Fabric downloads a single JAR — wait for completion
			if (result.data) {
				simpleProgress = 50;
				await waitForInstall(`/api/fabric/install/${result.data.installId}`);
			}
		} else if (implementation === 'quilt') {
			simpleStepText = 'Downloading Quilt server JAR...';
			simpleProgress = 20;
			const result = await api.installQuilt(
				fetch,
				selectedMcVersion,
				selectedLoaderVersion,
				name
			);
			if (result.error) {
				createError = result.error;
				step = 'name';
				return;
			}
			if (result.data) {
				simpleProgress = 50;
				await waitForInstall(`/api/quilt/install/${result.data.installId}`);
			}
		} else if (selectedProfileId) {
			const needsBuildTools = implementation === 'spigot';

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
					step = 'name';
					return;
				}
			}

			simpleProgress = 60;
			simpleStepText = 'Copying server files...';

			const copyResult = await api.copyProfileToServer(fetch, selectedProfileId, name);
			if (copyResult.error) {
				createError = copyResult.error;
				step = 'name';
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

	const stepNumber = $derived(
		step === 'category' ? 1 : step === 'implementation' ? 2 : step === 'version' ? 2 : step === 'name' ? 3 : 4
	);

	/** Wait for a fast install to complete by watching the SSE stream inline */
	function waitForInstall(statusUrl: string): Promise<void> {
		return new Promise((resolve, reject) => {
			const streamUrl = statusUrl + '/stream';
			const source = new EventSource(streamUrl);
			const timeout = setTimeout(() => {
				source.close();
				// If we're still waiting, check status one last time
				fetch(statusUrl).then(async (res) => {
					if (!res.ok || res.status === 404) { resolve(); return; }
					const body = await res.json();
					const d = body.data ?? body;
					if (d.status === 'completed') resolve();
					else if (d.status === 'failed') reject(new Error(d.error || 'Install failed'));
					else resolve(); // Assume done
				}).catch(() => resolve());
			}, 30000);

			source.onmessage = (event) => {
				try {
					const data = JSON.parse(event.data);
					if (data.progress) simpleProgress = Math.max(simpleProgress, data.progress);
					if (data.currentStep) simpleStepText = data.currentStep;
					if (data.status === 'completed') {
						source.close();
						clearTimeout(timeout);
						simpleProgress = 100;
						createCompleted = true;
						resolve();
					} else if (data.status === 'failed') {
						source.close();
						clearTimeout(timeout);
						reject(new Error(data.error || 'Install failed'));
					}
				} catch {}
			};
			source.onerror = () => {
				source.close();
				clearTimeout(timeout);
				// Stream died — check status
				fetch(statusUrl).then(async (res) => {
					if (!res.ok || res.status === 404) {
						simpleProgress = 100;
						createCompleted = true;
						resolve();
						return;
					}
					const body = await res.json();
					const d = body.data ?? body;
					if (d.status === 'completed') {
						simpleProgress = 100;
						createCompleted = true;
						resolve();
					} else if (d.status === 'failed') {
						reject(new Error(d.error || 'Install failed'));
					} else {
						simpleProgress = 100;
						createCompleted = true;
						resolve();
					}
				}).catch(() => {
					simpleProgress = 100;
					createCompleted = true;
					resolve();
				});
			};
		});
	}

	function viewServer() {
		goto(`/servers/${encodeURIComponent(serverName.trim())}`);
	}
</script>

<svelte:head>
	<title>New Server | MineOS</title>
</svelte:head>

<div class="page-header">
	<h1>Create New Server</h1>
	<p class="subtitle">Set up your perfect Minecraft server in just a few steps</p>
</div>

<div class="wizard">
	<div class="wizard-container">
		<nav class="step-indicator">
			{#each [
				{ num: 1, label: 'Server Type' },
				{ num: 2, label: 'Version' },
				{ num: 3, label: 'Name' },
				{ num: 4, label: 'Create' }
			] as s, i}
				{#if i > 0}
					<div class="step-line" class:active={stepNumber > s.num - 1}></div>
				{/if}
				<div class="step-dot" class:active={stepNumber >= s.num} class:current={stepNumber === s.num}>
					<span class="dot-num">{s.num}</span>
				</div>
			{/each}
		</nav>
		<div class="step-labels">
			{#each ['Server Type', 'Version', 'Name', 'Create'] as label, i}
				<span class:active={stepNumber >= i + 1} class:current={stepNumber === i + 1}>{label}</span>
			{/each}
		</div>

		{#if step === 'category'}
			<CategorySelect onselect={selectCategory} />
		{:else if step === 'implementation' && (category === 'plugins' || category === 'mods')}
			<ImplementationSelect
				{category}
				onselect={(id) => selectedImpl = id}
				onback={goBackFromImpl}
			/>
		{:else if step === 'version' && implementation}
			<VersionSelect
				{implementation}
				profiles={data.profiles.data ?? []}
				servers={data.servers.data ?? []}
				onselect={selectVersion}
				onback={goBackFromVersion}
				onready={(fn) => versionConfirm = fn}
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

		{#if step !== 'creating'}
			<div class="wizard-nav">
				<button
					class="btn-back"
					type="button"
					disabled={step === 'category'}
					onclick={() => {
						if (step === 'name') goBackFromName();
						else if (step === 'version') goBackFromVersion();
						else if (step === 'implementation') goBackFromImpl();
					}}
				>
					Back
				</button>
				<button
					class="btn-next"
					type="button"
					disabled={
						step === 'category' ||
						(step === 'implementation' && !selectedImpl) ||
						(step === 'version' && !versionConfirm) ||
						(step === 'name' && !serverName.trim())
					}
					onclick={() => {
						if (step === 'implementation' && selectedImpl) selectImplementation(selectedImpl);
						else if (step === 'version' && versionConfirm) versionConfirm();
						else if (step === 'name') createServer();
					}}
				>
					{step === 'name' ? 'Create Server' : 'Next'}
				</button>
			</div>
		{/if}
	</div>
</div>

<style>
	.page-header {
		padding: 0 2rem;
	}

	.page-header h1 {
		margin: 0 0 6px;
		font-size: 2rem;
		font-weight: 700;
	}

	.subtitle {
		margin: 0;
		color: var(--mc-text-muted, #9aa2c5);
	}

	.wizard {
		display: flex;
		justify-content: center;
		padding: 1.5rem 2rem 2rem;
	}

	.wizard-container {
		width: 100%;
		max-width: 720px;
		background: linear-gradient(135deg, var(--mc-panel, rgba(22, 27, 46, 0.95)), var(--mc-panel-dark, rgba(10, 14, 24, 0.95)));
		border: 1px solid var(--border-color, rgba(42, 47, 71, 0.8));
		border-radius: 18px;
		padding: 28px;
		box-shadow: 0 24px 40px rgba(0, 0, 0, 0.35);
	}

	.step-indicator {
		display: flex;
		align-items: center;
		justify-content: center;
		gap: 0;
		margin-bottom: 4px;
	}

	.step-line {
		width: 60px;
		height: 2px;
		background: var(--border-color, #2a2f47);
		transition: background 0.3s;
	}

	.step-line.active {
		background: var(--mc-grass, #6ab04c);
	}

	.step-dot {
		width: 36px;
		height: 36px;
		border-radius: 50%;
		display: flex;
		align-items: center;
		justify-content: center;
		background: var(--mc-panel-light, #2a2f47);
		border: 2px solid var(--border-color, #2a2f47);
		transition: all 0.3s;
	}

	.step-dot.active {
		background: var(--mc-grass, #6ab04c);
		border-color: var(--mc-grass, #6ab04c);
	}

	.step-dot.current {
		background: #4f46e5;
		border-color: #4f46e5;
		box-shadow: 0 0 0 4px rgba(79, 70, 229, 0.2);
	}

	.dot-num {
		font-size: 14px;
		font-weight: 600;
		color: white;
	}

	.step-labels {
		display: flex;
		justify-content: center;
		gap: 40px;
		margin-bottom: 24px;
		font-size: 12px;
		color: var(--mc-text-dim, #7c87b2);
	}

	.step-labels span.active {
		color: var(--mc-text-secondary, #c4cff5);
	}

	.step-labels span.current {
		color: var(--mc-text, #eef0f8);
		font-weight: 600;
	}

	.wizard-nav {
		display: flex;
		justify-content: space-between;
		align-items: center;
		margin-top: 24px;
		padding-top: 20px;
		border-top: 1px solid var(--border-color, #2a2f47);
	}

	.btn-back, .btn-next {
		padding: 10px 24px;
		border-radius: 10px;
		font-size: 14px;
		font-weight: 600;
		cursor: pointer;
		border: none;
		font-family: inherit;
		transition: all 0.15s;
	}

	.btn-back {
		background: var(--mc-panel-light, #2a2f47);
		color: var(--mc-text-secondary, #c4cff5);
	}

	.btn-back:hover:not(:disabled) {
		background: var(--mc-panel-lighter, #3a3f5a);
	}

	.btn-next {
		background: var(--mc-grass, #6ab04c);
		color: white;
	}

	.btn-next:hover:not(:disabled) {
		filter: brightness(1.1);
	}

	.btn-back:disabled, .btn-next:disabled {
		opacity: 0.35;
		cursor: not-allowed;
	}
</style>
