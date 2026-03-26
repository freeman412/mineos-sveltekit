<script lang="ts">
	import type { Profile } from '$lib/api/types';
	import StatusBadge from '$lib/components/StatusBadge.svelte';

	interface Props {
		profiles: Profile[];
		onselect: (profile: Profile) => void;
	}

	let { profiles, onselect }: Props = $props();

	let showPreview = $state(false);

	const filtered = $derived.by(() => {
		return profiles.filter((p) => {
			if (showPreview) return p.group === 'bedrock-server' || p.group === 'bedrock-server-preview';
			return p.group === 'bedrock-server';
		});
	});
</script>

<div class="version-picker">
	<div class="controls">
		<label class="toggle">
			<input type="checkbox" bind:checked={showPreview} />
			Show preview builds
		</label>
	</div>

	<div class="version-list">
		{#each filtered as profile}
			<button class="version-row" onclick={() => onselect(profile)} type="button">
				<span>{profile.version}</span>
				<span>
					{#if profile.group === 'bedrock-server-preview'}
						<StatusBadge variant="warning" size="sm">Preview</StatusBadge>
					{:else if profile.downloaded}
						<StatusBadge variant="success" size="sm">Ready</StatusBadge>
					{:else}
						<StatusBadge variant="info" size="sm">Will Download</StatusBadge>
					{/if}
				</span>
			</button>
		{/each}
	</div>
</div>

<style>
	.version-picker {
		display: flex;
		flex-direction: column;
		gap: 0.75rem;
	}

	.controls {
		display: flex;
		gap: 1rem;
	}

	.toggle {
		display: flex;
		align-items: center;
		gap: 0.4rem;
		font-size: 0.8rem;
		color: var(--text-secondary, #9ca3af);
		cursor: pointer;
	}

	.version-list {
		border: 1px solid var(--border-color, #374151);
		border-radius: 0.5rem;
		overflow: hidden;
		max-height: 400px;
		overflow-y: auto;
	}

	.version-row {
		display: flex;
		justify-content: space-between;
		align-items: center;
		width: 100%;
		padding: 0.5rem 0.75rem;
		border: none;
		border-bottom: 1px solid var(--border-color, #374151);
		background: transparent;
		color: inherit;
		cursor: pointer;
		font-size: 0.85rem;
		text-align: left;
		font-family: inherit;
	}

	.version-row:last-child {
		border-bottom: none;
	}

	.version-row:hover {
		background: rgba(255, 255, 255, 0.05);
	}
</style>
