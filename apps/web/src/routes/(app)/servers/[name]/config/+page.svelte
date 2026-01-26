<script lang="ts">
	import { enhance } from '$app/forms';
	import { invalidateAll } from '$app/navigation';
	import * as api from '$lib/api/client';
	import { modal } from '$lib/stores/modal';
	import type { PageData, ActionData } from './$types';

	let { data, form }: { data: PageData; form: ActionData } = $props();

	let lastServerName = $state('');
	let properties = $state<Record<string, string>>({});
	let loading = $state(false);
	let showAddModal = $state(false);
	let newKey = $state('');
	let newValue = $state('');
	let activeSection = $state('essential');
	let initialProperties = $state<Record<string, string>>({});
	let searchQuery = $state('');
	let restartLoading = $state(false);

	// Define property metadata for smart controls
	type PropertyType = 'boolean' | 'number' | 'text' | 'enum';
	type PropertyDef = {
		type: PropertyType;
		label: string;
		description?: string;
		options?: { value: string; label: string }[];
		min?: number;
		max?: number;
		default?: string;
	};

	const propertyDefinitions: Record<string, PropertyDef> = {
		// Essential Settings
		'server-port': { type: 'number', label: 'Server Port', description: 'Port the server listens on', min: 1, max: 65535, default: '25565' },
		'max-players': { type: 'number', label: 'Max Players', description: 'Maximum players allowed', min: 1, max: 1000, default: '20' },
		'motd': { type: 'text', label: 'Server Message (MOTD)', description: 'Message shown in server list' },
		'difficulty': {
			type: 'enum', label: 'Difficulty', description: 'Game difficulty level',
			options: [
				{ value: 'peaceful', label: 'Peaceful' },
				{ value: 'easy', label: 'Easy' },
				{ value: 'normal', label: 'Normal' },
				{ value: 'hard', label: 'Hard' }
			], default: 'easy'
		},
		'gamemode': {
			type: 'enum', label: 'Default Gamemode', description: 'Gamemode for new players',
			options: [
				{ value: 'survival', label: 'Survival' },
				{ value: 'creative', label: 'Creative' },
				{ value: 'adventure', label: 'Adventure' },
				{ value: 'spectator', label: 'Spectator' }
			], default: 'survival'
		},
		'pvp': { type: 'boolean', label: 'PvP', description: 'Allow player vs player combat', default: 'true' },
		'online-mode': { type: 'boolean', label: 'Online Mode', description: 'Verify players with Minecraft servers (disable for offline/cracked)', default: 'true' },
		'white-list': { type: 'boolean', label: 'Whitelist', description: 'Only allow whitelisted players', default: 'false' },
		'enforce-whitelist': { type: 'boolean', label: 'Enforce Whitelist', description: 'Kick non-whitelisted players when enabled', default: 'false' },

		// World Settings
		'level-name': { type: 'text', label: 'World Name', description: 'Name of the world folder', default: 'world' },
		'level-seed': { type: 'text', label: 'World Seed', description: 'Seed for world generation (leave blank for random)' },
		'level-type': {
			type: 'enum', label: 'World Type', description: 'Type of world generation',
			options: [
				{ value: 'minecraft:normal', label: 'Normal' },
				{ value: 'minecraft:flat', label: 'Flat' },
				{ value: 'minecraft:large_biomes', label: 'Large Biomes' },
				{ value: 'minecraft:amplified', label: 'Amplified' },
				{ value: 'minecraft:single_biome_surface', label: 'Single Biome' }
			], default: 'minecraft:normal'
		},
		'generate-structures': { type: 'boolean', label: 'Generate Structures', description: 'Generate villages, temples, etc.', default: 'true' },
		'spawn-monsters': { type: 'boolean', label: 'Spawn Monsters', description: 'Spawn hostile mobs', default: 'true' },
		'spawn-animals': { type: 'boolean', label: 'Spawn Animals', description: 'Spawn passive mobs', default: 'true' },
		'spawn-npcs': { type: 'boolean', label: 'Spawn NPCs', description: 'Spawn villagers', default: 'true' },
		'allow-nether': { type: 'boolean', label: 'Allow Nether', description: 'Enable the Nether dimension', default: 'true' },

		// Gameplay
		'hardcore': { type: 'boolean', label: 'Hardcore Mode', description: 'Players are banned on death', default: 'false' },
		'force-gamemode': { type: 'boolean', label: 'Force Gamemode', description: 'Force default gamemode on join', default: 'false' },
		'allow-flight': { type: 'boolean', label: 'Allow Flight', description: 'Allow flying in survival (anti-cheat)', default: 'false' },
		'spawn-protection': { type: 'number', label: 'Spawn Protection', description: 'Radius of spawn protection (0 to disable)', min: 0, max: 100, default: '16' },
		'max-world-size': { type: 'number', label: 'Max World Size', description: 'Maximum world radius in blocks', min: 1, max: 29999984, default: '29999984' },
		'view-distance': { type: 'number', label: 'View Distance', description: 'Render distance in chunks', min: 3, max: 32, default: '10' },
		'simulation-distance': { type: 'number', label: 'Simulation Distance', description: 'Entity simulation distance in chunks', min: 3, max: 32, default: '10' },
		'player-idle-timeout': { type: 'number', label: 'Idle Timeout', description: 'Kick idle players after minutes (0 = never)', min: 0, max: 1440, default: '0' },

		// Network
		'server-ip': { type: 'text', label: 'Server IP', description: 'IP to bind to (leave blank for all interfaces)' },
		'enable-query': { type: 'boolean', label: 'Enable Query', description: 'Enable GameSpy4 protocol server listener', default: 'false' },
		'query.port': { type: 'number', label: 'Query Port', description: 'Port for query protocol', min: 1, max: 65535, default: '25565' },
		'enable-rcon': { type: 'boolean', label: 'Enable RCON', description: 'Enable remote console access', default: 'false' },
		'rcon.port': { type: 'number', label: 'RCON Port', description: 'Port for RCON', min: 1, max: 65535, default: '25575' },
		'rcon.password': { type: 'text', label: 'RCON Password', description: 'Password for RCON access' },
		'network-compression-threshold': { type: 'number', label: 'Compression Threshold', description: 'Packet compression threshold (-1 to disable)', min: -1, max: 65535, default: '256' },
		'rate-limit': { type: 'number', label: 'Rate Limit', description: 'Max packets per second (0 = unlimited)', min: 0, max: 10000, default: '0' },

		// Advanced
		'enable-command-block': { type: 'boolean', label: 'Command Blocks', description: 'Enable command blocks', default: 'false' },
		'op-permission-level': { type: 'number', label: 'OP Permission Level', description: 'Default op permission level (1-4)', min: 1, max: 4, default: '4' },
		'function-permission-level': { type: 'number', label: 'Function Permission Level', description: 'Default function permission level (1-4)', min: 1, max: 4, default: '2' },
		'max-tick-time': { type: 'number', label: 'Max Tick Time', description: 'Max time for a tick before watchdog (ms, -1 to disable)', min: -1, max: 600000, default: '60000' },
		'sync-chunk-writes': { type: 'boolean', label: 'Sync Chunk Writes', description: 'Synchronous chunk writes (safer but slower)', default: 'true' },
		'entity-broadcast-range-percentage': { type: 'number', label: 'Entity Broadcast Range %', description: 'Entity visibility range (10-1000%)', min: 10, max: 1000, default: '100' },
		'enable-jmx-monitoring': { type: 'boolean', label: 'JMX Monitoring', description: 'Enable JMX monitoring', default: 'false' },
		'enable-status': { type: 'boolean', label: 'Enable Status', description: 'Show server in server list', default: 'true' },
		'hide-online-players': { type: 'boolean', label: 'Hide Online Players', description: 'Hide player count in server list', default: 'false' },
		'prevent-proxy-connections': { type: 'boolean', label: 'Prevent Proxy Connections', description: 'Block VPN/proxy connections', default: 'false' },
		'use-native-transport': { type: 'boolean', label: 'Native Transport', description: 'Use optimized Linux networking', default: 'true' },
		'broadcast-console-to-ops': { type: 'boolean', label: 'Broadcast Console to OPs', description: 'Send console output to ops', default: 'true' },
		'broadcast-rcon-to-ops': { type: 'boolean', label: 'Broadcast RCON to OPs', description: 'Send RCON output to ops', default: 'true' },
		'text-filtering-config': { type: 'text', label: 'Text Filtering Config', description: 'Text filter configuration file' },
		'require-resource-pack': { type: 'boolean', label: 'Require Resource Pack', description: 'Kick players who decline resource pack', default: 'false' },
		'resource-pack': { type: 'text', label: 'Resource Pack URL', description: 'URL to server resource pack' },
		'resource-pack-sha1': { type: 'text', label: 'Resource Pack SHA1', description: 'SHA1 hash of resource pack' },
		'resource-pack-prompt': { type: 'text', label: 'Resource Pack Prompt', description: 'Custom message when prompting for resource pack' }
	};

	// Define sections with their properties
	const sections = [
		{
			id: 'essential',
			label: 'Essential',
			icon: '[E]',
			description: 'Most commonly changed settings',
			properties: ['server-port', 'max-players', 'motd', 'difficulty', 'gamemode', 'pvp', 'online-mode', 'white-list', 'enforce-whitelist']
		},
		{
			id: 'world',
			label: 'World',
			icon: '[W]',
			description: 'World generation and environment',
			properties: ['level-name', 'level-seed', 'level-type', 'generate-structures', 'spawn-monsters', 'spawn-animals', 'spawn-npcs', 'allow-nether']
		},
		{
			id: 'gameplay',
			label: 'Gameplay',
			icon: '[G]',
			description: 'Player experience settings',
			properties: ['hardcore', 'force-gamemode', 'allow-flight', 'spawn-protection', 'max-world-size', 'view-distance', 'simulation-distance', 'player-idle-timeout']
		},
		{
			id: 'network',
			label: 'Network',
			icon: '[N]',
			description: 'Network and remote access',
			properties: ['server-ip', 'enable-query', 'query.port', 'enable-rcon', 'rcon.port', 'rcon.password', 'network-compression-threshold', 'rate-limit']
		},
		{
			id: 'advanced',
			label: 'Advanced',
			icon: '[A]',
			description: 'Performance and technical settings',
			properties: ['enable-command-block', 'op-permission-level', 'function-permission-level', 'max-tick-time', 'sync-chunk-writes', 'entity-broadcast-range-percentage', 'enable-jmx-monitoring', 'enable-status', 'hide-online-players', 'prevent-proxy-connections', 'use-native-transport']
		},
		{
			id: 'resources',
			label: 'Resources',
			icon: '[R]',
			description: 'Resource pack settings',
			properties: ['require-resource-pack', 'resource-pack', 'resource-pack-sha1', 'resource-pack-prompt']
		}
	];

	// Get all known property keys
	const knownProperties = new Set(Object.keys(propertyDefinitions));

	// Compute custom/unknown properties
	const customProperties = $derived.by(() => {
		return Object.keys(properties).filter(key => !knownProperties.has(key)).sort();
	});

	const restartRequiredKeys = new Set(Object.keys(propertyDefinitions));
	const normalizedQuery = $derived.by(() => searchQuery.trim().toLowerCase());
	const searchActive = $derived.by(() => normalizedQuery.length > 0);

	// Initialize properties from data when server changes
	$effect(() => {
		if (data.serverName !== lastServerName) {
			lastServerName = data.serverName;
			properties = { ...(data.properties.data || {}) };
			initialProperties = { ...(data.properties.data || {}) };
		}
	});

	function arePropertiesEqual(a: Record<string, string>, b: Record<string, string>): boolean {
		const aKeys = Object.keys(a);
		const bKeys = Object.keys(b);
		if (aKeys.length !== bKeys.length) return false;
		for (const key of aKeys) {
			if (a[key] !== b[key]) return false;
		}
		return true;
	}

	function isKeyDirty(key: string): boolean {
		return (properties[key] ?? '') !== (initialProperties[key] ?? '');
	}

	function isRestartKey(key: string): boolean {
		return restartRequiredKeys.has(key) || !knownProperties.has(key);
	}

	function hasSectionChanges(keys: string[]): boolean {
		return keys.some((key) => isKeyDirty(key));
	}

	function matchesSearch(key: string, query: string): boolean {
		if (!query) return true;
		const def = propertyDefinitions[key];
		const optionLabels = def?.options?.map((opt) => opt.label) ?? [];
		const haystack = [key, def?.label ?? '', def?.description ?? '', ...optionLabels]
			.join(' ')
			.toLowerCase();
		return haystack.includes(query);
	}

	function matchesCustomSearch(key: string, query: string): boolean {
		if (!query) return true;
		const value = properties[key] ?? '';
		return `${key} ${value}`.toLowerCase().includes(query);
	}

	function applyDefaultValue(updated: Record<string, string>, key: string) {
		const defaultValue = propertyDefinitions[key]?.default;
		if (defaultValue === undefined) {
			delete updated[key];
		} else {
			updated[key] = defaultValue;
		}
	}

	const hasChanges = $derived.by(() => !arePropertiesEqual(properties, initialProperties));
	const hasRestartChanges = $derived.by(() => {
		if (!hasChanges) return false;
		const keys = new Set([...Object.keys(properties), ...Object.keys(initialProperties)]);
		for (const key of keys) {
			if (isRestartKey(key) && isKeyDirty(key)) return true;
		}
		return false;
	});
	const matchingCustomKeys = $derived.by(() => {
		if (!searchActive) return customProperties;
		return customProperties.filter((key) => matchesCustomSearch(key, normalizedQuery));
	});
	const searchHasMatches = $derived.by(() => {
		if (!searchActive) return true;
		if (matchingCustomKeys.length > 0) return true;
		return sections.some((section) =>
			section.properties.some((key) => matchesSearch(key, normalizedQuery))
		);
	});

	function getValue(key: string): string {
		return properties[key] ?? propertyDefinitions[key]?.default ?? '';
	}

	function getEnumDisplayValue(key: string, value: string): string {
		if (!value) return value;
		if (key === 'difficulty') {
			const map: Record<string, string> = {
				'0': 'peaceful',
				'1': 'easy',
				'2': 'normal',
				'3': 'hard'
			};
			return map[value] ?? value;
		}
		if (key === 'gamemode') {
			const map: Record<string, string> = {
				'0': 'survival',
				'1': 'creative',
				'2': 'adventure',
				'3': 'spectator'
			};
			return map[value] ?? value;
		}
		if (key === 'level-type') {
			const map: Record<string, string> = {
				DEFAULT: 'minecraft:normal',
				FLAT: 'minecraft:flat',
				LARGE_BIOMES: 'minecraft:large_biomes',
				AMPLIFIED: 'minecraft:amplified'
			};
			return map[value.toUpperCase()] ?? value;
		}
		return value;
	}

	function setValue(key: string, value: string) {
		properties[key] = value;
		properties = { ...properties };
	}

	function toggleBoolean(key: string) {
		const current = getValue(key);
		setValue(key, current === 'true' ? 'false' : 'true');
	}

	function addProperty() {
		if (!newKey.trim()) return;
		properties[newKey.trim()] = newValue;
		properties = { ...properties };
		showAddModal = false;
		newKey = '';
		newValue = '';
	}

	function removeProperty(key: string) {
		delete properties[key];
		properties = { ...properties };
	}

	async function discardChanges() {
		const confirmed = await modal.confirm('Discard all unsaved changes?', 'Discard Changes');
		if (!confirmed) return;
		properties = { ...initialProperties };
	}

	async function resetProperty(key: string, label: string) {
		if (!isKeyDirty(key)) return;
		const confirmed = await modal.confirm(`Reset "${label}" to its default value?`, 'Reset Setting');
		if (!confirmed) return;
		const updated = { ...properties };
		applyDefaultValue(updated, key);
		properties = updated;
	}

	async function resetSection(sectionLabel: string, keys: string[]) {
		const confirmed = await modal.confirm(
			`Reset all ${sectionLabel} settings to their default values?`,
			'Reset Section'
		);
		if (!confirmed) return;
		const updated = { ...properties };
		for (const key of keys) {
			applyDefaultValue(updated, key);
		}
		properties = updated;
	}

	async function resetCustomProperties() {
		if (customProperties.length === 0) return;
		const confirmed = await modal.confirm(
			'Remove all custom properties? This cannot be undone.',
			'Clear Custom Properties'
		);
		if (!confirmed) return;
		const updated = { ...properties };
		for (const key of customProperties) {
			delete updated[key];
		}
		properties = updated;
	}

	async function restartServer() {
		const confirmed = await modal.confirm(
			`Restart server "${data.serverName}" now?`,
			'Restart Server'
		);
		if (!confirmed) return;
		restartLoading = true;
		try {
			const result = await api.restartServer(fetch, data.serverName);
			if (result.error) {
				await modal.error(`Failed to restart server: ${result.error}`);
			} else {
				setTimeout(() => invalidateAll(), 1000);
			}
		} finally {
			restartLoading = false;
		}
	}
</script>

{#key data.serverName}
	<div class="page">
		<div class="page-header">
			<div>
				<h2>Server Configuration</h2>
				<p class="subtitle">Configure server.properties for {data.serverName}</p>
			</div>
		</div>

		{#if data.properties.error}
			<div class="error-box">
				<p>Failed to load properties: {data.properties.error}</p>
			</div>
		{:else}
			<form
				method="POST"
				use:enhance={() => {
					loading = true;
					return async ({ update }) => {
						await update({ reset: false });
						properties = { ...(data.properties.data || {}) };
						initialProperties = { ...(data.properties.data || {}) };
						loading = false;
					};
				}}
			>
				<input type="hidden" name="properties" value={JSON.stringify(properties)} />

				<div class="config-toolbar">
					<div class="search-field">
						<input
							type="search"
							class="search-input"
							placeholder="Search settings by name or description"
							bind:value={searchQuery}
							onkeydown={(e) => e.key === 'Enter' && e.preventDefault()}
						/>
						{#if searchActive}
							<button type="button" class="btn-clear" onclick={() => searchQuery = ''}>
								Clear
							</button>
						{/if}
					</div>
					{#if hasChanges}
						<span class="dirty-pill">Unsaved changes</span>
					{/if}
				</div>

				<!-- Section Tabs -->
				<div class="section-tabs">
					{#each sections as section}
						<button
							type="button"
							class="section-tab"
							class:active={activeSection === section.id}
							onclick={() => activeSection = section.id}
						>
							<span class="tab-icon">{section.icon}</span>
							<span class="tab-label">{section.label}</span>
						</button>
					{/each}
					<button
						type="button"
						class="section-tab"
						class:active={activeSection === 'custom'}
						onclick={() => activeSection = 'custom'}
					>
						<span class="tab-icon">[C]</span>
						<span class="tab-label">Custom</span>
						{#if customProperties.length > 0}
							<span class="tab-badge">{customProperties.length}</span>
						{/if}
					</button>
				</div>

				<!-- Section Content -->
				<div class="config-panel">
					{#each sections as section}
						{@const visibleKeys = searchActive
							? section.properties.filter((key) => matchesSearch(key, normalizedQuery))
							: section.properties}
						{#if (searchActive && visibleKeys.length > 0) || (!searchActive && activeSection === section.id)}
							<div class="section-header">
								<div class="section-title">
									<h3>{section.icon} {section.label}</h3>
									<p>{section.description}</p>
								</div>
								<div class="section-actions">
									<button
										type="button"
										class="btn-section-reset"
										disabled={!hasSectionChanges(section.properties)}
										onclick={() => resetSection(section.label, section.properties)}
									>
										Reset section
									</button>
								</div>
							</div>
							<div class="properties-grid">
								{#each visibleKeys as key}
									{@const def = propertyDefinitions[key]}
									{@const value = getValue(key)}
									{#if def}
										<div class="property-card" class:dirty={isKeyDirty(key)}>
											<div class="property-header">
												<div class="property-info">
													<label class="property-label" for={key}>{def.label}</label>
													{#if def.description}
														<span class="property-hint">{def.description}</span>
													{/if}
												</div>
												<div class="property-meta">
													{#if isKeyDirty(key) && isRestartKey(key)}
														<span class="restart-badge">Restart required</span>
													{/if}
													<button
														type="button"
														class="btn-reset"
														disabled={!isKeyDirty(key)}
														onclick={() => resetProperty(key, def.label)}
													>
														Reset
													</button>
												</div>
											</div>
											<div class="property-control">
												{#if def.type === 'boolean'}
													<button
														type="button"
														class="toggle-btn"
														class:active={value === 'true'}
														onclick={() => toggleBoolean(key)}
													>
														<span class="toggle-track">
															<span class="toggle-thumb"></span>
														</span>
														<span class="toggle-label">{value === 'true' ? 'Enabled' : 'Disabled'}</span>
													</button>
												{:else if def.type === 'enum'}
													<select
														id={key}
														class="select-input"
														value={getEnumDisplayValue(key, value)}
														onchange={(e) => setValue(key, e.currentTarget.value)}
													>
														{#each def.options || [] as opt}
															<option value={opt.value}>{opt.label}</option>
														{/each}
													</select>
												{:else if def.type === 'number'}
													<input
														type="number"
														id={key}
														class="number-input"
														value={value}
														min={def.min}
														max={def.max}
														oninput={(e) => setValue(key, e.currentTarget.value)}
													/>
													{#if def.min !== undefined && def.max !== undefined}
														<span class="range-hint">{def.min} - {def.max}</span>
													{/if}
												{:else}
													<input
														type="text"
														id={key}
														class="text-input"
														value={value}
														oninput={(e) => setValue(key, e.currentTarget.value)}
														placeholder={def.default || ''}
													/>
												{/if}
											</div>
										</div>
									{/if}
								{/each}
							</div>
						{/if}
					{/each}

					{#if (searchActive && matchingCustomKeys.length > 0) || (!searchActive && activeSection === 'custom')}
						<div class="section-header">
							<div class="section-title">
								<h3>[C] Custom Properties</h3>
								<p>Additional properties not in standard categories</p>
							</div>
							<div class="section-actions">
								<button type="button" class="btn-add" onclick={() => showAddModal = true}>
									+ Add Property
								</button>
								<button
									type="button"
									class="btn-section-reset danger"
									disabled={customProperties.length === 0}
									onclick={resetCustomProperties}
								>
									Clear custom
								</button>
							</div>
						</div>
						{#if (!searchActive && customProperties.length === 0) || (searchActive && matchingCustomKeys.length === 0)}
							<div class="empty-custom">
								<p>No custom properties defined</p>
								<p class="hint">Add custom properties for mods or advanced configurations</p>
							</div>
						{:else}
							<div class="custom-list">
								{#each (searchActive ? matchingCustomKeys : customProperties) as key}
									<div class="custom-row" class:dirty={isKeyDirty(key)}>
										<div class="custom-key">
											<span>{key}</span>
											{#if isKeyDirty(key) && isRestartKey(key)}
												<span class="restart-badge mini">Restart required</span>
											{/if}
										</div>
										<input
											type="text"
											class="custom-value"
											value={properties[key] || ''}
											oninput={(e) => setValue(key, e.currentTarget.value)}
										/>
										<button
											type="button"
											class="btn-remove"
											onclick={() => removeProperty(key)}
											title="Remove property"
										>
											x
										</button>
									</div>
								{/each}
							</div>
						{/if}
					{/if}

					{#if searchActive && !searchHasMatches}
						<div class="empty-search">
							<p>No settings match "{searchQuery}".</p>
							<button type="button" class="btn-secondary" onclick={() => searchQuery = ''}>
								Clear search
							</button>
						</div>
					{/if}
				</div>

				{#if form?.error}
					<div class="message error">{form.error}</div>
				{/if}

				{#if form?.success}
					<div class="message success">Configuration saved successfully!</div>
				{/if}

				{#if hasChanges}
					<div class="save-bar">
						<div class="save-summary">
							<span class="save-title">Unsaved changes</span>
							<span class="save-hint">Most settings apply after a restart</span>
							{#if hasRestartChanges}
								<span class="restart-badge">Restart required</span>
							{/if}
						</div>
						<div class="save-actions">
							<button type="button" class="btn-ghost" onclick={discardChanges} disabled={loading}>
								Discard
							</button>
							<button type="submit" class="btn-primary" disabled={loading}>
								{loading ? 'Saving...' : 'Save changes'}
							</button>
							{#if hasRestartChanges}
								<button
									type="button"
									class="btn-restart"
									onclick={restartServer}
									disabled={restartLoading || loading}
								>
									{restartLoading ? 'Restarting...' : 'Restart server'}
								</button>
							{/if}
						</div>
					</div>
				{/if}
			</form>
		{/if}
	</div>
{/key}

{#if showAddModal}
	<!-- svelte-ignore a11y_no_static_element_interactions, a11y_click_events_have_key_events -->
	<div class="modal-overlay" onclick={() => showAddModal = false} role="presentation">
		<!-- svelte-ignore a11y_no_static_element_interactions, a11y_click_events_have_key_events -->
		<div class="modal" onclick={(e) => e.stopPropagation()} role="dialog" aria-modal="true">
			<h3>Add Custom Property</h3>
			<div class="form-field">
				<label for="prop-key">Property Name</label>
				<!-- svelte-ignore a11y_autofocus -->
				<input
					type="text"
					id="prop-key"
					bind:value={newKey}
					placeholder="e.g., my-custom-property"
					autofocus
				/>
			</div>
			<div class="form-field">
				<label for="prop-value">Value</label>
				<input type="text" id="prop-value" bind:value={newValue} placeholder="Value" />
			</div>
			<div class="modal-actions">
				<button type="button" class="btn-secondary" onclick={() => showAddModal = false}>
					Cancel
				</button>
				<button type="button" class="btn-primary" onclick={addProperty}>Add Property</button>
			</div>
		</div>
	</div>
{/if}

<style>
	.page {
		display: flex;
		flex-direction: column;
		gap: 24px;
	}

	.page-header {
		display: flex;
		justify-content: space-between;
		align-items: center;
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

	.config-toolbar {
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 16px;
		margin-top: 4px;
	}

	.search-field {
		position: relative;
		flex: 1;
		display: flex;
		align-items: center;
		gap: 8px;
		background: #0f1118;
		border: 1px solid #2a2f47;
		border-radius: 10px;
		padding: 10px 12px;
	}

	.search-input {
		flex: 1;
		background: transparent;
		border: none;
		color: #eef0f8;
		font-size: 14px;
		font-family: inherit;
		outline: none;
	}

	.btn-clear {
		background: rgba(88, 101, 242, 0.15);
		color: #a5b4fc;
		border: none;
		border-radius: 8px;
		padding: 6px 10px;
		font-size: 12px;
		cursor: pointer;
		transition: background 0.2s;
	}

	.btn-clear:hover {
		background: rgba(88, 101, 242, 0.3);
	}

	.dirty-pill {
		background: rgba(255, 179, 71, 0.15);
		color: #ffc48a;
		border: 1px solid rgba(255, 179, 71, 0.3);
		padding: 6px 10px;
		border-radius: 999px;
		font-size: 12px;
		font-weight: 600;
		white-space: nowrap;
	}

	.error-box {
		background: rgba(255, 92, 92, 0.1);
		border: 1px solid rgba(255, 92, 92, 0.3);
		border-radius: 12px;
		padding: 16px 20px;
		color: #ff9f9f;
	}

	.error-box p {
		margin: 0;
	}

	/* Section Tabs */
	.section-tabs {
		display: flex;
		gap: 8px;
		padding: 4px;
		background: #141827;
		border-radius: 12px;
		overflow-x: auto;
		-webkit-overflow-scrolling: touch;
		scroll-snap-type: x proximity;
	}

	.section-tab {
		display: flex;
		align-items: center;
		gap: 8px;
		padding: 12px 18px;
		background: transparent;
		border: none;
		border-radius: 8px;
		color: #9aa2c5;
		font-family: inherit;
		font-size: 14px;
		font-weight: 500;
		cursor: pointer;
		transition: all 0.2s;
		white-space: nowrap;
		scroll-snap-align: start;
	}

	.section-tab:hover {
		background: rgba(88, 101, 242, 0.1);
		color: #d4d9f1;
	}

	.section-tab.active {
		background: linear-gradient(135deg, #5865f2 0%, #4752c4 100%);
		color: white;
		box-shadow: 0 4px 12px rgba(88, 101, 242, 0.3);
	}

	.tab-icon {
		font-size: 16px;
	}

	.tab-badge {
		background: rgba(255, 255, 255, 0.2);
		padding: 2px 8px;
		border-radius: 10px;
		font-size: 12px;
	}

	.section-tab.active .tab-badge {
		background: rgba(255, 255, 255, 0.25);
	}

	/* Config Panel */
	.config-panel {
		background: linear-gradient(160deg, rgba(26, 30, 47, 0.95), rgba(17, 20, 34, 0.95));
		border-radius: 16px;
		padding: 28px;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.35);
		border: 1px solid rgba(88, 101, 242, 0.1);
	}

	.section-header {
		margin-bottom: 24px;
		padding-bottom: 16px;
		border-bottom: 1px solid rgba(255, 255, 255, 0.06);
		display: flex;
		align-items: flex-start;
		justify-content: space-between;
		gap: 16px;
	}

	.section-title {
		flex: 1;
	}

	.section-header h3 {
		margin: 0 0 6px;
		font-size: 18px;
		font-weight: 600;
		color: #eef0f8;
	}

	.section-header p {
		margin: 0;
		font-size: 14px;
		color: #8890b1;
	}

	.section-actions {
		display: flex;
		align-items: center;
		gap: 10px;
	}

	.btn-add {
		margin-top: 0;
		background: rgba(88, 101, 242, 0.15);
		color: #a5b4fc;
		border: 1px dashed rgba(88, 101, 242, 0.4);
		border-radius: 8px;
		padding: 8px 16px;
		font-family: inherit;
		font-size: 13px;
		font-weight: 500;
		cursor: pointer;
		transition: all 0.2s;
		white-space: nowrap;
	}

	.btn-add:hover {
		background: rgba(88, 101, 242, 0.25);
		border-style: solid;
	}

	.btn-section-reset {
		background: rgba(99, 108, 146, 0.2);
		color: #c1c7df;
		border: 1px solid rgba(99, 108, 146, 0.4);
		border-radius: 8px;
		padding: 8px 14px;
		font-size: 12px;
		font-weight: 600;
		cursor: pointer;
		transition: all 0.2s;
		white-space: nowrap;
	}

	.btn-section-reset:hover:not(:disabled) {
		background: rgba(99, 108, 146, 0.35);
	}

	.btn-section-reset:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.btn-section-reset.danger {
		background: rgba(255, 92, 92, 0.12);
		color: #ffb4b4;
		border-color: rgba(255, 92, 92, 0.35);
	}

	.btn-section-reset.danger:hover:not(:disabled) {
		background: rgba(255, 92, 92, 0.2);
	}

	/* Properties Grid */
	.properties-grid {
		display: grid;
		grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
		gap: 16px;
	}

	.property-card {
		background: rgba(20, 24, 39, 0.6);
		border-radius: 12px;
		padding: 16px;
		border: 1px solid rgba(42, 47, 71, 0.6);
		transition: border-color 0.2s;
	}

	.property-card:hover {
		border-color: rgba(88, 101, 242, 0.3);
	}

	.property-header {
		margin-bottom: 12px;
		display: flex;
		align-items: flex-start;
		justify-content: space-between;
		gap: 12px;
	}

	.property-info {
		flex: 1;
	}

	.property-meta {
		display: flex;
		align-items: center;
		gap: 8px;
	}

	.property-label {
		display: block;
		font-size: 14px;
		font-weight: 600;
		color: #eef0f8;
		margin-bottom: 4px;
	}

	.property-hint {
		display: block;
		font-size: 12px;
		color: #6a7192;
		line-height: 1.4;
	}

	.property-card.dirty {
		border-color: rgba(255, 179, 71, 0.35);
		box-shadow: 0 0 0 1px rgba(255, 179, 71, 0.1);
	}

	.restart-badge {
		background: rgba(255, 179, 71, 0.15);
		color: #ffc48a;
		border: 1px solid rgba(255, 179, 71, 0.3);
		padding: 3px 8px;
		border-radius: 999px;
		font-size: 11px;
		font-weight: 600;
		white-space: nowrap;
	}

	.restart-badge.mini {
		font-size: 10px;
		padding: 2px 6px;
	}

	.btn-reset {
		background: rgba(88, 101, 242, 0.15);
		color: #b3baff;
		border: 1px solid rgba(88, 101, 242, 0.3);
		border-radius: 8px;
		padding: 4px 10px;
		font-size: 11px;
		font-weight: 600;
		cursor: pointer;
		transition: all 0.2s;
	}

	.btn-reset:hover:not(:disabled) {
		background: rgba(88, 101, 242, 0.3);
	}

	.btn-reset:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.property-control {
		display: flex;
		align-items: center;
		gap: 10px;
	}

	/* Toggle Button */
	.toggle-btn {
		display: flex;
		align-items: center;
		gap: 12px;
		padding: 8px 12px;
		background: rgba(255, 92, 92, 0.1);
		border: 1px solid rgba(255, 92, 92, 0.2);
		border-radius: 8px;
		cursor: pointer;
		transition: all 0.2s;
		width: 100%;
	}

	.toggle-btn.active {
		background: rgba(106, 176, 76, 0.15);
		border-color: rgba(106, 176, 76, 0.3);
	}

	.toggle-track {
		position: relative;
		width: 44px;
		height: 24px;
		background: rgba(255, 92, 92, 0.3);
		border-radius: 12px;
		transition: background 0.2s;
		flex-shrink: 0;
	}

	.toggle-btn.active .toggle-track {
		background: rgba(106, 176, 76, 0.5);
	}

	.toggle-thumb {
		position: absolute;
		top: 3px;
		left: 3px;
		width: 18px;
		height: 18px;
		background: #ff9f9f;
		border-radius: 50%;
		transition: all 0.2s;
		box-shadow: 0 2px 4px rgba(0, 0, 0, 0.3);
	}

	.toggle-btn.active .toggle-thumb {
		left: 23px;
		background: #7ae68d;
	}

	.toggle-label {
		font-size: 13px;
		font-weight: 500;
		color: #ff9f9f;
	}

	.toggle-btn.active .toggle-label {
		color: #7ae68d;
	}

	/* Select Input */
	.select-input {
		width: 100%;
		padding: 10px 14px;
		background: #0f1118;
		border: 1px solid #2a2f47;
		border-radius: 8px;
		color: #eef0f8;
		font-family: inherit;
		font-size: 14px;
		cursor: pointer;
		appearance: none;
		background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='12' viewBox='0 0 12 12'%3E%3Cpath fill='%239aa2c5' d='M6 8L1 3h10z'/%3E%3C/svg%3E");
		background-repeat: no-repeat;
		background-position: right 12px center;
		padding-right: 36px;
	}

	.select-input:focus {
		outline: none;
		border-color: #5865f2;
	}

	/* Number Input */
	.number-input {
		flex: 1;
		padding: 10px 14px;
		background: #0f1118;
		border: 1px solid #2a2f47;
		border-radius: 8px;
		color: #eef0f8;
		font-family: 'Courier New', monospace;
		font-size: 14px;
	}

	.number-input:focus {
		outline: none;
		border-color: #5865f2;
	}

	.range-hint {
		font-size: 11px;
		color: #6a7192;
		white-space: nowrap;
	}

	/* Text Input */
	.text-input {
		width: 100%;
		padding: 10px 14px;
		background: #0f1118;
		border: 1px solid #2a2f47;
		border-radius: 8px;
		color: #eef0f8;
		font-family: inherit;
		font-size: 14px;
	}

	.text-input:focus {
		outline: none;
		border-color: #5865f2;
	}

	/* Custom Properties */
	.empty-custom {
		text-align: center;
		padding: 48px 24px;
		color: #6a7192;
	}

	.empty-custom p {
		margin: 0 0 8px;
	}

	.empty-custom .hint {
		font-size: 13px;
		color: #515873;
	}

	.empty-search {
		text-align: center;
		padding: 32px 20px;
		color: #6a7192;
		display: flex;
		flex-direction: column;
		gap: 12px;
		align-items: center;
	}

	.custom-list {
		display: flex;
		flex-direction: column;
		gap: 10px;
	}

	.custom-row {
		display: grid;
		grid-template-columns: 200px 1fr auto;
		gap: 12px;
		align-items: center;
		padding: 12px 16px;
		background: rgba(20, 24, 39, 0.6);
		border-radius: 8px;
		border: 1px solid rgba(42, 47, 71, 0.6);
	}

	.custom-row.dirty {
		border-color: rgba(255, 179, 71, 0.35);
		box-shadow: 0 0 0 1px rgba(255, 179, 71, 0.08);
	}

	.custom-key {
		display: flex;
		align-items: center;
		gap: 8px;
		font-weight: 500;
		font-size: 13px;
		color: #9aa2c5;
		font-family: 'Courier New', monospace;
		flex-wrap: wrap;
	}

	.custom-value {
		padding: 8px 12px;
		background: #0f1118;
		border: 1px solid #2a2f47;
		border-radius: 6px;
		color: #eef0f8;
		font-family: 'Courier New', monospace;
		font-size: 13px;
	}

	.custom-value:focus {
		outline: none;
		border-color: #5865f2;
	}

	.btn-remove {
		background: rgba(255, 92, 92, 0.1);
		color: #ff9f9f;
		border: none;
		border-radius: 6px;
		width: 32px;
		height: 32px;
		font-size: 20px;
		cursor: pointer;
		transition: background 0.2s;
		display: flex;
		align-items: center;
		justify-content: center;
	}

	.btn-remove:hover {
		background: rgba(255, 92, 92, 0.25);
	}

	/* Messages */
	.message {
		padding: 12px 16px;
		border-radius: 8px;
		font-size: 14px;
		font-weight: 500;
	}

	.message.error {
		background: rgba(255, 92, 92, 0.1);
		color: #ff9f9f;
		border: 1px solid rgba(255, 92, 92, 0.2);
	}

	.message.success {
		background: rgba(106, 176, 76, 0.1);
		color: #7ae68d;
		border: 1px solid rgba(106, 176, 76, 0.2);
	}

	/* Save Bar */
	.save-bar {
		position: sticky;
		bottom: 16px;
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 16px;
		padding: 16px 20px;
		margin-top: 12px;
		background: rgba(20, 24, 39, 0.92);
		border: 1px solid rgba(88, 101, 242, 0.2);
		border-radius: 14px;
		box-shadow: 0 18px 30px rgba(0, 0, 0, 0.35);
		backdrop-filter: blur(6px);
		z-index: 10;
	}

	.save-summary {
		display: flex;
		align-items: center;
		gap: 10px;
		flex-wrap: wrap;
	}

	.save-title {
		font-size: 14px;
		font-weight: 600;
		color: #eef0f8;
	}

	.save-hint {
		font-size: 13px;
		color: #6a7192;
	}

	.save-actions {
		display: flex;
		align-items: center;
		gap: 10px;
		flex-wrap: wrap;
		justify-content: flex-end;
	}

	.btn-primary {
		background: linear-gradient(135deg, #5865f2 0%, #4752c4 100%);
		color: white;
		border: none;
		border-radius: 10px;
		padding: 12px 28px;
		font-family: inherit;
		font-size: 15px;
		font-weight: 600;
		cursor: pointer;
		transition: all 0.2s;
		box-shadow: 0 4px 12px rgba(88, 101, 242, 0.25);
	}

	.btn-primary:hover:not(:disabled) {
		transform: translateY(-1px);
		box-shadow: 0 6px 16px rgba(88, 101, 242, 0.35);
	}

	.btn-primary:disabled {
		opacity: 0.6;
		cursor: not-allowed;
		transform: none;
	}

	.btn-ghost {
		background: rgba(99, 108, 146, 0.2);
		color: #c1c7df;
		border: 1px solid rgba(99, 108, 146, 0.4);
		border-radius: 10px;
		padding: 12px 22px;
		font-family: inherit;
		font-size: 14px;
		font-weight: 600;
		cursor: pointer;
		transition: background 0.2s;
	}

	.btn-ghost:hover:not(:disabled) {
		background: rgba(99, 108, 146, 0.35);
	}

	.btn-ghost:disabled {
		opacity: 0.6;
		cursor: not-allowed;
	}

	.btn-restart {
		background: rgba(255, 179, 71, 0.2);
		color: #ffc48a;
		border: 1px solid rgba(255, 179, 71, 0.35);
		border-radius: 10px;
		padding: 12px 22px;
		font-family: inherit;
		font-size: 14px;
		font-weight: 600;
		cursor: pointer;
		transition: background 0.2s;
	}

	.btn-restart:hover:not(:disabled) {
		background: rgba(255, 179, 71, 0.32);
	}

	.btn-restart:disabled {
		opacity: 0.6;
		cursor: not-allowed;
	}

	/* Modal */
	.modal-overlay {
		position: fixed;
		top: 0;
		left: 0;
		right: 0;
		bottom: 0;
		background: rgba(0, 0, 0, 0.75);
		display: flex;
		align-items: center;
		justify-content: center;
		z-index: 1000;
		padding: 24px;
		backdrop-filter: blur(4px);
	}

	.modal {
		background: linear-gradient(160deg, #1e2235 0%, #151827 100%);
		border-radius: 20px;
		padding: 32px;
		max-width: 420px;
		width: 100%;
		box-shadow: 0 24px 60px rgba(0, 0, 0, 0.5);
		border: 1px solid rgba(88, 101, 242, 0.15);
	}

	.modal h3 {
		margin: 0 0 24px;
		font-size: 20px;
		font-weight: 600;
	}

	.form-field {
		margin-bottom: 18px;
	}

	.form-field label {
		display: block;
		margin-bottom: 8px;
		font-size: 14px;
		font-weight: 500;
		color: #9aa2c5;
	}

	.form-field input {
		width: 100%;
		padding: 12px 16px;
		background: #0f1118;
		border: 1px solid #2a2f47;
		border-radius: 10px;
		color: #eef0f8;
		font-family: inherit;
		font-size: 14px;
		box-sizing: border-box;
		transition: border-color 0.2s;
	}

	.form-field input:focus {
		outline: none;
		border-color: #5865f2;
	}

	.modal-actions {
		display: flex;
		gap: 12px;
		justify-content: flex-end;
		margin-top: 24px;
	}

	.btn-secondary {
		background: #2b2f45;
		color: #d4d9f1;
		border: none;
		border-radius: 10px;
		padding: 12px 24px;
		font-family: inherit;
		font-size: 14px;
		font-weight: 500;
		cursor: pointer;
		transition: background 0.2s;
	}

	.btn-secondary:hover {
		background: #3a3f5a;
	}

	/* Responsive */
	@media (max-width: 768px) {
		.section-tabs {
			padding: 4px;
		}

		.section-tab {
			padding: 10px 14px;
			font-size: 13px;
		}

		.tab-label {
			display: none;
		}

		.section-tab.active .tab-label {
			display: inline;
		}

		.properties-grid {
			grid-template-columns: 1fr;
		}

		.config-toolbar {
			flex-direction: column;
			align-items: stretch;
		}

		.section-header {
			flex-direction: column;
			align-items: flex-start;
		}

		.section-actions {
			width: 100%;
			justify-content: flex-start;
			flex-wrap: wrap;
		}

		.property-header {
			flex-direction: column;
			align-items: flex-start;
		}

		.property-meta {
			width: 100%;
			justify-content: flex-start;
		}

		.custom-row {
			grid-template-columns: 1fr;
			gap: 8px;
		}

		.save-bar {
			flex-direction: column;
			align-items: stretch;
		}

		.save-actions {
			width: 100%;
			justify-content: stretch;
		}

		.save-actions button {
			width: 100%;
		}
	}
</style>

