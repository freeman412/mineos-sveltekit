<script lang="ts">
	import { invalidateAll } from '$app/navigation';
	import { modal } from '$lib/stores/modal';
	import type { PageData } from './$types';

	interface SettingInfo {
		key: string;
		value: string | null;
		description: string;
		isSecret: boolean;
		hasValue: boolean;
		source: string;
	}

	let { data }: { data: PageData } = $props();

	let editingKey = $state<string | null>(null);
	let editValue = $state('');
	let saving = $state(false);

	function startEdit(setting: SettingInfo) {
		editingKey = setting.key;
		editValue = '';
	}

	function cancelEdit() {
		editingKey = null;
		editValue = '';
	}

	async function saveSetting(key: string) {
		saving = true;
		try {
			const res = await fetch(`/api/settings/${encodeURIComponent(key)}`, {
				method: 'PUT',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify({ value: editValue || null })
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

	function getKeyDisplayName(key: string): string {
		const names: Record<string, string> = {
			'CurseForge:ApiKey': 'CurseForge API Key'
		};
		return names[key] || key;
	}
</script>

<div class="page-header">
	<div>
		<h1>Settings</h1>
		<p class="subtitle">Configure system settings and integrations</p>
	</div>
</div>

{#if data.settings.error}
	<div class="error-box">
		<p>{data.settings.error}</p>
	</div>
{:else if data.settings.data && data.settings.data.length > 0}
	<div class="settings-list">
		{#each data.settings.data as setting (setting.key)}
			<div class="setting-card">
				{#if editingKey === setting.key}
					<div class="setting-edit">
						<div class="setting-header">
							<h3>{getKeyDisplayName(setting.key)}</h3>
							<span class="edit-badge">Editing</span>
						</div>
						<p class="setting-description">{setting.description}</p>

						<label>
							<span class="label-text">{setting.isSecret ? 'New Value (will be encrypted)' : 'New Value'}</span>
							<input
								type={setting.isSecret ? 'password' : 'text'}
								bind:value={editValue}
								placeholder={setting.isSecret ? 'Enter new API key' : 'Enter value'}
							/>
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
					</div>
				{:else}
					<div class="setting-view">
						<div class="setting-header">
							<h3>{getKeyDisplayName(setting.key)}</h3>
							<span class="source-badge {getSourceBadgeClass(setting.source)}">
								{setting.source}
							</span>
						</div>
						<p class="setting-description">{setting.description}</p>

						<div class="setting-value">
							{#if setting.hasValue}
								<code>{setting.value}</code>
							{:else}
								<span class="not-configured">Not configured</span>
							{/if}
						</div>

						<div class="setting-actions">
							<button class="btn-primary" onclick={() => startEdit(setting)}>
								{setting.hasValue ? 'Update' : 'Configure'}
							</button>
							{#if setting.source === 'database'}
								<button
									class="btn-secondary danger"
									onclick={() => clearSetting(setting.key)}
									title="Clear database value and fall back to config"
								>
									Clear
								</button>
							{/if}
						</div>
					</div>
				{/if}
			</div>
		{/each}
	</div>
{:else}
	<div class="empty-state">
		<p>No configurable settings available.</p>
	</div>
{/if}

<div class="info-section">
	<div class="info-card">
		<h3>Setting Sources</h3>
		<ul>
			<li>
				<span class="source-badge source-database">database</span>
				Value is stored in the database and takes priority
			</li>
			<li>
				<span class="source-badge source-config">configuration</span>
				Value is from appsettings.json or environment variables
			</li>
			<li>
				<span class="source-badge source-not-set">not set</span>
				No value configured - feature may not work
			</li>
		</ul>
	</div>

	<div class="info-card">
		<h3>Getting a CurseForge API Key</h3>
		<ol>
			<li>Visit <a href="https://console.curseforge.com/" target="_blank" rel="noopener noreferrer">console.curseforge.com</a></li>
			<li>Sign in or create an account</li>
			<li>Go to "API Keys" in your account settings</li>
			<li>Create a new API key</li>
			<li>Copy the key and paste it above</li>
		</ol>
		<p class="note">
			The CurseForge API key is required for searching and downloading mods and modpacks.
			The site works without it for vanilla server management.
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

	.settings-list {
		display: flex;
		flex-direction: column;
		gap: 20px;
		margin-bottom: 32px;
	}

	.setting-card {
		background: linear-gradient(135deg, #1a1e2f 0%, #141827 100%);
		border-radius: 16px;
		padding: 24px;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.35);
		border: 1px solid #2a2f47;
	}

	.setting-header {
		display: flex;
		align-items: center;
		gap: 12px;
		margin-bottom: 8px;
	}

	.setting-header h3 {
		margin: 0;
		font-size: 18px;
		color: #eef0f8;
	}

	.source-badge {
		display: inline-block;
		padding: 3px 10px;
		border-radius: 20px;
		font-size: 11px;
		font-weight: 600;
		text-transform: uppercase;
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
		padding: 4px 10px;
		border-radius: 6px;
		font-size: 12px;
		font-weight: 500;
	}

	.setting-description {
		margin: 0 0 16px;
		color: #9aa2c5;
		font-size: 14px;
		line-height: 1.5;
	}

	.setting-value {
		margin-bottom: 16px;
	}

	.setting-value code {
		display: inline-block;
		background: rgba(20, 24, 39, 0.8);
		padding: 8px 14px;
		border-radius: 8px;
		font-family: 'Consolas', 'Monaco', monospace;
		font-size: 14px;
		color: #6ab04c;
		border: 1px solid #2a2f47;
	}

	.not-configured {
		color: #7c87b2;
		font-style: italic;
	}

	.setting-actions,
	.edit-actions {
		display: flex;
		gap: 10px;
	}

	label {
		display: flex;
		flex-direction: column;
		gap: 6px;
		margin-bottom: 16px;
	}

	.label-text {
		font-size: 13px;
		color: #9aa2c5;
		font-weight: 500;
	}

	input {
		background: #141827;
		border: 1px solid #2a2f47;
		border-radius: 8px;
		padding: 12px 14px;
		color: #eef0f8;
		font-family: inherit;
		font-size: 14px;
		transition: border-color 0.2s;
	}

	input:focus {
		outline: none;
		border-color: rgba(106, 176, 76, 0.5);
	}

	.btn-primary {
		background: var(--mc-grass);
		color: #fff;
		border: none;
		border-radius: 8px;
		padding: 10px 20px;
		font-size: 14px;
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
		padding: 10px 20px;
		font-size: 14px;
		font-weight: 600;
		cursor: pointer;
		transition: background 0.2s;
	}

	.btn-secondary:hover:not(:disabled) {
		background: #3a3f5a;
	}

	.btn-secondary.danger:hover:not(:disabled) {
		background: rgba(255, 92, 92, 0.2);
		color: #ff9f9f;
	}

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

	.empty-state {
		text-align: center;
		padding: 60px 20px;
		color: #7c87b2;
		background: linear-gradient(135deg, #1a1e2f 0%, #141827 100%);
		border-radius: 16px;
		border: 1px solid #2a2f47;
	}

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
</style>
