<!-- apps/web/src/lib/components/TwoColumnVersionPicker.svelte -->
<script lang="ts" generics="TLeft, TRight">
	interface Props {
		/** Left column items */
		leftItems: TLeft[];
		/** Right column items (filtered by left selection) */
		rightItems: TRight[];
		/** Left column label */
		leftLabel: string;
		/** Right column label */
		rightLabel: string;
		/** Get display text for left item */
		leftDisplay: (item: TLeft) => string;
		/** Get display text for right item */
		rightDisplay: (item: TRight) => string;
		/** Get badge text for right item (e.g. "Latest", "Recommended") */
		rightBadge?: (item: TRight) => string | null;
		/** Currently selected left item index */
		selectedLeftIndex: number | null;
		/** Currently selected right item index */
		selectedRightIndex: number | null;
		/** Called when left item is selected */
		onselectleft: (index: number, item: TLeft) => void;
		/** Called when right item is selected */
		onselectright: (index: number, item: TRight) => void;
		/** Loading state */
		loading?: boolean;
		/** Error message */
		error?: string;
	}

	let {
		leftItems,
		rightItems,
		leftLabel,
		rightLabel,
		leftDisplay,
		rightDisplay,
		rightBadge,
		selectedLeftIndex = null,
		selectedRightIndex = null,
		onselectleft,
		onselectright,
		loading = false,
		error = ''
	}: Props = $props();

	let leftSearch = $state('');
	let rightSearch = $state('');

	const filteredLeft = $derived(
		leftSearch.trim()
			? leftItems.filter((item) =>
					leftDisplay(item).toLowerCase().includes(leftSearch.trim().toLowerCase())
				)
			: leftItems
	);

	const filteredRight = $derived(
		rightSearch.trim()
			? rightItems.filter((item) =>
					rightDisplay(item).toLowerCase().includes(rightSearch.trim().toLowerCase())
				)
			: rightItems
	);
</script>

<div class="two-col-picker">
	{#if loading}
		<div class="loading">Loading versions...</div>
	{:else if error}
		<div class="error">{error}</div>
	{:else}
		<div class="column">
			<div class="column-header">
				<span>{leftLabel}</span>
				<input
					type="text"
					placeholder="Search..."
					bind:value={leftSearch}
					class="search-input"
				/>
			</div>
			<div class="column-list">
				{#each filteredLeft as item, i}
					{@const originalIndex = leftItems.indexOf(item)}
					<button
						class="version-item"
						class:selected={selectedLeftIndex === originalIndex}
						onclick={() => onselectleft(originalIndex, item)}
						type="button"
					>
						{leftDisplay(item)}
					</button>
				{/each}
			</div>
		</div>

		<div class="column">
			<div class="column-header">
				<span>{rightLabel}</span>
				{#if rightItems.length > 0}
					<input
						type="text"
						placeholder="Search..."
						bind:value={rightSearch}
						class="search-input"
					/>
				{/if}
			</div>
			<div class="column-list">
				{#if selectedLeftIndex === null}
					<div class="placeholder">Select a {leftLabel.toLowerCase()} first</div>
				{:else if rightItems.length === 0}
					<div class="placeholder">No versions available</div>
				{:else}
					{#each filteredRight as item, i}
						{@const originalIndex = rightItems.indexOf(item)}
						{@const badge = rightBadge?.(item)}
						<button
							class="version-item"
							class:selected={selectedRightIndex === originalIndex}
							onclick={() => onselectright(originalIndex, item)}
							type="button"
						>
							<span>{rightDisplay(item)}</span>
							{#if badge}
								<span class="badge">{badge}</span>
							{/if}
						</button>
					{/each}
				{/if}
			</div>
		</div>
	{/if}
</div>

<style>
	.two-col-picker {
		display: grid;
		grid-template-columns: 1fr 1fr;
		gap: 1rem;
		min-height: 300px;
	}

	.column {
		display: flex;
		flex-direction: column;
		border: 1px solid var(--border-color, #374151);
		border-radius: 0.5rem;
		overflow: hidden;
	}

	.column-header {
		display: flex;
		justify-content: space-between;
		align-items: center;
		padding: 0.5rem 0.75rem;
		background: var(--header-bg, #111827);
		border-bottom: 1px solid var(--border-color, #374151);
		font-weight: 600;
		font-size: 0.85rem;
	}

	.search-input {
		padding: 0.25rem 0.5rem;
		border: 1px solid var(--border-color, #374151);
		border-radius: 0.25rem;
		background: var(--input-bg, #1f2937);
		color: inherit;
		font-size: 0.8rem;
		width: 120px;
	}

	.column-list {
		flex: 1;
		overflow-y: auto;
		max-height: 400px;
	}

	.version-item {
		display: flex;
		justify-content: space-between;
		align-items: center;
		width: 100%;
		padding: 0.4rem 0.75rem;
		border: none;
		background: transparent;
		color: inherit;
		cursor: pointer;
		font-size: 0.85rem;
		text-align: left;
		font-family: inherit;
	}

	.version-item:hover {
		background: rgba(255, 255, 255, 0.05);
	}

	.version-item.selected {
		background: rgba(59, 130, 246, 0.2);
		color: #60a5fa;
	}

	.badge {
		font-size: 0.65rem;
		font-weight: 600;
		padding: 0.1rem 0.35rem;
		border-radius: 0.2rem;
		background: #22c55e;
		color: #000;
		text-transform: uppercase;
	}

	.loading,
	.error,
	.placeholder {
		grid-column: 1 / -1;
		display: flex;
		align-items: center;
		justify-content: center;
		padding: 2rem;
		color: var(--text-secondary, #9ca3af);
		font-size: 0.9rem;
	}

	.error {
		color: #ef4444;
	}
</style>
