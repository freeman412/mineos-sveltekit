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
			description: 'Official Minecraft server from Mojang',
			icon: '🎮',
			iconImage: '/images/loaders/vanilla.svg',
			color: '#4ade80',
			features: ['Official', 'Pure gameplay', 'Most stable']
		},
		{
			id: 'plugins' as const,
			name: 'Plugins',
			description: 'Paper, Spigot, or CraftBukkit with plugin support',
			icon: '🔌',
			iconImage: '/images/plugins.png',
			color: '#60a5fa',
			features: ['Custom modes', 'Anti-cheat', 'Permissions']
		},
		{
			id: 'mods' as const,
			name: 'Mods',
			description: 'Forge, Fabric, NeoForge, or Quilt modded servers',
			icon: '🧩',
			iconImage: '/images/mods.png',
			color: '#a855f7',
			features: ['New blocks', 'Dimensions', 'Total conversions']
		},
		{
			id: 'bedrock' as const,
			name: 'Bedrock',
			description: 'Official Bedrock Dedicated Server for cross-platform play',
			icon: '🪨',
			iconImage: '/images/loaders/bedrock.svg',
			color: '#3b82f6',
			features: ['Cross-platform', 'Mobile/Console', 'Native binary']
		},
		{
			id: 'template' as const,
			name: 'Template',
			description: 'Clone an existing server as your starting point',
			icon: 'T',
			iconImage: '/images/templates.png',
			color: '#22d3ee',
			features: ['Duplicate', 'Fast setup', 'Preserve config']
		}
	];
</script>

<div class="step">
	<h2>What kind of server?</h2>
	<p class="subtitle">Select the type of Minecraft server you want to create</p>
	<div class="cards">
		{#each categories as cat}
			<button
				class="type-card"
				style="--card-color: {cat.color}"
				onclick={() => onselect(cat.id)}
				type="button"
			>
				<div class="type-icon">
					{#if cat.iconImage}
						<img src={cat.iconImage} alt={cat.name} class="icon-img" />
					{:else}
						{cat.icon}
					{/if}
				</div>
				<div class="type-info">
					<h3>{cat.name}</h3>
					<p>{cat.description}</p>
					<div class="type-features">
						{#each cat.features as feature}
							<span class="feature-tag">{feature}</span>
						{/each}
					</div>
				</div>
			</button>
		{/each}
	</div>
</div>

<style>
	.step {
		display: flex;
		flex-direction: column;
		gap: 1rem;
	}

	h2 {
		margin: 0;
		font-size: 1.5rem;
		font-weight: 700;
	}

	.subtitle {
		margin: 0;
		color: var(--mc-text-muted, #9ca3af);
		font-size: 0.9rem;
	}

	.cards {
		display: grid;
		grid-template-columns: repeat(2, 1fr);
		gap: 12px;
	}

	@media (max-width: 640px) {
		.cards {
			grid-template-columns: 1fr;
		}
	}

	.type-card {
		display: flex;
		align-items: flex-start;
		gap: 14px;
		padding: 18px;
		background: var(--mc-panel, #141827);
		border: 2px solid var(--border-color, #2a2f47);
		border-radius: 14px;
		cursor: pointer;
		transition: all 0.15s;
		text-align: left;
		font-family: inherit;
		color: inherit;
		position: relative;
		overflow: hidden;
	}

	.type-card:hover {
		border-color: var(--card-color);
		background: color-mix(in srgb, var(--card-color) 8%, var(--mc-panel, #141827));
		transform: translateY(-1px);
	}

	.type-icon {
		font-size: 42px;
		flex-shrink: 0;
		display: flex;
		align-items: center;
		justify-content: center;
		align-self: stretch;
	}

	.icon-img {
		height: 100%;
		width: auto;
		max-width: 80px;
		object-fit: contain;
		border-radius: 8px;
	}

	.type-info {
		flex: 1;
		min-width: 0;
	}

	.type-info h3 {
		margin: 0 0 4px;
		font-size: 1rem;
		font-weight: 600;
		color: var(--mc-text, #eef0f8);
	}

	.type-info p {
		margin: 0 0 10px;
		font-size: 0.8rem;
		color: var(--mc-text-muted, #9aa2c5);
		line-height: 1.4;
	}

	.type-features {
		display: flex;
		flex-wrap: wrap;
		gap: 6px;
	}

	.feature-tag {
		background: rgba(255, 255, 255, 0.08);
		padding: 2px 10px;
		border-radius: 999px;
		font-size: 0.7rem;
		color: var(--mc-text-secondary, #c4cff5);
		font-weight: 500;
	}
</style>
