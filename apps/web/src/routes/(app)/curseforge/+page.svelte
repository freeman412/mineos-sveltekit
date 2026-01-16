<script lang="ts">
	import CurseForgeSearch from '$lib/components/CurseForgeSearch.svelte';
	import type { PageData } from './$types';
	import type { ServerSummary } from '$lib/api/types';

	let { data }: { data: PageData } = $props();

	const servers = (data.servers.data ?? []) as ServerSummary[];
	let selectedServer = $state(servers[0]?.name ?? '');
</script>

<div class="page">
	<div class="page-header">
		<div>
			<h2>CurseForge</h2>
			<p class="subtitle">Search mods and modpacks, then install directly to a server</p>
		</div>
	</div>

	{#if data.servers.error}
		<div class="error-box">
			<p>Failed to load servers: {data.servers.error}</p>
		</div>
	{:else if servers.length === 0}
		<div class="info-box">
			<p>Create a server before installing mods.</p>
		</div>
	{:else}
		<div class="server-selector">
			<label>
				<span>Target Server:</span>
				<select bind:value={selectedServer}>
					{#each servers as server}
						<option value={server.name}>{server.name}</option>
					{/each}
				</select>
			</label>
		</div>

		<CurseForgeSearch serverName={selectedServer} />
	{/if}
</div>

<style>
	.page {
		display: flex;
		flex-direction: column;
		gap: 24px;
	}

	h2 {
		margin: 0 0 8px;
		font-size: 24px;
		font-weight: 600;
	}

	.subtitle {
		margin: 0;
		color: #aab2d3;
		font-size: 14px;
	}

	.error-box {
		background: rgba(255, 92, 92, 0.1);
		border: 1px solid rgba(255, 92, 92, 0.3);
		border-radius: 12px;
		padding: 16px 20px;
		color: #ff9f9f;
	}

	.info-box {
		background: rgba(88, 101, 242, 0.1);
		border: 1px solid rgba(88, 101, 242, 0.3);
		border-radius: 12px;
		padding: 16px 20px;
		color: #cdd3f3;
	}

	.server-selector {
		background: #1a1e2f;
		border-radius: 16px;
		padding: 20px;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.35);
	}

	.server-selector label {
		display: flex;
		gap: 12px;
		align-items: center;
		color: #eef0f8;
		font-weight: 600;
	}

	.server-selector select {
		background: #141827;
		border: 1px solid #2a2f47;
		border-radius: 8px;
		padding: 10px 12px;
		color: #eef0f8;
		min-width: 200px;
	}
</style>
