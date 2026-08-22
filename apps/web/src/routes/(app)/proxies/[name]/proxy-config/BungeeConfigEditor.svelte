<script lang="ts">
	import { enhance } from '$app/forms';
	import { invalidateAll } from '$app/navigation';
	import type { BungeeConfig, BungeeBackend } from '$lib/api/types';

	interface Props {
		serverName: string;
		initial: BungeeConfig | null;
		formError?: string | null;
		formSuccess?: boolean;
	}

	let { serverName, initial, formError, formSuccess }: Props = $props();

	function emptyConfig(): BungeeConfig {
		return {
			exists: false,
			onlineMode: true,
			ipForward: false,
			playerLimit: -1,
			timeout: 30000,
			networkCompressionThreshold: 256,
			forgeSupport: false,
			logCommands: false,
			logPings: false,
			connectionThrottle: 4000,
			connectionThrottleLimit: 3,
			host: '0.0.0.0:25577',
			motd: '&1Another BungeeCord server',
			maxPlayers: 1,
			queryEnabled: false,
			queryPort: 25577,
			pingPassthrough: false,
			forceDefaultServer: false,
			tabList: 'GLOBAL_PING',
			proxyProtocol: false,
			priorities: [],
			forcedHosts: {},
			servers: {}
		};
	}

	let config = $state<BungeeConfig>({ ...(initial ?? emptyConfig()) });
	let initialState = $state<BungeeConfig>(JSON.parse(JSON.stringify(config)));
	let lastServerName = $state(serverName);

	type ServerEntry = { name: string; address: string; motd: string; restricted: boolean };
	let serverEntries = $state<ServerEntry[]>(
		Object.entries(config.servers).map(([name, b]) => ({
			name,
			address: b.address,
			motd: b.motd,
			restricted: b.restricted
		}))
	);
	let priorities = $state<string[]>([...config.priorities]);
	let forcedHostEntries = $state<{ hostname: string; server: string }[]>(
		Object.entries(config.forcedHosts).map(([hostname, server]) => ({ hostname, server }))
	);
	let saving = $state(false);

	$effect(() => {
		if (serverName !== lastServerName) {
			lastServerName = serverName;
			const fresh = initial ?? emptyConfig();
			config = { ...fresh };
			initialState = JSON.parse(JSON.stringify(fresh));
			serverEntries = Object.entries(fresh.servers).map(([name, b]) => ({
				name,
				address: b.address,
				motd: b.motd,
				restricted: b.restricted
			}));
			priorities = [...fresh.priorities];
			forcedHostEntries = Object.entries(fresh.forcedHosts).map(([hostname, server]) => ({
				hostname,
				server
			}));
		}
	});

	function buildServersObject(): Record<string, BungeeBackend> {
		const result: Record<string, BungeeBackend> = {};
		for (const e of serverEntries) {
			const name = e.name.trim();
			if (!name) continue;
			result[name] = {
				address: e.address.trim(),
				motd: e.motd,
				restricted: e.restricted
			};
		}
		return result;
	}

	function buildForcedHostsObject(): Record<string, string> {
		const result: Record<string, string> = {};
		for (const e of forcedHostEntries) {
			const host = e.hostname.trim();
			const target = e.server.trim();
			if (!host || !target) continue;
			result[host] = target;
		}
		return result;
	}

	function buildSubmitConfig(): BungeeConfig {
		return {
			...config,
			servers: buildServersObject(),
			priorities: priorities.filter((n) => n.trim().length > 0),
			forcedHosts: buildForcedHostsObject()
		};
	}

	const dirty = $derived.by(() => {
		const current = buildSubmitConfig();
		return JSON.stringify(current) !== JSON.stringify(initialState);
	});

	function addServer() {
		serverEntries = [
			...serverEntries,
			{ name: '', address: '', motd: '&1Backend server', restricted: false }
		];
	}

	function removeServer(idx: number) {
		const removedName = serverEntries[idx]?.name.trim();
		serverEntries = serverEntries.filter((_, i) => i !== idx);
		if (removedName) {
			priorities = priorities.filter((n) => n !== removedName);
		}
	}

	function addPriority() {
		priorities = [...priorities, ''];
	}

	function removePriority(idx: number) {
		priorities = priorities.filter((_, i) => i !== idx);
	}

	function addForcedHost() {
		forcedHostEntries = [...forcedHostEntries, { hostname: '', server: '' }];
	}

	function removeForcedHost(idx: number) {
		forcedHostEntries = forcedHostEntries.filter((_, i) => i !== idx);
	}

	function resetForm() {
		config = JSON.parse(JSON.stringify(initialState));
		serverEntries = Object.entries(initialState.servers).map(([name, b]) => ({
			name,
			address: b.address,
			motd: b.motd,
			restricted: b.restricted
		}));
		priorities = [...initialState.priorities];
		forcedHostEntries = Object.entries(initialState.forcedHosts).map(([hostname, server]) => ({
			hostname,
			server
		}));
	}

</script>

<div class="page">
	<header class="header">
		<div>
			<h1>BungeeCord Configuration</h1>
			<p class="subtitle">
				Edit <code>config.yml</code>. Changes require a restart to take effect.
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
			<strong>config.yml has not been generated yet.</strong> Showing BungeeCord defaults — saving
			here will create the file. Alternatively, start the server once and it will generate the file
			with its own defaults.
		</div>
	{/if}

	{#if formError}
		<div class="error">{formError}</div>
	{:else if formSuccess}
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
				initialState = JSON.parse(JSON.stringify(buildSubmitConfig()));
			};
		}}
	>
		<input type="hidden" name="proxyKind" value="bungeecord" />
		<input type="hidden" name="config" value={JSON.stringify(buildSubmitConfig())} />

		<section class="card">
			<h2>Network</h2>
			<div class="grid">
				<label class="field">
					<span class="label">Bind address</span>
					<input type="text" bind:value={config.host} placeholder="0.0.0.0:25577" />
					<span class="help">IP and port the proxy listens on. Default port is 25577.</span>
				</label>
				<label class="field">
					<span class="label">MOTD</span>
					<input type="text" bind:value={config.motd} />
					<span class="help">Server list message. Use color codes like <code>&amp;1</code>.</span>
				</label>
				<label class="field">
					<span class="label">Max players (cosmetic)</span>
					<input type="number" bind:value={config.maxPlayers} min="0" max="100000" />
					<span class="help">Slot count shown in the server list. Not the real cap.</span>
				</label>
				<label class="field">
					<span class="label">Player limit (real cap)</span>
					<input type="number" bind:value={config.playerLimit} min="-1" max="100000" />
					<span class="help">Hard cap on connected players. <code>-1</code> for unlimited.</span>
				</label>
				<label class="field checkbox">
					<input type="checkbox" bind:checked={config.onlineMode} />
					<span class="label">Online mode</span>
					<span class="help">Authenticate players with Mojang.</span>
				</label>
				<label class="field checkbox">
					<input type="checkbox" bind:checked={config.queryEnabled} />
					<span class="label">Enable query</span>
					<span class="help">Expose the GameSpy4 query protocol.</span>
				</label>
				<label class="field">
					<span class="label">Query port</span>
					<input type="number" bind:value={config.queryPort} min="1" max="65535" />
					<span class="help">UDP query port. Usually matches the bind port.</span>
				</label>
			</div>
		</section>

		<section class="card">
			<h2>Forwarding</h2>
			<div class="grid">
				<label class="field checkbox">
					<input type="checkbox" bind:checked={config.ipForward} />
					<span class="label">IP forward</span>
					<span class="help"
						>Pass the player's real IP to backends. Backends must accept this — and you must
						firewall them, or anyone can spoof identities.</span
					>
				</label>
				<label class="field checkbox">
					<input type="checkbox" bind:checked={config.forgeSupport} />
					<span class="label">Forge support</span>
					<span class="help">Pass through Forge mod handshake to backends.</span>
				</label>
				<label class="field checkbox">
					<input type="checkbox" bind:checked={config.pingPassthrough} />
					<span class="label">Ping passthrough</span>
					<span class="help">Forward MOTD/version from the priority backend.</span>
				</label>
				<label class="field checkbox">
					<input type="checkbox" bind:checked={config.forceDefaultServer} />
					<span class="label">Force default server</span>
					<span class="help">Always send players to the priority list on join.</span>
				</label>
				<label class="field checkbox">
					<input type="checkbox" bind:checked={config.proxyProtocol} />
					<span class="label">PROXY protocol</span>
					<span class="help"
						>Accept HAProxy/PROXY protocol on the listener. Only enable behind a TCP load
						balancer.</span
					>
				</label>
				<label class="field">
					<span class="label">Tab list</span>
					<select bind:value={config.tabList}>
						<option value="GLOBAL_PING">GLOBAL_PING</option>
						<option value="GLOBAL">GLOBAL</option>
						<option value="SERVER">SERVER</option>
					</select>
					<span class="help">How the player tab list is composed across backends.</span>
				</label>
			</div>
		</section>

		<section class="card">
			<div class="card-header">
				<h2>Backend servers</h2>
				<button class="btn btn-secondary" type="button" onclick={addServer}>+ Add</button>
			</div>
			<p class="card-description">
				Map a name to a backend Minecraft server's <code>host:port</code>. Restricted backends
				require <code>bungeecord.server.&lt;name&gt;</code> permission.
			</p>
			{#if serverEntries.length === 0}
				<p class="empty">No backends configured. The proxy won't have anywhere to route players.</p>
			{:else}
				<div class="server-rows">
					{#each serverEntries as entry, i}
						<div class="server-row">
							<input type="text" placeholder="Name (e.g. lobby)" bind:value={entry.name} />
							<input
								type="text"
								placeholder="host:port (e.g. 127.0.0.1:25565)"
								bind:value={entry.address}
							/>
							<input type="text" placeholder="MOTD" bind:value={entry.motd} />
							<label class="restricted-toggle">
								<input type="checkbox" bind:checked={entry.restricted} />
								<span>Restricted</span>
							</label>
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
				<h2>Priority order</h2>
				<button class="btn btn-secondary" type="button" onclick={addPriority}>+ Add</button>
			</div>
			<p class="card-description">
				Backend names tried in order on join or kick fallback. Names must match entries above.
			</p>
			{#if priorities.length === 0}
				<p class="empty">No priorities configured.</p>
			{:else}
				<div class="try-rows">
					{#each priorities as _name, i}
						<div class="try-row">
							<input type="text" placeholder="Backend name" bind:value={priorities[i]} />
							<button
								class="btn btn-icon"
								type="button"
								title="Remove"
								onclick={() => removePriority(i)}>×</button
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
				Route players to a specific backend based on the hostname they connect with. BungeeCord
				accepts a single backend per hostname (unlike Velocity's list).
			</p>
			{#if forcedHostEntries.length === 0}
				<p class="empty">No forced hosts configured.</p>
			{:else}
				<div class="server-rows">
					{#each forcedHostEntries as entry, i}
						<div class="server-row two-col">
							<input
								type="text"
								placeholder="hostname (e.g. lobby.example.com)"
								bind:value={entry.hostname}
							/>
							<input
								type="text"
								placeholder="backend name (e.g. lobby)"
								bind:value={entry.server}
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

		<section class="card">
			<h2>Advanced</h2>
			<div class="grid">
				<label class="field">
					<span class="label">Timeout (ms)</span>
					<input type="number" bind:value={config.timeout} min="1000" max="600000" />
					<span class="help">Backend connection timeout in milliseconds.</span>
				</label>
				<label class="field">
					<span class="label">Network compression threshold</span>
					<input
						type="number"
						bind:value={config.networkCompressionThreshold}
						min="-1"
						max="65535"
					/>
					<span class="help">Packet size in bytes above which compression kicks in.</span>
				</label>
				<label class="field">
					<span class="label">Connection throttle (ms)</span>
					<input type="number" bind:value={config.connectionThrottle} min="0" />
					<span class="help">Per-IP cooldown between connections. <code>0</code> disables.</span>
				</label>
				<label class="field">
					<span class="label">Connection throttle limit</span>
					<input type="number" bind:value={config.connectionThrottleLimit} min="0" />
					<span class="help">Max connections per IP within the throttle window.</span>
				</label>
				<label class="field checkbox">
					<input type="checkbox" bind:checked={config.logCommands} />
					<span class="label">Log commands</span>
					<span class="help">Log every command run through the proxy.</span>
				</label>
				<label class="field checkbox">
					<input type="checkbox" bind:checked={config.logPings} />
					<span class="label">Log pings</span>
					<span class="help">
						Log every server-list ping. MineOS pings the proxy every few seconds for status,
						so leaving this on floods the console.
					</span>
				</label>
			</div>
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
		grid-template-columns: 1fr 1.5fr 1.5fr auto auto;
		gap: 0.5rem;
		align-items: center;
	}

	.server-row.two-col {
		grid-template-columns: 1fr 1fr auto;
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

	.restricted-toggle {
		display: flex;
		align-items: center;
		gap: 0.3rem;
		font-size: 0.8rem;
		color: var(--mc-text-muted, #9aa2c5);
		white-space: nowrap;
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
