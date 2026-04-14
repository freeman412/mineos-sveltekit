<script lang="ts">
	import { invalidateAll } from '$app/navigation';
	import { modal } from '$lib/stores/modal';
	import { sheepEnabled } from '$lib/stores/uiPreferences';
	import type { PageData } from './$types';

	interface SettingInfo {
		key: string;
		value: string | null;
		description: string;
		isSecret: boolean;
		hasValue: boolean;
		source: string;
		type: string;
		group: string;
		displayName: string | null;
		options: string | null;
		min: number | null;
		max: number | null;
		comingSoon: boolean;
	}

	let { data }: { data: PageData } = $props();

	let editingKey = $state<string | null>(null);
	let editValue = $state('');
	let saving = $state(false);
	let numberValues = $state<Record<string, number>>({});

	// Initialize number values when settings data changes
	$effect(() => {
		const settings = (data.settings.data ?? []) as SettingInfo[];
		for (const s of settings) {
			if (s.type === 'number' && s.hasValue && s.value != null && !(s.key in numberValues)) {
				numberValues[s.key] = parseInt(s.value) || 0;
			}
		}
	});

	// Group settings by their group field
	let groupedSettings = $derived.by(() => {
		const settings = (data.settings.data ?? []) as SettingInfo[];
		const groups: Record<string, SettingInfo[]> = {};
		const groupOrder = ['General', 'Integrations', 'Notifications', 'Advanced'];

		for (const s of settings) {
			const group = s.group || 'General';
			if (!groups[group]) groups[group] = [];
			groups[group].push(s);
		}

		return groupOrder
			.filter((g) => groups[g]?.length)
			.map((g) => ({ name: g, settings: groups[g] }));
	});

	function displayName(setting: SettingInfo): string {
		return setting.displayName || setting.key;
	}

	function startEdit(setting: SettingInfo) {
		editingKey = setting.key;
		editValue = '';
	}

	function cancelEdit() {
		editingKey = null;
		editValue = '';
	}

	async function saveSetting(key: string, value?: string) {
		saving = true;
		try {
			const val = value !== undefined ? value : editValue || null;
			const res = await fetch(`/api/settings/${encodeURIComponent(key)}`, {
				method: 'PUT',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify({ value: val })
			});

			if (!res.ok) {
				const error = await res.json().catch(() => ({ error: 'Failed to save setting' }));
				await modal.error(error.error || 'Failed to save setting');
			} else {
				cancelEdit();
				await invalidateAll();
			}
		} finally {
			saving = false;
		}
	}

	async function clearSetting(key: string) {
		const confirmed = await modal.confirm(
			'Are you sure you want to clear this setting? It will fall back to the configuration file value if one exists.',
			'Clear Setting'
		);
		if (!confirmed) return;

		saving = true;
		try {
			const res = await fetch(`/api/settings/${encodeURIComponent(key)}`, {
				method: 'PUT',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify({ value: null })
			});

			if (!res.ok) {
				const error = await res.json().catch(() => ({ error: 'Failed to clear setting' }));
				await modal.error(error.error || 'Failed to clear setting');
			} else {
				await invalidateAll();
			}
		} finally {
			saving = false;
		}
	}

	async function toggleBoolean(setting: SettingInfo) {
		const current = setting.value?.toLowerCase() === 'true';
		await saveSetting(setting.key, current ? 'false' : 'true');
	}

	async function saveSelect(setting: SettingInfo, value: string) {
		await saveSetting(setting.key, value);
	}

	async function saveNumber(setting: SettingInfo) {
		const val = numberValues[setting.key];
		if (val !== undefined) {
			await saveSetting(setting.key, String(val));
		}
	}

	function getSourceBadgeClass(source: string): string {
		switch (source) {
			case 'database':
				return 'source-database';
			case 'configuration':
				return 'source-config';
			default:
				return 'source-not-set';
		}
	}

	function getGroupIcon(group: string): string {
		switch (group) {
			case 'General':
				return '⚙';
			case 'Integrations':
				return '🔌';
			case 'Notifications':
				return '🔔';
			case 'Advanced':
				return '🔧';
			default:
				return '📋';
		}
	}

	function parseOptions(options: string | null): string[] {
		if (!options) return [];
		try {
			return JSON.parse(options);
		} catch {
			return [];
		}
	}
</script>

<div class="page-header">
	<div>
		<h1>Settings</h1>
		<p class="subtitle">Configure system settings and integrations</p>
	</div>
</div>

<!-- UI Preferences (local/browser) -->
<div class="settings-group">
	<div class="group-header">
		<span class="group-icon">🎨</span>
		<h2>UI Preferences</h2>
		<span class="source-badge source-config">local</span>
	</div>
	<div class="setting-card">
		<div class="preference-row">
			<div>
				<div class="preference-title">Minecraft Sheep Pet</div>
				<div class="preference-help">Show the sheep companion on every page.</div>
			</div>
			<label class="toggle">
				<input type="checkbox" bind:checked={$sheepEnabled} aria-label="Toggle sheep pet" />
				<span class="toggle-slider"></span>
			</label>
		</div>
	</div>
</div>

{#if data.settings.error}
	<div class="error-box">
		<p>{data.settings.error}</p>
	</div>
{:else}
	{#each groupedSettings as group (group.name)}
		<div class="settings-group">
			<div class="group-header">
				<span class="group-icon">{getGroupIcon(group.name)}</span>
				<h2>{group.name}</h2>
			</div>

			{#each group.settings as setting (setting.key)}
				<div class="setting-card" class:coming-soon={setting.comingSoon}>
					<!-- Coming Soon -->
					{#if setting.comingSoon}
						<div class="preference-row">
							<div class="setting-info">
								<div class="setting-title-row">
									<div class="preference-title">{displayName(setting)}</div>
									<span class="coming-soon-badge">Coming Soon</span>
								</div>
								<div class="preference-help">{setting.description}</div>
							</div>
						</div>

					<!-- Boolean toggle -->
					{:else if setting.type === 'boolean'}
						<div class="preference-row">
							<div class="setting-info">
								<div class="setting-title-row">
									<div class="preference-title">{displayName(setting)}</div>
									<span class="source-badge {getSourceBadgeClass(setting.source)}"
										>{setting.source}</span
									>
								</div>
								<div class="preference-help">{setting.description}</div>
							</div>
							<label class="toggle">
								<input
									type="checkbox"
									checked={setting.value?.toLowerCase() === 'true'}
									onchange={() => toggleBoolean(setting)}
									disabled={saving}
									aria-label={displayName(setting)}
								/>
								<span class="toggle-slider"></span>
							</label>
						</div>

						<!-- Number input with range -->
					{:else if setting.type === 'number'}
						<div class="setting-info">
							<div class="setting-title-row">
								<div class="preference-title">{displayName(setting)}</div>
								<span class="source-badge {getSourceBadgeClass(setting.source)}"
									>{setting.source}</span
								>
							</div>
							<div class="preference-help">{setting.description}</div>
						</div>
						<div class="number-control">
							<input
								type="range"
								min={setting.min ?? 0}
								max={setting.max ?? 900}
								bind:value={numberValues[setting.key]}
								oninput={() => {
									/* reactive binding handles it */
								}}
								aria-label={displayName(setting)}
							/>
							<div class="number-input-group">
								<input
									type="number"
									min={setting.min ?? 0}
									max={setting.max ?? 900}
									bind:value={numberValues[setting.key]}
									onblur={() => saveNumber(setting)}
									onkeydown={(e) => {
										if (e.key === 'Enter') saveNumber(setting);
									}}
								/>
								<span class="number-unit">sec</span>
							</div>
							<button
								class="btn-sm"
								onclick={() => saveNumber(setting)}
								disabled={saving}
							>
								{saving ? '...' : 'Save'}
							</button>
						</div>
						{#if setting.source === 'database'}
							<button
								class="btn-clear"
								onclick={() => clearSetting(setting.key)}
								title="Clear database value and fall back to config"
							>
								Reset to default
							</button>
						{/if}

						<!-- Select dropdown -->
					{:else if setting.type === 'select'}
						<div class="setting-info">
							<div class="setting-title-row">
								<div class="preference-title">{displayName(setting)}</div>
								<span class="source-badge {getSourceBadgeClass(setting.source)}"
									>{setting.source}</span
								>
							</div>
							<div class="preference-help">{setting.description}</div>
						</div>
						<div class="select-control">
							<select
								value={setting.value || ''}
								onchange={(e) => saveSelect(setting, e.currentTarget.value)}
								disabled={saving}
								aria-label={displayName(setting)}
							>
								{#if !setting.hasValue}
									<option value="" disabled>Select...</option>
								{/if}
								{#each parseOptions(setting.options) as opt (opt)}
									<option value={opt}>{opt}</option>
								{/each}
							</select>
							{#if setting.source === 'database'}
								<button
									class="btn-clear"
									onclick={() => clearSetting(setting.key)}
									title="Clear database value and fall back to config"
								>
									Reset
								</button>
							{/if}
						</div>

						<!-- Secret input -->
					{:else if setting.type === 'secret'}
						{#if editingKey === setting.key}
							<div class="setting-info">
								<div class="setting-title-row">
									<div class="preference-title">{displayName(setting)}</div>
									<span class="edit-badge">Editing</span>
								</div>
								<div class="preference-help">{setting.description}</div>
							</div>
							<label class="input-label">
								<span class="label-text">New value (will be stored securely)</span>
								<input type="password" bind:value={editValue} placeholder="Enter new value" />
							</label>
							<div class="edit-actions">
								<button
									class="btn-primary"
									onclick={() => saveSetting(setting.key)}
									disabled={saving || !editValue.trim()}
								>
									{saving ? 'Saving...' : 'Save'}
								</button>
								<button class="btn-secondary" onclick={cancelEdit} disabled={saving}>
									Cancel
								</button>
							</div>
						{:else}
							<div class="preference-row">
								<div class="setting-info">
									<div class="setting-title-row">
										<div class="preference-title">{displayName(setting)}</div>
										<span class="source-badge {getSourceBadgeClass(setting.source)}"
											>{setting.source}</span
										>
									</div>
									<div class="preference-help">{setting.description}</div>
									{#if setting.hasValue}
										<code class="setting-value-code">{setting.value}</code>
									{:else}
										<span class="not-configured">Not configured</span>
									{/if}
								</div>
								<div class="setting-actions-vertical">
									<button class="btn-primary" onclick={() => startEdit(setting)}>
										{setting.hasValue ? 'Update' : 'Configure'}
									</button>
									{#if setting.source === 'database'}
										<button
											class="btn-clear"
											onclick={() => clearSetting(setting.key)}
										>
											Clear
										</button>
									{/if}
								</div>
							</div>
						{/if}

						<!-- Text input (default) -->
					{:else}
						{#if editingKey === setting.key}
							<div class="setting-info">
								<div class="setting-title-row">
									<div class="preference-title">{displayName(setting)}</div>
									<span class="edit-badge">Editing</span>
								</div>
								<div class="preference-help">{setting.description}</div>
							</div>
							<label class="input-label">
								<span class="label-text">New value</span>
								<input type="text" bind:value={editValue} placeholder="Enter value" />
							</label>
							<div class="edit-actions">
								<button
									class="btn-primary"
									onclick={() => saveSetting(setting.key)}
									disabled={saving || !editValue.trim()}
								>
									{saving ? 'Saving...' : 'Save'}
								</button>
								<button class="btn-secondary" onclick={cancelEdit} disabled={saving}>
									Cancel
								</button>
							</div>
						{:else}
							<div class="preference-row">
								<div class="setting-info">
									<div class="setting-title-row">
										<div class="preference-title">{displayName(setting)}</div>
										<span class="source-badge {getSourceBadgeClass(setting.source)}"
											>{setting.source}</span
										>
									</div>
									<div class="preference-help">{setting.description}</div>
									{#if setting.hasValue}
										<code class="setting-value-code">{setting.value}</code>
									{:else}
										<span class="not-configured">Not configured</span>
									{/if}
								</div>
								<div class="setting-actions-vertical">
									<button class="btn-primary" onclick={() => startEdit(setting)}>
										{setting.hasValue ? 'Update' : 'Configure'}
									</button>
									{#if setting.source === 'database'}
										<button
											class="btn-clear"
											onclick={() => clearSetting(setting.key)}
										>
											Clear
										</button>
									{/if}
								</div>
							</div>
						{/if}
					{/if}
				</div>
			{/each}
		</div>
	{/each}
{/if}

{#if data.meta}
	<div class="settings-group">
		<div class="group-header">
			<span class="group-icon">ℹ</span>
			<h2>System</h2>
		</div>
		<div class="setting-card">
			<div class="preference-row">
				<div class="setting-info">
					<div class="preference-title">Installation ID</div>
					<div class="preference-help">Unique identifier for this MineOS installation. Used for telemetry and support.</div>
					{#if data.meta.installationId}
						<code class="setting-value-code installation-id">{data.meta.installationId}</code>
					{:else}
						<span class="not-configured">Not set</span>
					{/if}
				</div>
				{#if data.meta.installationId}
					<button
						class="btn-sm"
						onclick={() => navigator.clipboard.writeText(data.meta.installationId)}
						title="Copy to clipboard"
					>
						Copy
					</button>
				{/if}
			</div>
		</div>
	</div>
{/if}

<div class="info-section">
	<div class="info-card">
		<h3>Setting Sources</h3>
		<ul>
			<li>
				<span class="source-badge source-database">database</span>
				Value stored in the database (takes priority)
			</li>
			<li>
				<span class="source-badge source-config">configuration</span>
				Value from environment variables or config file
			</li>
			<li>
				<span class="source-badge source-not-set">not set</span>
				No value configured
			</li>
		</ul>
	</div>

	<div class="info-card">
		<h3>Getting a CurseForge API Key</h3>
		<ol>
			<li>
				Visit <a href="https://console.curseforge.com/" target="_blank" rel="noopener noreferrer"
					>console.curseforge.com</a
				>
			</li>
			<li>Sign in or create an account</li>
			<li>Go to "API Keys" in your account settings</li>
			<li>Create a new API key</li>
			<li>Copy the key and paste it above</li>
		</ol>
		<p class="note">
			The CurseForge API key is required for searching and downloading mods and modpacks. The site
			works without it for vanilla server management.
		</p>
	</div>
</div>

<style>
	.page-header {
		margin-bottom: 32px;
	}

	h1 {
		margin: 0 0 8px;
		font-size: 32px;
		font-weight: 600;
	}

	.subtitle {
		margin: 0;
		color: #aab2d3;
		font-size: 15px;
	}

	/* Group layout */
	.settings-group {
		margin-bottom: 28px;
	}

	.group-header {
		display: flex;
		align-items: center;
		gap: 10px;
		margin-bottom: 12px;
		padding: 0 4px;
	}

	.group-header h2 {
		margin: 0;
		font-size: 16px;
		font-weight: 600;
		color: #9aa2c5;
		text-transform: uppercase;
		letter-spacing: 0.05em;
	}

	.group-icon {
		font-size: 16px;
	}

	/* Cards */
	.setting-card {
		background: linear-gradient(135deg, #1a1e2f 0%, #141827 100%);
		border-radius: 12px;
		padding: 20px 24px;
		border: 1px solid #2a2f47;
		margin-bottom: 8px;
	}

	.setting-info {
		flex: 1;
		min-width: 0;
	}

	.setting-title-row {
		display: flex;
		align-items: center;
		gap: 10px;
		margin-bottom: 4px;
	}

	/* Badges */
	.source-badge {
		display: inline-block;
		padding: 2px 8px;
		border-radius: 20px;
		font-size: 11px;
		font-weight: 600;
		text-transform: uppercase;
		flex-shrink: 0;
	}

	.source-database {
		background: rgba(106, 176, 76, 0.15);
		color: #b7f5a2;
		border: 1px solid rgba(106, 176, 76, 0.3);
	}

	.source-config {
		background: rgba(88, 101, 242, 0.15);
		color: #a5b4fc;
		border: 1px solid rgba(88, 101, 242, 0.3);
	}

	.source-not-set {
		background: rgba(255, 193, 7, 0.15);
		color: #ffd54f;
		border: 1px solid rgba(255, 193, 7, 0.3);
	}

	.edit-badge {
		background: rgba(88, 101, 242, 0.2);
		color: #a5b4fc;
		padding: 2px 8px;
		border-radius: 6px;
		font-size: 11px;
		font-weight: 500;
	}

	.coming-soon-badge {
		display: inline-block;
		padding: 2px 8px;
		border-radius: 20px;
		font-size: 11px;
		font-weight: 600;
		text-transform: uppercase;
		background: rgba(255, 193, 7, 0.15);
		color: #ffd54f;
		border: 1px solid rgba(255, 193, 7, 0.3);
		flex-shrink: 0;
	}

	.setting-card.coming-soon {
		opacity: 0.6;
	}

	/* Preference rows */
	.preference-row {
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 16px;
	}

	.preference-title {
		font-size: 15px;
		font-weight: 600;
		color: #eef0f8;
	}

	.preference-help {
		margin-top: 2px;
		color: #9aa2c5;
		font-size: 13px;
		line-height: 1.4;
	}

	/* Toggle switch */
	.toggle {
		position: relative;
		display: inline-flex;
		align-items: center;
		flex-shrink: 0;
	}

	.toggle input {
		position: absolute;
		opacity: 0;
		width: 1px;
		height: 1px;
	}

	.toggle-slider {
		width: 48px;
		height: 26px;
		border-radius: 999px;
		background: #2a2f47;
		border: 1px solid #3a3f5a;
		position: relative;
		transition: all 0.2s;
		cursor: pointer;
	}

	.toggle-slider::after {
		content: '';
		position: absolute;
		top: 3px;
		left: 3px;
		width: 18px;
		height: 18px;
		border-radius: 50%;
		background: #c9d1f2;
		transition: transform 0.2s, background 0.2s;
	}

	.toggle input:checked + .toggle-slider {
		background: rgba(106, 176, 76, 0.35);
		border-color: rgba(106, 176, 76, 0.7);
	}

	.toggle input:checked + .toggle-slider::after {
		transform: translateX(22px);
		background: #6ab04c;
	}

	.toggle input:disabled + .toggle-slider {
		opacity: 0.5;
		cursor: not-allowed;
	}

	/* Number control */
	.number-control {
		display: flex;
		align-items: center;
		gap: 12px;
		margin-top: 12px;
	}

	.number-control input[type='range'] {
		flex: 1;
		height: 6px;
		-webkit-appearance: none;
		appearance: none;
		background: #2a2f47;
		border-radius: 3px;
		outline: none;
	}

	.number-control input[type='range']::-webkit-slider-thumb {
		-webkit-appearance: none;
		width: 18px;
		height: 18px;
		border-radius: 50%;
		background: #6ab04c;
		cursor: pointer;
		border: 2px solid #141827;
	}

	.number-input-group {
		display: flex;
		align-items: center;
		gap: 4px;
		flex-shrink: 0;
	}

	.number-control input[type='number'] {
		width: 70px;
		background: #141827;
		border: 1px solid #2a2f47;
		border-radius: 6px;
		padding: 6px 8px;
		color: #eef0f8;
		font-family: 'Consolas', 'Monaco', monospace;
		font-size: 14px;
		text-align: center;
	}

	.number-control input[type='number']:focus {
		outline: none;
		border-color: rgba(106, 176, 76, 0.5);
	}

	.number-unit {
		color: #7c87b2;
		font-size: 13px;
	}

	/* Select dropdown */
	.select-control {
		display: flex;
		align-items: center;
		gap: 10px;
		margin-top: 12px;
	}

	select {
		background: #141827;
		border: 1px solid #2a2f47;
		border-radius: 8px;
		padding: 8px 32px 8px 12px;
		color: #eef0f8;
		font-family: inherit;
		font-size: 14px;
		cursor: pointer;
		appearance: none;
		background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='12' viewBox='0 0 12 12'%3E%3Cpath fill='%239aa2c5' d='M6 8L1 3h10z'/%3E%3C/svg%3E");
		background-repeat: no-repeat;
		background-position: right 10px center;
	}

	select:focus {
		outline: none;
		border-color: rgba(106, 176, 76, 0.5);
	}

	select:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	/* Value display */
	.setting-value-code {
		display: inline-block;
		background: rgba(20, 24, 39, 0.8);
		padding: 4px 10px;
		border-radius: 6px;
		font-family: 'Consolas', 'Monaco', monospace;
		font-size: 13px;
		color: #6ab04c;
		border: 1px solid #2a2f47;
		margin-top: 8px;
	}

	.installation-id {
		font-size: 12px;
		letter-spacing: 0.03em;
		word-break: break-all;
		user-select: all;
	}

	.not-configured {
		color: #7c87b2;
		font-style: italic;
		font-size: 13px;
		margin-top: 8px;
		display: inline-block;
	}

	/* Input labels */
	.input-label {
		display: flex;
		flex-direction: column;
		gap: 6px;
		margin: 12px 0 16px;
	}

	.label-text {
		font-size: 13px;
		color: #9aa2c5;
		font-weight: 500;
	}

	input[type='text'],
	input[type='password'] {
		background: #141827;
		border: 1px solid #2a2f47;
		border-radius: 8px;
		padding: 10px 14px;
		color: #eef0f8;
		font-family: inherit;
		font-size: 14px;
		transition: border-color 0.2s;
	}

	input[type='text']:focus,
	input[type='password']:focus {
		outline: none;
		border-color: rgba(106, 176, 76, 0.5);
	}

	/* Actions */
	.setting-actions-vertical {
		display: flex;
		flex-direction: column;
		gap: 6px;
		flex-shrink: 0;
	}

	.edit-actions {
		display: flex;
		gap: 10px;
	}

	/* Buttons */
	.btn-primary {
		background: var(--mc-grass);
		color: #fff;
		border: none;
		border-radius: 8px;
		padding: 8px 16px;
		font-size: 13px;
		font-weight: 600;
		cursor: pointer;
		transition: background 0.2s;
	}

	.btn-primary:hover:not(:disabled) {
		background: var(--mc-grass-dark);
	}

	.btn-primary:disabled {
		opacity: 0.6;
		cursor: not-allowed;
	}

	.btn-secondary {
		background: #2a2f47;
		color: #eef0f8;
		border: none;
		border-radius: 8px;
		padding: 8px 16px;
		font-size: 13px;
		font-weight: 600;
		cursor: pointer;
		transition: background 0.2s;
	}

	.btn-secondary:hover:not(:disabled) {
		background: #3a3f5a;
	}

	.btn-sm {
		background: #2a2f47;
		color: #eef0f8;
		border: none;
		border-radius: 6px;
		padding: 6px 12px;
		font-size: 12px;
		font-weight: 600;
		cursor: pointer;
		transition: background 0.2s;
		flex-shrink: 0;
	}

	.btn-sm:hover:not(:disabled) {
		background: rgba(106, 176, 76, 0.3);
		color: #b7f5a2;
	}

	.btn-sm:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.btn-clear {
		background: none;
		border: none;
		color: #7c87b2;
		font-size: 12px;
		cursor: pointer;
		padding: 4px 0;
		margin-top: 6px;
	}

	.btn-clear:hover {
		color: #ff9f9f;
	}

	/* Error */
	.error-box {
		background: rgba(255, 92, 92, 0.1);
		border: 1px solid rgba(255, 92, 92, 0.3);
		border-radius: 10px;
		padding: 16px;
		margin-bottom: 24px;
	}

	.error-box p {
		margin: 0;
		color: #ff9f9f;
	}

	/* Info section */
	.info-section {
		display: grid;
		grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
		gap: 20px;
		margin-top: 32px;
	}

	.info-card {
		background: rgba(88, 101, 242, 0.08);
		border: 1px solid rgba(88, 101, 242, 0.2);
		border-radius: 12px;
		padding: 20px 24px;
	}

	.info-card h3 {
		margin: 0 0 16px;
		font-size: 16px;
		color: #a5b4fc;
	}

	.info-card ul,
	.info-card ol {
		margin: 0;
		padding-left: 20px;
		color: #9aa2c5;
		font-size: 14px;
		line-height: 2;
	}

	.info-card li {
		display: flex;
		align-items: center;
		gap: 10px;
	}

	.info-card ol li {
		display: list-item;
	}

	.info-card a {
		color: #6ab04c;
		text-decoration: none;
	}

	.info-card a:hover {
		text-decoration: underline;
	}

	.info-card .note {
		margin: 16px 0 0;
		padding: 12px;
		background: rgba(255, 193, 7, 0.1);
		border-radius: 8px;
		color: #ffd54f;
		font-size: 13px;
		line-height: 1.5;
	}

	@media (max-width: 768px) {
		.page-header {
			margin-bottom: 20px;
		}

		h1 {
			font-size: 24px;
		}

		.setting-card {
			padding: 16px;
		}

		.preference-row {
			flex-direction: column;
			align-items: stretch;
			gap: 12px;
		}

		.preference-row .toggle {
			align-self: flex-start;
		}

		.number-control {
			flex-wrap: wrap;
			gap: 10px;
		}

		.number-control input[type='range'] {
			width: 100%;
			flex: none;
		}

		.select-control {
			flex-wrap: wrap;
		}

		.setting-actions-vertical {
			flex-direction: row;
			gap: 8px;
		}

		.edit-actions {
			flex-wrap: wrap;
		}

		.info-section {
			grid-template-columns: 1fr;
		}

		.info-card {
			padding: 16px;
		}

		.info-card li {
			flex-wrap: wrap;
			gap: 6px;
		}
	}

	@media (max-width: 480px) {
		.setting-card {
			padding: 14px;
			border-radius: 10px;
		}

		.setting-title-row {
			flex-wrap: wrap;
			gap: 6px;
		}

		.btn-primary,
		.btn-secondary {
			padding: 10px 16px;
		}
	}
</style>
