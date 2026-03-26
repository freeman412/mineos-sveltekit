<script lang="ts">
	import type { ServerSummary } from '$lib/api/types';

	interface Props {
		servers: ServerSummary[];
		onselect: (serverName: string) => void;
	}

	let { servers, onselect }: Props = $props();
</script>

<div class="template-picker">
	<p class="hint">Select an existing server to clone as your starting point:</p>
	<div class="server-list">
		{#each servers as server}
			<button class="server-row" onclick={() => onselect(server.name)} type="button">
				<span class="server-name">{server.name}</span>
				<span class="server-type">{server.serverType}</span>
			</button>
		{:else}
			<div class="empty">No existing servers to clone</div>
		{/each}
	</div>
</div>

<style>
	.template-picker {
		display: flex;
		flex-direction: column;
		gap: 0.75rem;
	}

	.hint {
		margin: 0;
		font-size: 0.85rem;
		color: var(--text-secondary, #9ca3af);
	}

	.server-list {
		border: 1px solid var(--border-color, #374151);
		border-radius: 0.5rem;
		overflow: hidden;
		max-height: 400px;
		overflow-y: auto;
	}

	.server-row {
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

	.server-row:last-child {
		border-bottom: none;
	}

	.server-row:hover {
		background: rgba(255, 255, 255, 0.05);
	}

	.server-type {
		color: var(--text-secondary, #9ca3af);
		font-size: 0.75rem;
	}

	.empty {
		padding: 2rem;
		text-align: center;
		color: var(--text-secondary, #9ca3af);
	}
</style>
