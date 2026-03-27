<script lang="ts">
	import type { Profile, ServerDetail, ForgeVersion } from '$lib/api/types';
	import type { NeoForgeVersion } from '$lib/api/types';
	import type { Implementation } from './ImplementationSelect.svelte';
	import VanillaVersions from '../version-pickers/VanillaVersions.svelte';
	import ForgeVersions from '../version-pickers/ForgeVersions.svelte';
	import NeoForgeVersions from '../version-pickers/NeoForgeVersions.svelte';
	import FabricVersions from '../version-pickers/FabricVersions.svelte';
	import QuiltVersions from '../version-pickers/QuiltVersions.svelte';
	import BedrockVersions from '../version-pickers/BedrockVersions.svelte';
	import TemplateSelect from '../version-pickers/TemplateSelect.svelte';

	interface VersionSelection {
		profileId?: string;
		minecraftVersion?: string;
		loaderVersion?: string;
		forgeVersion?: ForgeVersion;
		neoForgeVersion?: NeoForgeVersion;
		cloneSource?: string;
	}

	interface Props {
		implementation: Implementation | 'vanilla' | 'bedrock' | 'template';
		profiles: Profile[];
		servers: ServerDetail[];
		onselect: (selection: VersionSelection) => void;
		onback: () => void;
		onready?: (confirmFn: (() => void) | null) => void;
	}

	let { implementation, profiles, servers, onselect, onback, onready }: Props = $props();

	const labels: Record<string, string> = {
		vanilla: 'Vanilla',
		paper: 'Paper',
		spigot: 'Spigot',
		craftbukkit: 'CraftBukkit',
		forge: 'Forge',
		neoforge: 'NeoForge',
		fabric: 'Fabric',
		quilt: 'Quilt',
		bedrock: 'Bedrock',
		template: 'Template'
	};
</script>

<div class="step">
	<div class="header">
		<h2>Select {labels[implementation]} version</h2>
	</div>

	{#if implementation === 'vanilla' || implementation === 'paper' || implementation === 'spigot'}
		<VanillaVersions
			{profiles}
			{implementation}
			onselect={(profile) => onselect({ profileId: profile.id, minecraftVersion: profile.version })}
			onready={onready}
		/>
	{:else if implementation === 'forge'}
		<ForgeVersions
			onselect={(mc, forge) =>
				onselect({ minecraftVersion: mc, forgeVersion: forge })}
			onconfirm={onready}
		/>
	{:else if implementation === 'neoforge'}
		<NeoForgeVersions
			onselect={(mc, nf) =>
				onselect({ minecraftVersion: mc, neoForgeVersion: nf })}
			onconfirm={onready}
		/>
	{:else if implementation === 'fabric'}
		<FabricVersions
			onselect={(mc, loader) =>
				onselect({ minecraftVersion: mc, loaderVersion: loader })}
			onconfirm={onready}
		/>
	{:else if implementation === 'quilt'}
		<QuiltVersions
			onselect={(mc, loader) =>
				onselect({ minecraftVersion: mc, loaderVersion: loader })}
			onconfirm={onready}
		/>
	{:else if implementation === 'bedrock'}
		<BedrockVersions
			{profiles}
			onselect={(profile) => onselect({ profileId: profile.id, minecraftVersion: profile.version })}
			onready={onready}
		/>
	{:else if implementation === 'template'}
		<TemplateSelect
			{servers}
			onselect={(name) => onselect({ cloneSource: name })}
			onready={onready}
		/>
	{/if}
</div>

<style>
	.step {
		display: flex;
		flex-direction: column;
		gap: 1.25rem;
	}

	.header {
		display: flex;
		align-items: center;
		gap: 1rem;
	}

	h2 {
		margin: 0;
		font-size: 1.25rem;
		font-weight: 600;
	}

	.back-btn {
		background: none;
		border: 1px solid var(--border-color, #374151);
		color: var(--text-secondary, #9ca3af);
		padding: 0.35rem 0.75rem;
		border-radius: 0.375rem;
		cursor: pointer;
		font-size: 0.85rem;
	}

	.back-btn:hover {
		color: var(--text-primary, #f9fafb);
		border-color: var(--text-secondary, #9ca3af);
	}
</style>
