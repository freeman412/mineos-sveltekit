<script lang="ts">
	import { invalidateAll } from '$app/navigation';
	import * as api from '$lib/api/client';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	let actionLoading = $state<Record<string, boolean>>({});
	let showCreateModal = $state(false);
	let newServerName = $state('');
	let createError = $state('');

	async function handleAction(serverName: string, action: 'start' | 'stop' | 'restart' | 'kill') {
		actionLoading[serverName] = true;
		try {
			let result;
			switch (action) {
				case 'start':
					result = await api.startServer(fetch, serverName);
					break;
				case 'stop':
					result = await api.stopServer(fetch, serverName);
					break;
				case 'restart':
					result = await api.restartServer(fetch, serverName);
					break;
				case 'kill':
					result = await api.killServer(fetch, serverName);
					break;
			}

			if (result.error) {
				alert(`Failed to ${action} server: ${result.error}`);
			} else {
				// Wait a bit for the action to complete, then refresh
				setTimeout(() => invalidateAll(), 1000);
			}
		} finally {
			delete actionLoading[serverName];
			actionLoading = { ...actionLoading };
		}
	}

	async function handleDelete(serverName: string) {
		if (!confirm(`Are you sure you want to delete server "${serverName}"?`)) {
			return;
		}

		actionLoading[serverName] = true;
		try {
			const result = await api.deleteServer(fetch, serverName);
			if (result.error) {
				alert(`Failed to delete server: ${result.error}`);
			} else {
				await invalidateAll();
			}
		} finally {
			delete actionLoading[serverName];
			actionLoading = { ...actionLoading };
		}
	}

	async function handleCreateServer() {
		if (!newServerName.trim()) {
			createError = 'Server name is required';
			return;
		}

		createError = '';
		const result = await api.createServer(fetch, {
			name: newServerName.trim(),
			ownerUid: 1000, // TODO: Get from current user
			ownerGid: 1000
		});

		if (result.error) {
			createError = result.error;
		} else {
			showCreateModal = false;
			newServerName = '';
			await invalidateAll();
		}
	}
</script>

<div class="page-header">
	<div>
		<h1>Servers</h1>
		<p class="subtitle">Manage your Minecraft servers</p>
	</div>
	<button class="btn-primary" onclick={() => (showCreateModal = true)}>
		<span>+</span> Create Server
	</button>
</div>

{#if data.servers.error}
	<div class="error-box">
		<p>Failed to load servers: {data.servers.error}</p>
	</div>
{:else if data.servers.data && data.servers.data.length > 0}
	<div class="server-grid">
		{#each data.servers.data as server}
			<div class="server-card">
				<div class="card-header">
					<div>
						<h2>{server.name}</h2>
						<span class="status-badge" class:status-up={server.up} class:status-down={!server.up}>
							{server.up ? 'Running' : 'Stopped'}
						</span>
					</div>
				</div>

				<div class="card-body">
					{#if server.profile}
						<div class="info-row">
							<span class="label">Profile:</span>
							<span class="value">{server.profile}</span>
						</div>
					{/if}
					{#if server.port}
						<div class="info-row">
							<span class="label">Port:</span>
							<span class="value">{server.port}</span>
						</div>
					{/if}
					{#if server.playersOnline !== null && server.playersMax !== null}
						<div class="info-row">
							<span class="label">Players:</span>
							<span class="value">{server.playersOnline} / {server.playersMax}</span>
						</div>
					{/if}
				</div>

				<div class="card-actions">
					{#if server.up}
						<button
							class="btn-action btn-warning"
							onclick={() => handleAction(server.name, 'stop')}
							disabled={actionLoading[server.name]}
						>
							Stop
						</button>
						<button
							class="btn-action"
							onclick={() => handleAction(server.name, 'restart')}
							disabled={actionLoading[server.name]}
						>
							Restart
						</button>
						<button
							class="btn-action btn-danger"
							onclick={() => handleAction(server.name, 'kill')}
							disabled={actionLoading[server.name]}
						>
							Kill
						</button>
					{:else}
						<button
							class="btn-action btn-success"
							onclick={() => handleAction(server.name, 'start')}
							disabled={actionLoading[server.name]}
						>
							Start
						</button>
					{/if}
					<a href="/servers/{server.name}" class="btn-action">Details</a>
					<button
						class="btn-action btn-danger"
						onclick={() => handleDelete(server.name)}
						disabled={actionLoading[server.name]}
					>
						Delete
					</button>
				</div>
			</div>
		{/each}
	</div>
{:else}
	<div class="empty-state">
		<p class="empty-icon">📦</p>
		<h2>No servers yet</h2>
		<p>Create your first Minecraft server to get started</p>
		<button class="btn-primary" onclick={() => (showCreateModal = true)}>Create Server</button>
	</div>
{/if}

{#if showCreateModal}
	<div class="modal-overlay" onclick={() => (showCreateModal = false)}>
		<div class="modal" onclick={(e) => e.stopPropagation()}>
			<h2>Create New Server</h2>
			<div class="form-field">
				<label for="server-name">Server Name</label>
				<input
					type="text"
					id="server-name"
					bind:value={newServerName}
					placeholder="my-server"
					autofocus
				/>
			</div>
			{#if createError}
				<div class="error-text">{createError}</div>
			{/if}
			<div class="modal-actions">
				<button class="btn-secondary" onclick={() => (showCreateModal = false)}>Cancel</button>
				<button class="btn-primary" onclick={handleCreateServer}>Create</button>
			</div>
		</div>
	</div>
{/if}

<style>
	.page-header {
		display: flex;
		justify-content: space-between;
		align-items: center;
		margin-bottom: 32px;
		gap: 24px;
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

	.btn-primary {
		background: #5865f2;
		color: white;
		border: none;
		border-radius: 8px;
		padding: 12px 24px;
		font-family: inherit;
		font-size: 15px;
		font-weight: 600;
		cursor: pointer;
		transition: background 0.2s;
		display: flex;
		align-items: center;
		gap: 8px;
	}

	.btn-primary:hover {
		background: #4752c4;
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

	.server-grid {
		display: grid;
		grid-template-columns: repeat(auto-fill, minmax(350px, 1fr));
		gap: 20px;
	}

	.server-card {
		background: #1a1e2f;
		border-radius: 16px;
		padding: 20px;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.35);
	}

	.card-header {
		display: flex;
		justify-content: space-between;
		align-items: flex-start;
		margin-bottom: 16px;
		gap: 12px;
	}

	.card-header h2 {
		margin: 0 0 8px;
		font-size: 20px;
		font-weight: 600;
	}

	.status-badge {
		display: inline-block;
		padding: 4px 12px;
		border-radius: 12px;
		font-size: 12px;
		font-weight: 500;
	}

	.status-up {
		background: rgba(122, 230, 141, 0.15);
		color: #7ae68d;
	}

	.status-down {
		background: rgba(255, 159, 159, 0.15);
		color: #ff9f9f;
	}

	.card-body {
		display: flex;
		flex-direction: column;
		gap: 8px;
		margin-bottom: 16px;
	}

	.info-row {
		display: flex;
		justify-content: space-between;
		font-size: 14px;
	}

	.label {
		color: #9aa2c5;
	}

	.value {
		color: #eef0f8;
		font-weight: 500;
	}

	.card-actions {
		display: flex;
		gap: 8px;
		flex-wrap: wrap;
	}

	.btn-action {
		background: #2b2f45;
		color: #d4d9f1;
		border: none;
		border-radius: 6px;
		padding: 8px 14px;
		font-family: inherit;
		font-size: 13px;
		font-weight: 500;
		cursor: pointer;
		transition: all 0.2s;
		text-decoration: none;
		display: inline-block;
	}

	.btn-action:hover:not(:disabled) {
		background: #3a3f5a;
	}

	.btn-action:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.btn-success {
		background: rgba(122, 230, 141, 0.15);
		color: #7ae68d;
	}

	.btn-success:hover:not(:disabled) {
		background: rgba(122, 230, 141, 0.25);
	}

	.btn-warning {
		background: rgba(255, 200, 87, 0.15);
		color: #ffc857;
	}

	.btn-warning:hover:not(:disabled) {
		background: rgba(255, 200, 87, 0.25);
	}

	.btn-danger {
		background: rgba(255, 92, 92, 0.15);
		color: #ff9f9f;
	}

	.btn-danger:hover:not(:disabled) {
		background: rgba(255, 92, 92, 0.25);
	}

	.empty-state {
		text-align: center;
		padding: 80px 20px;
		color: #8e96bb;
	}

	.empty-icon {
		font-size: 64px;
		margin-bottom: 16px;
	}

	.empty-state h2 {
		margin: 0 0 8px;
		color: #eef0f8;
	}

	.empty-state p {
		margin: 0 0 24px;
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

	.modal h2 {
		margin: 0 0 20px;
		font-size: 22px;
	}

	.form-field {
		margin-bottom: 20px;
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
		padding: 12px 16px;
		color: #eef0f8;
		font-family: inherit;
		font-size: 14px;
		box-sizing: border-box;
	}

	.form-field input:focus {
		outline: none;
		border-color: #5865f2;
	}

	.error-text {
		color: #ff9f9f;
		font-size: 13px;
		margin-bottom: 16px;
	}

	.modal-actions {
		display: flex;
		gap: 12px;
		justify-content: flex-end;
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

	@media (max-width: 640px) {
		.page-header {
			flex-direction: column;
			align-items: flex-start;
		}

		.server-grid {
			grid-template-columns: 1fr;
		}
	}
</style>
