<script lang="ts">
	import { enhance } from '$app/forms';
	import { invalidateAll } from '$app/navigation';
	import type { PageData, ActionData } from './$types';
	import type { VelocityConfig } from '$lib/api/types';

	let { data, form }: { data: PageData; form: ActionData } = $props();

	function emptyConfig(): VelocityConfig {
		return {
			exists: false,
			bind: '0.0.0.0:25577',
			motd: '<#09add3>A Velocity Server',
			showMaxPlayers: 500,
			onlineMode: true,
			forceKeyAuthentication: true,
			preventClientProxyConnections: false,
			playerInfoForwardingMode: 'none',
			forwardingSecretFile: 'forwarding.secret',
			announceForge: false,
			kickExistingPlayers: false,
			pingPassthrough: 'DISABLED',
			enablePlayerAddressLogging: true,
			servers: {},
			try: [],
			forcedHosts: {}
		};
	}

	let config = $state<VelocityConfig>({ ...(data.config.data ?? emptyConfig()) });
	let initial = $state<VelocityConfig>(JSON.parse(JSON.stringify(config)));
	let lastServerName = $state(data.serverName);

	let serverEntries = $state<{ name: string; address: string }[]>(
		Object.entries(config.servers).map(([name, address]) => ({ name, address }))
	);
	let tryList = $state<string[]>([...config.try]);
	let forcedHostEntries = $state<{ hostname: string; servers: string }[]>(
		Object.entries(config.forcedHosts).map(([hostname, servers]) => ({
			hostname,
			servers: servers.join(', ')
		}))
	);
	let saving = $state(false);

	$effect(() => {
		if (data.serverName !== lastServerName) {
			lastServerName = data.serverName;
			const fresh = data.config.data ?? emptyConfig();
			config = { ...fresh };
			initial = JSON.parse(JSON.stringify(fresh));
			serverEntries = Object.entries(fresh.servers).map(([name, address]) => ({
				name,
				address
			}));
			tryList = [...fresh.try];
			forcedHostEntries = Object.entries(fresh.forcedHosts).map(([hostname, servers]) => ({
				hostname,
				servers: servers.join(', ')
			}));
		}
	});

	function buildForcedHostsObject(): Record<string, string[]> {
		const result: Record<string, string[]> = {};
		for (const e of forcedHostEntries) {
			const host = e.hostname.trim();
			if (!host) continue;
			const servers = e.servers
				.split(',')
				.map((s) => s.trim())
				.filter((s) => s.length > 0);
			result[host] = servers;
		}
		return result;
	}

	const dirty = $derived.by(() => {
		const current: VelocityConfig = {
			...config,
			servers: Object.fromEntries(
				serverEntries
					.filter((e) => e.name.trim().length > 0)
					.map((e) => [e.name.trim(), e.address.trim()])
			),
			try: tryList.filter((n) => n.trim().length > 0),
			forcedHosts: buildForcedHostsObject()
		};
		return JSON.stringify(current) !== JSON.stringify(initial);
	});

	function buildSubmitConfig(): VelocityConfig {
		return {
			...config,
			servers: Object.fromEntries(
				serverEntries
					.filter((e) => e.name.trim().length > 0)
					.map((e) => [e.name.trim(), e.address.trim()])
			),
			try: tryList.filter((n) => n.trim().length > 0),
			forcedHosts: buildForcedHostsObject()
		};
	}

	function addServer() {
		serverEntries = [...serverEntries, { name: '', address: '' }];
	}

	function removeServer(idx: number) {
		const removedName = serverEntries[idx]?.name.trim();
		serverEntries = serverEntries.filter((_, i) => i !== idx);
		if (removedName) {
			tryList = tryList.filter((n) => n !== removedName);
		}
	}

	function addTry() {
		tryList = [...tryList, ''];
	}

	function removeTry(idx: number) {
		tryList = tryList.filter((_, i) => i !== idx);
	}

	function addForcedHost() {
		forcedHostEntries = [...forcedHostEntries, { hostname: '', servers: '' }];
	}

	function removeForcedHost(idx: number) {
		forcedHostEntries = forcedHostEntries.filter((_, i) => i !== idx);
	}

	function resetForm() {
		config = JSON.parse(JSON.stringify(initial));
		serverEntries = Object.entries(initial.servers).map(([name, address]) => ({
			name,
			address
		}));
		tryList = [...initial.try];
		forcedHostEntries = Object.entries(initial.forcedHosts).map(([hostname, servers]) => ({
			hostname,
			servers: servers.join(', ')
		}));
	}
</script>

<svelte:head>
	<title>Velocity Config | {data.serverName} | MineOS</title>
</svelte:head>

<div class="page">
	<header class="header">
		<div>
			<h1>Velocity Configuration</h1>
			<p class="subtitle">
				Edit <code>velocity.toml</code>. Changes require a restart to take effect.
			</p>
		</div>
		<div class="header-actions">
			<button class="btn btn-secondary" type="button" onclick={resetForm} disabled={!dirty}>
				Reset
			</button>
		</div>
	</header>

	{#if !config.exists}
		<div class="hint">
			<strong>velocity.toml has not been generated yet.</strong> Showing Velocity defaults — saving
			here will create the file. Alternatively, start the server once and it will generate the
			file with its own defaults.
		</div>
	{/if}

	{#if form?.error}
		<div class="error">{form.error}</div>
	{:else if form?.success}
		<div class="success">Saved. Restart the proxy for changes to apply.</div>
	{/if}

	<form
		method="POST"
		use:enhance={() => {
			saving = true;
			return async ({ update }) => {
				await update();
				await invalidateAll();
				saving = false;
				initial = JSON.parse(JSON.stringify(buildSubmitConfig()));
			};
		}}
	>
		<input type="hidden" name="config" value={JSON.stringify(buildSubmitConfig())} />

		<section class="card">
			<h2>Network</h2>
			<div class="grid">
				<label class="field">
					<span class="label">Bind address</span>
					<input type="text" bind:value={config.bind} placeholder="0.0.0.0:25577" />
					<span class="help">IP and port the proxy listens on. Default port is 25577.</span>
				</label>
				<label class="field">
					<span class="label">MOTD</span>
					<input type="text" bind:value={config.motd} />
					<span class="help">Server list message. Supports MiniMessage format.</span>
				</label>
				<label class="field">
					<span class="label">Show max players</span>
					<input type="number" bind:value={config.showMaxPlayers} min="0" max="100000" />
					<span class="help">Cosmetic player cap shown in server list.</span>
				</label>
				<label class="field checkbox">
					<input type="checkbox" bind:checked={config.onlineMode} />
					<span class="label">Online mode</span>
					<span class="help">Authenticate players with Mojang.</span>
				</label>
				<label class="field checkbox">
					<input type="checkbox" bind:checked={config.forceKeyAuthentication} />
					<span class="label">Force key authentication</span>
					<span class="help">Require signed messages from clients.</span>
				</label>
				<label class="field checkbox">
					<input type="checkbox" bind:checked={config.preventClientProxyConnections} />
					<span class="label">Prevent client-side proxies</span>
					<span class="help">Weak VPN/proxy filter; can have false positives.</span>
				</label>
			</div>
		</section>

		<section class="card">
			<h2>Forwarding</h2>
			<div class="grid">
				<label class="field">
					<span class="label">Player info forwarding mode</span>
					<select bind:value={config.playerInfoForwardingMode}>
						<option value="none">none</option>
						<option value="legacy">legacy (BungeeCord-compatible)</option>
						<option value="bungeeguard">bungeeguard</option>
						<option value="modern">modern (recommended for Paper backends)</option>
					</select>
					<span class="help"
						>Use <code>modern</code> if your backends are Paper 1.13+ and you control them.</span
					>
				</label>
				<label class="field">
					<span class="label">Forwarding secret file</span>
					<input type="text" bind:value={config.forwardingSecretFile} />
					<span class="help"
						>Filename in the server directory holding the modern/bungeeguard secret. Only
						used when forwarding mode is <code>modern</code> or <code>bungeeguard</code>.
						Older Velocity 3.x versions write the secret inline into <code>velocity.toml</code>
						(as <code>forwarding-secret</code>) instead of creating this file.</span
					>
				</label>
				<label class="field">
					<span class="label">Ping passthrough</span>
					<select bind:value={config.pingPassthrough}>
						<option value="DISABLED">DISABLED</option>
						<option value="MODS">MODS</option>
						<option value="DESCRIPTION">DESCRIPTION</option>
						<option value="ALL">ALL</option>
					</select>
					<span class="help">What server-list info gets forwarded from the backend.</span>
				</label>
				<label class="field checkbox">
					<input type="checkbox" bind:checked={config.announceForge} />
					<span class="label">Announce Forge</span>
				</label>
				<label class="field checkbox">
					<input type="checkbox" bind:checked={config.kickExistingPlayers} />
					<span class="label">Kick existing players on reconnect</span>
				</label>
				<label class="field checkbox">
					<input type="checkbox" bind:checked={config.enablePlayerAddressLogging} />
					<span class="label">Log player addresses</span>
				</label>
			</div>
		</section>

		<section class="card">
			<div class="card-header">
				<h2>Backend servers</h2>
				<button class="btn btn-secondary" type="button" onclick={addServer}>+ Add</button>
			</div>
			<p class="card-description">
				Map a name to a backend Minecraft server's <code>host:port</code>.
			</p>
			{#if serverEntries.length === 0}
				<p class="empty">No backends configured. Velocity won't have anywhere to route players.</p>
			{:else}
				<div class="server-rows">
					{#each serverEntries as entry, i}
						<div class="server-row">
							<input type="text" placeholder="Name (e.g. lobby)" bind:value={entry.name} />
							<input
								type="text"
								placeholder="host:port (e.g. 127.0.0.1:30066)"
								bind:value={entry.address}
							/>
							<button
								class="btn btn-icon"
								type="button"
								title="Remove"
								onclick={() => removeServer(i)}>×</button
							>
						</div>
					{/each}
				</div>
			{/if}
		</section>

		<section class="card">
			<div class="card-header">
				<h2>Try order</h2>
				<button class="btn btn-secondary" type="button" onclick={addTry}>+ Add</button>
			</div>
			<p class="card-description">
				Server names to try in order when a player joins or gets kicked from a backend. Names must
				match entries above.
			</p>
			{#if tryList.length === 0}
				<p class="empty">No try list configured.</p>
			{:else}
				<div class="try-rows">
					{#each tryList as name, i}
						<div class="try-row">
							<input type="text" placeholder="Backend name" bind:value={tryList[i]} />
							<button
								class="btn btn-icon"
								type="button"
								title="Remove"
								onclick={() => removeTry(i)}>×</button
							>
						</div>
					{/each}
				</div>
			{/if}
		</section>

		<section class="card">
			<div class="card-header">
				<h2>Forced hosts</h2>
				<button class="btn btn-secondary" type="button" onclick={addForcedHost}>+ Add</button>
			</div>
			<p class="card-description">
				Route players to specific backends based on the hostname they connect with.
				Servers is a comma-separated list of backend names from above (tried in order).
			</p>
			{#if forcedHostEntries.length === 0}
				<p class="empty">No forced hosts configured.</p>
			{:else}
				<div class="server-rows">
					{#each forcedHostEntries as entry, i}
						<div class="server-row">
							<input
								type="text"
								placeholder="hostname (e.g. lobby.example.com)"
								bind:value={entry.hostname}
							/>
							<input
								type="text"
								placeholder="servers (e.g. lobby, fallback)"
								bind:value={entry.servers}
							/>
							<button
								class="btn btn-icon"
								type="button"
								title="Remove"
								onclick={() => removeForcedHost(i)}>×</button
							>
						</div>
					{/each}
				</div>
			{/if}
		</section>

		<div class="actions">
			<button class="btn btn-primary" type="submit" disabled={!dirty || saving}>
				{saving ? 'Saving…' : 'Save'}
			</button>
		</div>
	</form>
</div>

<style>
	.page {
		padding: 1.5rem 2rem;
		display: flex;
		flex-direction: column;
		gap: 1rem;
	}

	.header {
		display: flex;
		justify-content: space-between;
		align-items: flex-start;
		gap: 1rem;
	}

	.header h1 {
		margin: 0 0 4px;
		font-size: 1.6rem;
		font-weight: 700;
	}

	.subtitle {
		margin: 0;
		color: var(--mc-text-muted, #9aa2c5);
		font-size: 0.9rem;
	}

	.subtitle code,
	.help code,
	.card-description code {
		background: rgba(255, 255, 255, 0.08);
		padding: 0.05rem 0.3rem;
		border-radius: 0.2rem;
		font-size: 0.85em;
	}

	.hint {
		padding: 0.6rem 0.9rem;
		font-size: 0.85rem;
		color: var(--mc-text-secondary, #c4cff5);
		background: rgba(6, 182, 212, 0.08);
		border: 1px solid rgba(6, 182, 212, 0.25);
		border-radius: 0.5rem;
	}

	.error {
		padding: 0.6rem 0.9rem;
		font-size: 0.9rem;
		color: #fecaca;
		background: rgba(239, 68, 68, 0.12);
		border: 1px solid rgba(239, 68, 68, 0.3);
		border-radius: 0.5rem;
	}

	.success {
		padding: 0.6rem 0.9rem;
		font-size: 0.9rem;
		color: #bbf7d0;
		background: rgba(34, 197, 94, 0.12);
		border: 1px solid rgba(34, 197, 94, 0.3);
		border-radius: 0.5rem;
	}

	form {
		display: flex;
		flex-direction: column;
		gap: 1rem;
	}

	.card {
		background: var(--mc-panel, rgba(22, 27, 46, 0.95));
		border: 1px solid var(--border-color, #2a2f47);
		border-radius: 0.75rem;
		padding: 1.25rem;
	}

	.card h2 {
		margin: 0 0 0.75rem;
		font-size: 1.05rem;
		font-weight: 600;
	}

	.card-header {
		display: flex;
		justify-content: space-between;
		align-items: center;
		margin-bottom: 0.5rem;
	}

	.card-header h2 {
		margin: 0;
	}

	.card-description {
		margin: 0 0 0.75rem;
		font-size: 0.85rem;
		color: var(--mc-text-muted, #9aa2c5);
	}

	.empty {
		margin: 0.5rem 0 0;
		font-size: 0.85rem;
		color: var(--mc-text-muted, #9aa2c5);
		font-style: italic;
	}

	.grid {
		display: grid;
		grid-template-columns: repeat(2, minmax(0, 1fr));
		gap: 0.75rem 1rem;
	}

	@media (max-width: 720px) {
		.grid {
			grid-template-columns: 1fr;
		}
	}

	.field {
		display: flex;
		flex-direction: column;
		gap: 0.25rem;
	}

	.field .label {
		font-size: 0.85rem;
		font-weight: 500;
		color: var(--mc-text-secondary, #c4cff5);
	}

	.field input[type='text'],
	.field input[type='number'],
	.field select {
		padding: 0.4rem 0.6rem;
		background: var(--input-bg, #1f2937);
		border: 1px solid var(--border-color, #374151);
		border-radius: 0.375rem;
		color: inherit;
		font-size: 0.9rem;
		font-family: inherit;
	}

	.field.checkbox {
		flex-direction: row;
		align-items: center;
		gap: 0.5rem;
		flex-wrap: wrap;
	}

	.field.checkbox input[type='checkbox'] {
		width: 1rem;
		height: 1rem;
		flex-shrink: 0;
	}

	.field.checkbox .label {
		flex: 1 1 auto;
	}

	.field.checkbox .help {
		flex-basis: 100%;
		margin-left: 1.5rem;
	}

	.help {
		font-size: 0.75rem;
		color: var(--mc-text-muted, #9aa2c5);
	}

	.server-rows,
	.try-rows {
		display: flex;
		flex-direction: column;
		gap: 0.4rem;
	}

	.server-row {
		display: grid;
		grid-template-columns: 1fr 2fr auto;
		gap: 0.5rem;
	}

	.try-row {
		display: grid;
		grid-template-columns: 1fr auto;
		gap: 0.5rem;
	}

	.server-row input,
	.try-row input {
		padding: 0.35rem 0.55rem;
		background: var(--input-bg, #1f2937);
		border: 1px solid var(--border-color, #374151);
		border-radius: 0.35rem;
		color: inherit;
		font-size: 0.85rem;
		font-family: inherit;
	}

	.btn {
		padding: 0.4rem 0.9rem;
		border-radius: 0.4rem;
		border: 1px solid transparent;
		font-size: 0.85rem;
		font-weight: 500;
		cursor: pointer;
		font-family: inherit;
	}

	.btn-primary {
		background: #06b6d4;
		color: #0b1220;
	}

	.btn-primary:hover:not(:disabled) {
		filter: brightness(1.08);
	}

	.btn-primary:disabled {
		opacity: 0.4;
		cursor: not-allowed;
	}

	.btn-secondary {
		background: var(--mc-panel-light, #2a2f47);
		color: var(--mc-text-secondary, #c4cff5);
	}

	.btn-secondary:hover:not(:disabled) {
		background: var(--mc-panel-lighter, #3a3f5a);
	}

	.btn-secondary:disabled {
		opacity: 0.4;
		cursor: not-allowed;
	}

	.btn-icon {
		background: transparent;
		color: var(--mc-text-muted, #9aa2c5);
		border: 1px solid var(--border-color, #374151);
		padding: 0.2rem 0.55rem;
		font-size: 1rem;
		line-height: 1;
	}

	.btn-icon:hover {
		color: #fecaca;
		border-color: rgba(239, 68, 68, 0.4);
	}

	.actions {
		display: flex;
		justify-content: flex-end;
		gap: 0.6rem;
	}
</style>
