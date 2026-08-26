<script lang="ts">
	import { enhance } from '$app/forms';
	import { invalidateAll } from '$app/navigation';
	import type { PageData, ActionData } from './$types';
	import type { VelocityConfig } from '$lib/api/types';
	import BungeeConfigEditor from './BungeeConfigEditor.svelte';
	import ProxyBackendRollup from '$lib/components/ProxyBackendRollup.svelte';
	import {
		splitBackendList,
		forcedHostOptions as computeForcedHostOptions,
		toggleForcedHostBackend as toggleBackendInList
	} from '$lib/utils/proxy';

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

	// The Velocity branch only runs when proxyKind === 'velocity'; the destructured
	// $state below is initialized from velocityConfig in that branch (null otherwise).
	let config = $state<VelocityConfig>({ ...(data.velocityConfig?.data ?? emptyConfig()) });
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
			const fresh = data.velocityConfig?.data ?? emptyConfig();
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

	// Backend names defined above, for the try-order and forced-host pickers. Derived so
	// renaming a backend immediately re-offers it under its new name.
	const backendNames = $derived(
		serverEntries.map((e) => e.name.trim()).filter((n) => n.length > 0)
	);

	/**
	 * Options for a picker whose current value may not (yet) be a defined backend —
	 * a name typed before the backend existed, or one whose backend was renamed. Keeping
	 * the stale value as an option means opening the page cannot silently rewrite config
	 * that is already on disk.
	 */
	function optionsFor(current: string): string[] {
		const value = current.trim();
		return value.length > 0 && !backendNames.includes(value)
			? [value, ...backendNames]
			: backendNames;
	}

	/** Forced hosts are stored as a comma-separated string; the picker works in names. */
	const splitServers = splitBackendList;

	/** The options one forced-host row offers, against the backends defined above. */
	function forcedHostOptions(servers: string): string[] {
		return computeForcedHostOptions(splitServers(servers), backendNames);
	}

	function toggleForcedHostBackend(idx: number, name: string, checked: boolean) {
		const entry = forcedHostEntries[idx];
		if (!entry) return;
		entry.servers = toggleBackendInList(splitServers(entry.servers), name, checked).join(', ');
	}

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
	<title>Proxy Config | {data.serverName} | MineOS</title>
</svelte:head>

<ProxyBackendRollup summary={data.backends?.data ?? null} />

{#if data.proxyKind !== 'velocity'}
	<BungeeConfigEditor
		serverName={data.serverName}
		initial={data.bungeeConfig?.data ?? null}
		formError={form && 'error' in form ? form.error : null}
		formSuccess={form?.success ?? false}
	/>
{:else}
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
				// reset: false — the default resets the form element, which blanks every
				// dynamically rendered backend/try/forced-host row on a successful save.
				// The values were written; the editor just erased itself in front of you.
				await update({ reset: false });
				await invalidateAll();
				saving = false;
				initial = JSON.parse(JSON.stringify(buildSubmitConfig()));
			};
		}}
	>
		<input type="hidden" name="proxyKind" value="velocity" />
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
				Backends to try in order when a player joins or gets kicked from a backend.
			</p>
			{#if tryList.length === 0}
				<p class="empty">No try list configured.</p>
			{:else}
				<div class="try-rows">
					{#each tryList as name, i}
						<div class="try-row">
							<select bind:value={tryList[i]}>
								<option value="">Select a backend…</option>
								{#each optionsFor(name) as backend (backend)}
									<option value={backend}>{backend}</option>
								{/each}
							</select>
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
				Route players to specific backends based on the hostname they connect with. Tick one or
				more of the backends defined above; the number shows the order they are tried in.
			</p>
			{#if forcedHostEntries.length === 0}
				<p class="empty">No forced hosts configured.</p>
			{:else}
				<div class="forced-rows">
					{#each forcedHostEntries as entry, i}
						{@const chosen = splitServers(entry.servers)}
						<div class="forced-row">
							<div class="forced-head">
								<input
									type="text"
									placeholder="hostname (e.g. lobby.example.com)"
									bind:value={entry.hostname}
								/>
								<button
									class="btn btn-icon"
									type="button"
									title="Remove"
									onclick={() => removeForcedHost(i)}>×</button
								>
							</div>
							{#if forcedHostOptions(entry.servers).length === 0}
								<p class="empty">
									Add a backend server above to route this hostname to it.
								</p>
							{:else}
								<div class="backend-list">
									{#each forcedHostOptions(entry.servers) as backend (backend)}
										{@const order = chosen.indexOf(backend)}
										<label class="backend-option" class:selected={order >= 0}>
											<input
												type="checkbox"
												checked={order >= 0}
												onchange={(event) =>
													toggleForcedHostBackend(i, backend, event.currentTarget.checked)}
											/>
											<span class="backend-name">{backend}</span>
											{#if order >= 0}
												<span class="backend-order">{order + 1}</span>
											{/if}
										</label>
									{/each}
								</div>
							{/if}
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
{/if}

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
		color: var(--mc-text-muted);
		font-size: 0.9rem;
	}

	.subtitle code,
	.help code,
	.card-description code {
		background: var(--mc-panel-light);
		padding: 0.05rem 0.3rem;
		border-radius: 0.2rem;
		font-size: 0.85em;
	}

	.hint {
		padding: 0.6rem 0.9rem;
		font-size: 0.85rem;
		color: var(--color-info-light);
		background: var(--color-info-bg);
		border: 1px solid var(--color-info-border);
		border-radius: 0.5rem;
	}

	.error {
		padding: 0.6rem 0.9rem;
		font-size: 0.9rem;
		color: var(--color-error-light);
		background: var(--color-error-bg);
		border: 1px solid var(--color-error-border);
		border-radius: 0.5rem;
	}

	.success {
		padding: 0.6rem 0.9rem;
		font-size: 0.9rem;
		color: var(--color-success-light);
		background: var(--color-success-bg);
		border: 1px solid var(--color-success-border);
		border-radius: 0.5rem;
	}

	form {
		display: flex;
		flex-direction: column;
		gap: 1rem;
	}

	.card {
		background: var(--mc-panel);
		border: 1px solid var(--border-color);
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
		color: var(--mc-text-muted);
	}

	.empty {
		margin: 0.5rem 0 0;
		font-size: 0.85rem;
		color: var(--mc-text-muted);
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
		color: var(--mc-text-secondary);
	}

	.field input[type='text'],
	.field input[type='number'],
	.field select {
		padding: 0.4rem 0.6rem;
		background: var(--input-bg);
		border: 1px solid var(--border-color);
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
		color: var(--mc-text-muted);
	}

	.server-rows,
	.try-rows,
	.forced-rows {
		display: flex;
		flex-direction: column;
		gap: 0.4rem;
	}

	.forced-rows {
		gap: 0.6rem;
	}

	.forced-row {
		display: flex;
		flex-direction: column;
		gap: 0.5rem;
		padding: 0.6rem;
		border: 1px solid var(--border-color);
		border-radius: 0.5rem;
		background: var(--mc-panel-dark);
	}

	.forced-head {
		display: grid;
		grid-template-columns: 1fr auto;
		gap: 0.5rem;
	}

	.backend-list {
		display: flex;
		flex-wrap: wrap;
		gap: 0.4rem;
	}

	.backend-option {
		display: flex;
		align-items: center;
		gap: 0.45rem;
		padding: 0.3rem 0.6rem;
		border: 1px solid var(--border-color);
		border-radius: 999px;
		background: var(--input-bg);
		font-size: 0.85rem;
		cursor: pointer;
	}

	.backend-option:hover {
		border-color: var(--mc-grass);
	}

	.backend-option.selected {
		border-color: var(--mc-grass);
		color: var(--mc-text);
	}

	.backend-option input[type='checkbox'] {
		accent-color: var(--mc-grass);
		width: 0.9rem;
		height: 0.9rem;
		cursor: pointer;
	}

	.backend-order {
		min-width: 1.2rem;
		padding: 0 0.3rem;
		border-radius: 999px;
		background: var(--mc-grass);
		color: var(--mc-panel-darkest);
		font-size: 0.7rem;
		font-weight: 700;
		text-align: center;
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
	.server-row select,
	.try-row select,
	.forced-head input {
		padding: 0.35rem 0.55rem;
		background: var(--input-bg);
		border: 1px solid var(--border-color);
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
		background: var(--mc-grass);
		color: #fff;
	}

	.btn-primary:hover:not(:disabled) {
		filter: brightness(1.08);
	}

	.btn-primary:disabled {
		opacity: 0.4;
		cursor: not-allowed;
	}

	.btn-secondary {
		background: var(--mc-panel-light);
		color: var(--mc-text-secondary);
	}

	.btn-secondary:hover:not(:disabled) {
		background: var(--mc-panel-lighter);
	}

	.btn-secondary:disabled {
		opacity: 0.4;
		cursor: not-allowed;
	}

	.btn-icon {
		background: transparent;
		color: var(--mc-text-muted);
		border: 1px solid var(--border-color);
		padding: 0.2rem 0.55rem;
		font-size: 1rem;
		line-height: 1;
	}

	.btn-icon:hover {
		color: var(--color-error-light);
		border-color: var(--color-error-border);
	}

	.actions {
		display: flex;
		justify-content: flex-end;
		gap: 0.6rem;
	}
</style>
