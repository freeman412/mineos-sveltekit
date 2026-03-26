<!-- apps/web/src/lib/components/SelectionCard.svelte -->
<script lang="ts">
	interface Props {
		title: string;
		description: string;
		icon?: string;
		iconImage?: string;
		color?: string;
		badge?: string;
		selected?: boolean;
		onclick?: () => void;
	}

	let { title, description, icon, iconImage, color = '#6b7280', badge, selected = false, onclick }: Props = $props();
</script>

<button
	class="selection-card"
	class:selected
	style="--card-color: {color}"
	onclick={onclick}
	type="button"
>
	<div class="card-icon">
		{#if iconImage}
			<img src={iconImage} alt={title} class="icon-image" />
		{:else if icon}
			<span class="icon-emoji">{icon}</span>
		{/if}
	</div>
	<div class="card-content">
		<div class="card-header">
			<h3>{title}</h3>
			{#if badge}
				<span class="badge">{badge}</span>
			{/if}
		</div>
		<p>{description}</p>
	</div>
</button>

<style>
	.selection-card {
		display: flex;
		align-items: center;
		gap: 1rem;
		padding: 1rem 1.25rem;
		border: 2px solid var(--border-color, #374151);
		border-radius: 0.75rem;
		background: var(--card-bg, #1f2937);
		cursor: pointer;
		transition: all 0.15s ease;
		text-align: left;
		width: 100%;
		color: inherit;
		font-family: inherit;
	}

	.selection-card:hover {
		border-color: var(--card-color);
		background: color-mix(in srgb, var(--card-color) 8%, var(--card-bg, #1f2937));
	}

	.selection-card.selected {
		border-color: var(--card-color);
		background: color-mix(in srgb, var(--card-color) 15%, var(--card-bg, #1f2937));
		box-shadow: 0 0 0 1px var(--card-color);
	}

	.card-icon {
		flex-shrink: 0;
		width: 2.5rem;
		height: 2.5rem;
		display: flex;
		align-items: center;
		justify-content: center;
		border-radius: 0.5rem;
		background: color-mix(in srgb, var(--card-color) 20%, transparent);
	}

	.icon-image {
		width: 1.5rem;
		height: 1.5rem;
		object-fit: contain;
	}

	.icon-emoji {
		font-size: 1.25rem;
	}

	.card-content {
		flex: 1;
		min-width: 0;
	}

	.card-header {
		display: flex;
		align-items: center;
		gap: 0.5rem;
	}

	h3 {
		margin: 0;
		font-size: 0.95rem;
		font-weight: 600;
		color: var(--text-primary, #f9fafb);
	}

	.badge {
		font-size: 0.65rem;
		font-weight: 600;
		padding: 0.1rem 0.4rem;
		border-radius: 0.25rem;
		background: var(--card-color);
		color: #000;
		text-transform: uppercase;
		letter-spacing: 0.03em;
	}

	p {
		margin: 0.25rem 0 0;
		font-size: 0.8rem;
		color: var(--text-secondary, #9ca3af);
		line-height: 1.4;
	}
</style>
