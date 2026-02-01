<script lang="ts">
	import ModrinthModSearch from './ModrinthModSearch.svelte';
	import ModrinthModpackSearch from './ModrinthModpackSearch.svelte';
	import ModrinthResourcePackSearch from './ModrinthResourcePackSearch.svelte';

	interface Props {
		serverName: string;
		serverVersion?: string | null;
		onInstallComplete?: () => void;
	}

	let { serverName, serverVersion, onInstallComplete }: Props = $props();
	let searchType = $state<'mods' | 'modpacks' | 'resourcepacks'>('mods');
</script>

<div class="modrinth-search">
	<div class="search-controls">
		<div class="type-tabs" role="tablist" aria-label="Modrinth content type">
			<button
				type="button"
				class:active={searchType === 'mods'}
				role="tab"
				aria-selected={searchType === 'mods'}
				onclick={() => (searchType = 'mods')}
			>
				Mods
			</button>
			<button
				type="button"
				class:active={searchType === 'modpacks'}
				role="tab"
				aria-selected={searchType === 'modpacks'}
				onclick={() => (searchType = 'modpacks')}
			>
				Modpacks
			</button>
			<button
				type="button"
				class:active={searchType === 'resourcepacks'}
				role="tab"
				aria-selected={searchType === 'resourcepacks'}
				onclick={() => (searchType = 'resourcepacks')}
			>
				Resource Packs
			</button>
		</div>
	</div>

	{#if searchType === 'mods'}
		<ModrinthModSearch {serverName} {serverVersion} {onInstallComplete} />
	{:else if searchType === 'modpacks'}
		<ModrinthModpackSearch {serverName} {serverVersion} {onInstallComplete} />
	{:else}
		<ModrinthResourcePackSearch {serverName} {serverVersion} {onInstallComplete} />
	{/if}
</div>

<style>
	.modrinth-search {
		display: flex;
		flex-direction: column;
		gap: 16px;
	}

	.search-controls {
		display: flex;
		align-items: center;
		gap: 12px;
		flex-wrap: wrap;
	}

	.type-tabs {
		display: flex;
		background: #141827;
		border-radius: 10px;
		padding: 4px;
		border: 1px solid #2a2f47;
	}

	.type-tabs button {
		background: transparent;
		border: none;
		color: #8890b1;
		padding: 8px 14px;
		border-radius: 8px;
		cursor: pointer;
		transition: all 0.2s;
	}

	.type-tabs button.active {
		background: #1f2a4a;
		color: #eef0f8;
	}
</style>
