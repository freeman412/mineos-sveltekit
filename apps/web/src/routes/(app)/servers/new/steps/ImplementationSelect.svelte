<script lang="ts">
	import SelectionCard from '$lib/components/SelectionCard.svelte';
	import type { ServerCategory } from './CategorySelect.svelte';

	export type PluginImpl = 'paper' | 'spigot' | 'craftbukkit';
	export type ModLoader = 'forge' | 'neoforge' | 'fabric' | 'quilt';
	export type Implementation = PluginImpl | ModLoader;

	interface Props {
		category: 'plugins' | 'mods';
		onselect: (impl: Implementation) => void;
		onback: () => void;
	}

	let { category, onselect, onback }: Props = $props();

	const pluginOptions = [
		{
			id: 'paper' as const,
			name: 'Paper',
			description:
				'High-performance Spigot fork with async chunks and extensive optimizations.',
			icon: '📄',
			iconImage: '/images/loaders/paper.png',
			color: '#60a5fa',
			badge: 'Recommended'
		},
		{
			id: 'spigot' as const,
			name: 'Spigot',
			description: 'The original plugin server. Wide compatibility with Bukkit plugins.',
			icon: '🔧',
			color: '#fbbf24'
		},
		{
			id: 'craftbukkit' as const,
			name: 'CraftBukkit',
			description: 'The classic. Fewest modifications to vanilla, built via BuildTools.',
			icon: '🪣',
			color: '#f97316'
		}
	];

	const modOptions = [
		{
			id: 'forge' as const,
			name: 'Forge',
			description:
				'The most established modloader. Largest mod library, widest version support.',
			icon: '🔥',
			iconImage: '/images/loaders/forge.png',
			color: '#ef4444'
		},
		{
			id: 'neoforge' as const,
			name: 'NeoForge',
			description:
				'Community-driven Forge successor. Modern APIs, active development. 1.20.1+ only.',
			icon: '⚡',
			iconImage: '/images/loaders/neoforge.png',
			color: '#f97316'
		},
		{
			id: 'fabric' as const,
			name: 'Fabric',
			description: 'Lightweight and fast. Growing mod ecosystem, popular for newer versions.',
			icon: '🧵',
			iconImage: '/images/loaders/fabric.png',
			color: '#c4b5a4'
		},
		{
			id: 'quilt' as const,
			name: 'Quilt',
			description: 'Fabric-compatible fork with additional mod management features.',
			icon: '🪡',
			iconImage: '/images/loaders/quilt.png',
			color: '#8b5cf6'
		}
	];

	const options = $derived(category === 'plugins' ? pluginOptions : modOptions);
	const heading = $derived(
		category === 'plugins' ? 'Choose a plugin server' : 'Choose a modloader'
	);
</script>

<div class="step">
	<div class="header">
		<button class="back-btn" onclick={onback} type="button">&larr; Back</button>
		<h2>{heading}</h2>
	</div>
	<div class="cards">
		{#each options as opt}
			<SelectionCard
				title={opt.name}
				description={opt.description}
				icon={opt.icon}
				iconImage={opt.iconImage}
				color={opt.color}
				badge={'badge' in opt ? opt.badge : undefined}
				onclick={() => onselect(opt.id)}
			/>
		{/each}
	</div>
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

	.cards {
		display: flex;
		flex-direction: column;
		gap: 0.75rem;
	}
</style>
