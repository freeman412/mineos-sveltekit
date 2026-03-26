<script lang="ts">
	import SelectionCard from '$lib/components/SelectionCard.svelte';

	export type ServerCategory = 'vanilla' | 'plugins' | 'mods' | 'bedrock' | 'template';

	interface Props {
		onselect: (category: ServerCategory) => void;
	}

	let { onselect }: Props = $props();

	const categories = [
		{
			id: 'vanilla' as const,
			name: 'Vanilla',
			description:
				'The official Minecraft server from Mojang. No mods, no plugins — pure gameplay.',
			icon: '🎮',
			color: '#4ade80'
		},
		{
			id: 'plugins' as const,
			name: 'Plugins',
			description:
				'Enhanced servers with plugin support for custom game modes, anti-cheat, permissions, and more.',
			icon: '🔌',
			color: '#60a5fa'
		},
		{
			id: 'mods' as const,
			name: 'Mods',
			description:
				'Full mod support — new blocks, dimensions, mechanics, and total conversions.',
			icon: '🧩',
			color: '#a855f7'
		},
		{
			id: 'bedrock' as const,
			name: 'Bedrock',
			description:
				'Bedrock Edition dedicated server for cross-platform play (mobile, console, Windows).',
			icon: '🪨',
			color: '#3b82f6'
		},
		{
			id: 'template' as const,
			name: 'Template',
			description: 'Clone an existing server as a starting point.',
			icon: 'T',
			color: '#22d3ee'
		}
	];
</script>

<div class="step">
	<h2>What kind of server?</h2>
	<div class="cards">
		{#each categories as cat}
			<SelectionCard
				title={cat.name}
				description={cat.description}
				icon={cat.icon}
				color={cat.color}
				onclick={() => onselect(cat.id)}
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

	h2 {
		margin: 0;
		font-size: 1.25rem;
		font-weight: 600;
	}

	.cards {
		display: flex;
		flex-direction: column;
		gap: 0.75rem;
	}
</style>
