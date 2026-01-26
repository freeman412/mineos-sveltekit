<script lang="ts">
	import CurseForgeSearch from '$lib/components/CurseForgeSearch.svelte';
	import ModrinthSearch from '$lib/components/ModrinthSearch.svelte';
	import type { PageData } from './$types';
	import type { ServerSummary } from '$lib/api/types';
	import { withBase } from '$lib/utils/paths';

	let { data }: { data: PageData } = $props();

	const servers = (data.servers.data ?? []) as ServerSummary[];
	let selectedServer = $state(servers[0]?.name ?? '');

	// Tab state for switching between providers
	type Provider = 'curseforge' | 'modrinth';
	let activeProvider = $state<Provider>('curseforge');
</script>

<div class="page">
	<div class="page-header">
		<div>
			<h2>Mods</h2>
			<p class="subtitle">Search and install mods and modpacks from CurseForge and Modrinth</p>
		</div>
	</div>

	{#if data.servers.error}
		<div class="error-box">
			<p>Failed to load servers: {data.servers.error}</p>
		</div>
	{:else if servers.length === 0}
		<div class="info-box">
			<p>Create a server before installing mods.</p>
			<a href={withBase('/servers/new')} class="btn-create">Create Server</a>
		</div>
	{:else}
		<div class="controls-section">
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

			<div class="provider-tabs">
				<button
					class="provider-tab"
					class:active={activeProvider === 'curseforge'}
					onclick={() => (activeProvider = 'curseforge')}
				>
					<span class="provider-icon curseforge">CF</span>
					CurseForge
				</button>
				<button
					class="provider-tab"
					class:active={activeProvider === 'modrinth'}
					onclick={() => (activeProvider = 'modrinth')}
				>
					<span class="provider-icon modrinth">M</span>
					Modrinth
				</button>
			</div>
		</div>

		{#if activeProvider === 'curseforge'}
			{#if !data.curseForgeConfigured}
				<div class="setup-required">
					<div class="setup-icon">
						<svg viewBox="0 0 24 24" width="48" height="48">
							<path fill="currentColor" d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-2 15l-5-5 1.41-1.41L10 14.17l7.59-7.59L19 8l-9 9z" opacity="0.3"/>
							<path fill="currentColor" d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8zm-1-5h2v2h-2zm0-8h2v6h-2z"/>
						</svg>
					</div>
					<h3>CurseForge API Key Required</h3>
					<p>
						To search and download mods or modpacks from CurseForge, you need to configure an API key.
					</p>
					{#if data.isAdmin}
						<a href="/admin/settings" class="btn-setup">
							Configure API Key
						</a>
					{:else}
						<p class="admin-note">
							Please ask an administrator to configure the CurseForge API key in Settings.
						</p>
					{/if}
					<div class="setup-help">
						<details>
							<summary>How to get an API key</summary>
							<ol>
								<li>Visit <a href="https://console.curseforge.com/" target="_blank" rel="noopener noreferrer">console.curseforge.com</a></li>
								<li>Sign in or create an account</li>
								<li>Go to "API Keys" in your account settings</li>
								<li>Create a new API key</li>
								<li>Copy the key and configure it in Settings</li>
							</ol>
						</details>
					</div>
				</div>
			{:else}
				<div class="provider-content">
					<CurseForgeSearch serverName={selectedServer} />
				</div>
			{/if}
		{:else if activeProvider === 'modrinth'}
			<div class="provider-content">
				<ModrinthSearch serverName={selectedServer} />
			</div>
		{/if}
	{/if}
</div>

<style>
	.page {
		display: flex;
		flex-direction: column;
		gap: 24px;
	}

	.page-header {
		display: flex;
		justify-content: space-between;
		align-items: flex-start;
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
		padding: 24px;
		color: #cdd3f3;
		text-align: center;
	}

	.info-box p {
		margin: 0 0 16px;
	}

	.btn-create {
		display: inline-block;
		background: var(--mc-grass);
		color: #fff;
		padding: 10px 20px;
		border-radius: 8px;
		font-weight: 600;
		font-size: 14px;
		text-decoration: none;
		transition: background 0.2s;
	}

	.btn-create:hover {
		background: var(--mc-grass-dark);
	}

	.controls-section {
		background: #1a1e2f;
		border-radius: 16px;
		padding: 20px;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.35);
		display: flex;
		flex-wrap: wrap;
		gap: 20px;
		align-items: center;
		justify-content: space-between;
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

	.provider-tabs {
		display: flex;
		gap: 8px;
	}

	.provider-tab {
		display: flex;
		align-items: center;
		gap: 8px;
		padding: 10px 20px;
		border-radius: 8px;
		border: 1px solid #2a2f47;
		background: #141827;
		color: #9aa2c5;
		font-family: inherit;
		font-size: 14px;
		font-weight: 500;
		cursor: pointer;
		transition: all 0.2s;
	}

	.provider-tab:hover {
		background: #1e2336;
		color: #eef0f8;
	}

	.provider-tab.active {
		background: rgba(106, 176, 76, 0.15);
		border-color: rgba(106, 176, 76, 0.4);
		color: #eef0f8;
	}

	.provider-icon {
		display: flex;
		align-items: center;
		justify-content: center;
		width: 24px;
		height: 24px;
		border-radius: 4px;
		font-size: 11px;
		font-weight: 700;
	}

	.provider-icon.curseforge {
		background: linear-gradient(135deg, #f16436, #e04e17);
		color: white;
	}

	.provider-icon.modrinth {
		background: linear-gradient(135deg, #1bd96a, #0da540);
		color: white;
	}

	.provider-content {
		background: #1a1e2f;
		border-radius: 16px;
		padding: 20px;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.35);
	}

	.setup-required {
		background: linear-gradient(135deg, #1a1e2f 0%, #141827 100%);
		border: 1px solid rgba(255, 193, 7, 0.3);
		border-radius: 16px;
		padding: 40px;
		text-align: center;
		max-width: 600px;
		margin: 0 auto;
	}

	.setup-icon {
		color: #ffd54f;
		margin-bottom: 16px;
	}

	.setup-required h3 {
		margin: 0 0 12px;
		font-size: 22px;
		color: #eef0f8;
	}

	.setup-required > p {
		margin: 0 0 24px;
		color: #9aa2c5;
		font-size: 15px;
		line-height: 1.6;
	}

	.btn-setup {
		display: inline-block;
		background: var(--mc-grass);
		color: #fff;
		padding: 12px 28px;
		border-radius: 8px;
		font-weight: 600;
		font-size: 15px;
		text-decoration: none;
		transition: background 0.2s;
	}

	.btn-setup:hover {
		background: var(--mc-grass-dark);
	}

	.admin-note {
		background: rgba(88, 101, 242, 0.1);
		border: 1px solid rgba(88, 101, 242, 0.3);
		border-radius: 8px;
		padding: 14px 20px;
		color: #a5b4fc;
		font-size: 14px;
	}

	.setup-help {
		margin-top: 32px;
		text-align: left;
	}

	.setup-help details {
		background: rgba(20, 24, 39, 0.6);
		border: 1px solid #2a2f47;
		border-radius: 10px;
		padding: 16px;
	}

	.setup-help summary {
		cursor: pointer;
		color: #a5b4fc;
		font-weight: 500;
		margin-bottom: 8px;
	}

	.setup-help ol {
		margin: 12px 0 0;
		padding-left: 20px;
		color: #9aa2c5;
		font-size: 14px;
		line-height: 1.8;
	}

	.setup-help a {
		color: #6ab04c;
		text-decoration: none;
	}

	.setup-help a:hover {
		text-decoration: underline;
	}

	@media (max-width: 640px) {
		.controls-section {
			flex-direction: column;
			align-items: stretch;
		}

		.server-selector label {
			flex-direction: column;
			align-items: stretch;
			gap: 8px;
		}

		.server-selector select {
			min-width: 100%;
		}

		.provider-tabs {
			justify-content: center;
		}
	}
</style>
