<script lang="ts">
	import type { Profile } from '$lib/api/types';
	import StatusBadge from '$lib/components/StatusBadge.svelte';

	interface Props {
		profiles: Profile[];
		implementation: 'vanilla' | 'paper' | 'spigot';
		onselect: (profile: Profile) => void;
		onready?: (fn: (() => void) | null) => void;
	}

	let { profiles, implementation, onselect, onready }: Props = $props();
	let selectedProfile = $state<Profile | null>(null);

	let search = $state('');
	let showSnapshots = $state(false);
	let page = $state(0);
	const perPage = 20;

	const filtered = $derived.by(() => {
		let result = profiles.filter((p) => {
			switch (implementation) {
				case 'vanilla':
					return p.group === 'vanilla';
				case 'paper':
					return p.group === 'paper';
				case 'spigot':
					return p.group === 'spigot' || (p.group === 'vanilla' && p.type === 'release');
				case 'craftbukkit':
					return (
						p.group === 'craftbukkit' ||
						p.group === 'bukkit' ||
						(p.group === 'vanilla' && p.type === 'release')
					);
				default:
					return false;
			}
		});
		if (!showSnapshots) {
			result = result.filter((p) => p.type === 'release' || p.type === 'latest');
		}
		if (search.trim()) {
			result = result.filter((p) =>
				p.version.toLowerCase().includes(search.trim().toLowerCase())
			);
		}
		return result;
	});

	const paged = $derived(filtered.slice(page * perPage, (page + 1) * perPage));
	const totalPages = $derived(Math.ceil(filtered.length / perPage));

	// Reset page when filter changes
	$effect(() => {
		search;
		showSnapshots;
		page = 0;
	});
</script>

<div class="version-picker">
	<div class="controls">
		<input type="text" placeholder="Search versions..." bind:value={search} class="search" />
		{#if implementation === 'vanilla'}
			<label class="toggle">
				<input type="checkbox" bind:checked={showSnapshots} />
				Show snapshots
			</label>
		{/if}
	</div>

	<div class="version-list">
		{#each paged as profile}
			<button
				class="version-row"
				class:selected={selectedProfile?.id === profile.id}
				onclick={() => {
					selectedProfile = profile;
					onready?.(() => onselect(profile));
				}}
				type="button"
			>
				<span class="version-name">{profile.version}</span>
				<span class="version-meta">
					{#if profile.downloaded}
						<StatusBadge variant="success" size="sm">Ready</StatusBadge>
					{:else}
						<StatusBadge variant="info" size="sm">Will Download</StatusBadge>
					{/if}
				</span>
			</button>
		{/each}
		{#if paged.length === 0}
			<div class="empty">No versions found</div>
		{/if}
	</div>

	{#if totalPages > 1}
		<div class="pagination">
			<button onclick={() => (page = Math.max(0, page - 1))} disabled={page === 0} type="button"
				>Prev</button
			>
			<span>
				Page {page + 1} of {totalPages}
			</span>
			<button
				onclick={() => (page = Math.min(totalPages - 1, page + 1))}
				disabled={page >= totalPages - 1}
				type="button">Next</button
			>
		</div>
	{/if}
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
		align-items: center;
	}

	.search {
		flex: 1;
		padding: 0.4rem 0.75rem;
		border: 1px solid var(--border-color, #374151);
		border-radius: 0.375rem;
		background: var(--input-bg, #1f2937);
		color: inherit;
		font-size: 0.85rem;
	}

	.toggle {
		display: flex;
		align-items: center;
		gap: 0.4rem;
		font-size: 0.8rem;
		color: var(--text-secondary, #9ca3af);
		cursor: pointer;
		white-space: nowrap;
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

	.version-row.selected {
		background: rgba(106, 176, 76, 0.15);
		border-color: var(--mc-grass, #6ab04c);
	}

	.empty {
		padding: 2rem;
		text-align: center;
		color: var(--text-secondary, #9ca3af);
	}

	.pagination {
		display: flex;
		justify-content: center;
		align-items: center;
		gap: 1rem;
		font-size: 0.85rem;
	}

	.pagination button {
		padding: 0.3rem 0.75rem;
		border: 1px solid var(--border-color, #374151);
		border-radius: 0.25rem;
		background: transparent;
		color: inherit;
		cursor: pointer;
		font-size: 0.8rem;
	}

	.pagination button:disabled {
		opacity: 0.4;
		cursor: not-allowed;
	}
</style>
