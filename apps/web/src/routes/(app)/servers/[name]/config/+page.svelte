<script lang="ts">
	import { enhance } from '$app/forms';
	import { untrack } from 'svelte';
	import type { PageData, ActionData } from './$types';

	let { data, form }: { data: PageData; form: ActionData } = $props();

	let properties = $state<Record<string, string>>({ ...(data.properties.data || {}) });
	let loading = $state(false);
	let showAddModal = $state(false);
	let newKey = $state('');
	let newValue = $state('');
	let lastServerName = data.serverName;

	$effect(() => {
		const previousName = untrack(() => lastServerName);
		if (data.serverName !== previousName) {
			properties = { ...(data.properties.data || {}) };
			lastServerName = data.serverName;
		}
	});

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
</script>

<div class="page">
	<div class="page-header">
		<div>
			<h2>Server Properties</h2>
			<p class="subtitle">Edit server.properties configuration</p>
		</div>
		<button class="btn-primary" onclick={() => (showAddModal = true)}>+ Add Property</button>
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
					await update();
					loading = false;
				};
			}}
		>
			<input type="hidden" name="properties" value={JSON.stringify(properties)} />

			<div class="properties-editor">
				{#if Object.keys(properties).length === 0}
					<div class="empty-state">
						<p>No properties defined yet</p>
						<button type="button" class="btn-secondary" onclick={() => (showAddModal = true)}>
							Add Property
						</button>
					</div>
				{:else}
					<div class="properties-list">
						{#each Object.entries(properties) as [key, value]}
							<div class="property-row">
								<div class="property-key">{key}</div>
								<input
									type="text"
									class="property-value"
									bind:value={properties[key]}
									placeholder="Value"
								/>
								<button
									type="button"
									class="btn-remove"
									onclick={() => removeProperty(key)}
									title="Remove property"
								>
									×
								</button>
							</div>
						{/each}
					</div>
				{/if}
			</div>

			{#if form?.error}
				<div class="error-text">{form.error}</div>
			{/if}

			{#if form?.success}
				<div class="success-text">Properties saved successfully!</div>
			{/if}

			<div class="actions">
				<button type="submit" class="btn-primary" disabled={loading}>
					{loading ? 'Saving...' : 'Save Changes'}
				</button>
			</div>
		</form>
	{/if}
</div>

{#if showAddModal}
	<!-- svelte-ignore a11y_no_static_element_interactions, a11y_click_events_have_key_events -->
	<div class="modal-overlay" onclick={() => (showAddModal = false)} role="presentation">
		<!-- svelte-ignore a11y_no_static_element_interactions, a11y_click_events_have_key_events, a11y_interactive_supports_focus -->
		<div class="modal" onclick={(e) => e.stopPropagation()} role="dialog" aria-modal="true" tabindex="-1">
			<h3>Add Property</h3>
			<div class="form-field">
				<label for="prop-key">Key</label>
				<!-- svelte-ignore a11y_autofocus -->
				<input
					type="text"
					id="prop-key"
					bind:value={newKey}
					placeholder="e.g., server-port"
					autofocus
				/>
			</div>
			<div class="form-field">
				<label for="prop-value">Value</label>
				<input type="text" id="prop-value" bind:value={newValue} placeholder="e.g., 25565" />
			</div>
			<div class="modal-actions">
				<button type="button" class="btn-secondary" onclick={() => (showAddModal = false)}>
					Cancel
				</button>
				<button type="button" class="btn-primary" onclick={addProperty}>Add</button>
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

	.btn-primary {
		background: #5865f2;
		color: white;
		border: none;
		border-radius: 8px;
		padding: 10px 20px;
		font-family: inherit;
		font-size: 14px;
		font-weight: 600;
		cursor: pointer;
		transition: background 0.2s;
	}

	.btn-primary:hover:not(:disabled) {
		background: #4752c4;
	}

	.btn-primary:disabled {
		opacity: 0.6;
		cursor: not-allowed;
	}

	.btn-secondary {
		background: #2b2f45;
		color: #d4d9f1;
		border: none;
		border-radius: 8px;
		padding: 10px 20px;
		font-family: inherit;
		font-size: 14px;
		font-weight: 500;
		cursor: pointer;
		transition: background 0.2s;
	}

	.btn-secondary:hover {
		background: #3a3f5a;
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

	.properties-editor {
		background: #1a1e2f;
		border-radius: 16px;
		padding: 24px;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.35);
	}

	.empty-state {
		text-align: center;
		padding: 40px 20px;
		color: #8890b1;
	}

	.empty-state p {
		margin: 0 0 16px;
	}

	.properties-list {
		display: flex;
		flex-direction: column;
		gap: 12px;
	}

	.property-row {
		display: grid;
		grid-template-columns: 200px 1fr auto;
		gap: 12px;
		align-items: center;
		padding: 12px;
		background: #141827;
		border-radius: 8px;
	}

	.property-key {
		font-weight: 500;
		color: #9aa2c5;
		font-size: 14px;
	}

	.property-value {
		background: #0f1118;
		border: 1px solid #2a2f47;
		border-radius: 6px;
		padding: 8px 12px;
		color: #eef0f8;
		font-family: 'Courier New', monospace;
		font-size: 13px;
	}

	.property-value:focus {
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
		background: rgba(255, 92, 92, 0.2);
	}

	.error-text {
		color: #ff9f9f;
		font-size: 14px;
	}

	.success-text {
		color: #7ae68d;
		font-size: 14px;
	}

	.actions {
		display: flex;
		justify-content: flex-end;
	}

	.modal-overlay {
		position: fixed;
		top: 0;
		left: 0;
		right: 0;
		bottom: 0;
		background: rgba(0, 0, 0, 0.7);
		display: flex;
		align-items: center;
		justify-content: center;
		z-index: 1000;
		padding: 24px;
	}

	.modal {
		background: #1a1e2f;
		border-radius: 16px;
		padding: 28px;
		max-width: 450px;
		width: 100%;
		box-shadow: 0 24px 50px rgba(0, 0, 0, 0.5);
	}

	.modal h3 {
		margin: 0 0 20px;
		font-size: 20px;
	}

	.form-field {
		margin-bottom: 16px;
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
		background: #141827;
		border: 1px solid #2a2f47;
		border-radius: 8px;
		padding: 10px 14px;
		color: #eef0f8;
		font-family: inherit;
		font-size: 14px;
		box-sizing: border-box;
	}

	.form-field input:focus {
		outline: none;
		border-color: #5865f2;
	}

	.modal-actions {
		display: flex;
		gap: 12px;
		justify-content: flex-end;
		margin-top: 20px;
	}

	@media (max-width: 768px) {
		.property-row {
			grid-template-columns: 1fr;
			gap: 8px;
		}

		.property-key {
			font-size: 13px;
		}
	}
</style>
