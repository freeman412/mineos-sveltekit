<script lang="ts">
	import type { Profile } from '$lib/api/types';
	import StatusBadge from '$lib/components/StatusBadge.svelte';

	interface Props {
		profiles: Profile[];
		onselect: (profile: Profile) => void;
		onready?: (fn: (() => void) | null) => void;
	}

	let { profiles, onselect, onready }: Props = $props();
	let selectedProfile = $state<Profile | null>(null);

	let search = $state('');
	let page = $state(0);
	const perPage = 20;

	// BungeeCord versions are Jenkins build numbers ("build-2068"), not semver, so
	// the shared profile list falls back to ordering them lexically — where
	// "build-999" lands after "build-1000". Sort numerically here so the newest
	// build is always first.
	const buildNumber = (p: Profile) => Number(p.version.replace(/^build-/, '')) || 0;

	const filtered = $derived.by(() => {
		let result = profiles.filter((p) => p.group === 'bungeecord');
		if (search.trim()) {
			result = result.filter((p) =>
				p.version.toLowerCase().includes(search.trim().toLowerCase())
			);
		}
		return [...result].sort((a, b) => buildNumber(b) - buildNumber(a));
	});

	const paged = $derived(filtered.slice(page * perPage, (page + 1) * perPage));
	const totalPages = $derived(Math.ceil(filtered.length / perPage));

	$effect(() => {
		search;
		page = 0;
	});
</script>

<div class="version-picker">
	<div class="controls">
		<input type="text" placeholder="Search builds..." bind:value={search} class="search" />
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
			<div class="empty">No BungeeCord builds available</div>
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

	<p class="hint">
		BungeeCord is a long-running Minecraft proxy by md_5. Builds are numbered (no semver). The
		latest successful build supports current Minecraft releases. After first launch, edit
		<code>config.yml</code> via the Properties tab to configure backends.
	</p>
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
		background: rgba(6, 182, 212, 0.15);
		border-color: #06b6d4;
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

	.hint {
		margin: 0;
		padding: 0.6rem 0.8rem;
		font-size: 0.78rem;
		color: var(--text-secondary, #9ca3af);
		background: rgba(6, 182, 212, 0.06);
		border: 1px solid rgba(6, 182, 212, 0.2);
		border-radius: 0.4rem;
		line-height: 1.4;
	}

	.hint code {
		background: rgba(255, 255, 255, 0.08);
		padding: 0.05rem 0.3rem;
		border-radius: 0.2rem;
		font-size: 0.85em;
	}
</style>
