<script lang="ts">
	import { createEventDispatcher } from 'svelte';
	import { invalidateAll } from '$app/navigation';
	import * as api from '$lib/api/client';
	import { modal } from '$lib/stores/modal';
	import type { ServerDetail } from '$lib/api/types';

	let { server }: { server: ServerDetail | null } = $props();
	const dispatch = createEventDispatcher<{ refresh: void }>();

	let actionLoading = $state(false);
	let status = $derived((server?.status ?? '').toLowerCase());
	let isRunning = $derived(status === 'up' || status === 'running');

	async function handleAction(action: 'start' | 'stop' | 'restart' | 'kill') {
		if (!server) return;

		actionLoading = true;
		try {
			let result;
			switch (action) {
				case 'start':
					result = await api.startServer(fetch, server.name);
					break;
				case 'stop':
					result = await api.stopServer(fetch, server.name);
					break;
				case 'restart':
					result = await api.restartServer(fetch, server.name);
					break;
				case 'kill':
					result = await api.killServer(fetch, server.name);
					break;
			}

			if (result.error) {
				await modal.error(`Failed to ${action} server: ${result.error}`);
			} else {
				dispatch('refresh');
				setTimeout(() => dispatch('refresh'), 2000);
				setTimeout(() => invalidateAll(), 2000);
			}
		} finally {
			actionLoading = false;
		}
	}

	async function handleAcceptEula() {
		if (!server) return;

		actionLoading = true;
		try {
			const result = await api.acceptEula(fetch, server.name);
			if (result.error) {
				await modal.error(`Failed to accept EULA: ${result.error}`);
			} else {
				await modal.success('EULA accepted successfully! You can now start the server.');
				dispatch('refresh');
				await invalidateAll();
			}
		} finally {
			actionLoading = false;
		}
	}

</script>

{#if server}
	<div class="quick-actions">
		<div class="quick-actions__header">
			<span class="title">Quick actions</span>
			{#if server.needsRestart}
				<span class="pill">Restart required</span>
			{/if}
		</div>
		<div class="action-buttons">
			{#if isRunning}
				<button class="btn btn-warning" onclick={() => handleAction('stop')} disabled={actionLoading}>
					Stop
				</button>
				<button class="btn btn-primary" onclick={() => handleAction('restart')} disabled={actionLoading}>
					Restart
				</button>
				<button class="btn btn-danger" onclick={() => handleAction('kill')} disabled={actionLoading}>
					Kill
				</button>
			{:else}
				<button class="btn btn-success" onclick={() => handleAction('start')} disabled={actionLoading}>
					Start
				</button>
				<button
					class="btn btn-secondary"
					onclick={handleAcceptEula}
					disabled={actionLoading || server.eulaAccepted}
				>
					{server.eulaAccepted ? 'EULA accepted' : 'Accept EULA'}
				</button>
			{/if}
		</div>
	</div>
{/if}

<style>
	.quick-actions {
		background: #1a1e2f;
		border-radius: 14px;
		padding: 16px;
		border: 1px solid #2a2f47;
		display: flex;
		flex-direction: column;
		gap: 12px;
		min-width: 260px;
	}

	.quick-actions__header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 12px;
	}

	.title {
		font-size: 13px;
		font-weight: 600;
		letter-spacing: 0.02em;
		text-transform: uppercase;
		color: #9aa2c5;
	}

	.pill {
		background: rgba(255, 200, 87, 0.15);
		border: 1px solid rgba(255, 200, 87, 0.3);
		color: #f4c08e;
		padding: 4px 10px;
		border-radius: 999px;
		font-size: 12px;
		font-weight: 600;
	}

	.action-buttons {
		display: flex;
		gap: 10px;
		flex-wrap: wrap;
	}


	.btn {
		background: #2b2f45;
		color: #d4d9f1;
		border: none;
		border-radius: 8px;
		padding: 10px 16px;
		font-family: inherit;
		font-size: 13px;
		font-weight: 600;
		cursor: pointer;
		transition: all 0.2s;
		display: flex;
		align-items: center;
		gap: 8px;
	}

	.btn:hover:not(:disabled) {
		transform: translateY(-1px);
		box-shadow: 0 4px 12px rgba(0, 0, 0, 0.3);
	}

	.btn:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.btn-primary {
		background: var(--mc-grass);
		color: white;
	}

	.btn-primary:hover:not(:disabled) {
		background: var(--mc-grass-dark);
	}

	.btn-success {
		background: rgba(106, 176, 76, 0.18);
		color: #b7f5a2;
		border: 1px solid rgba(106, 176, 76, 0.35);
	}

	.btn-success:hover:not(:disabled) {
		background: rgba(106, 176, 76, 0.28);
	}

	.btn-warning {
		background: rgba(139, 90, 43, 0.2);
		color: #f4c08e;
		border: 1px solid rgba(139, 90, 43, 0.4);
	}

	.btn-warning:hover:not(:disabled) {
		background: rgba(139, 90, 43, 0.3);
	}

	.btn-danger {
		background: rgba(210, 94, 72, 0.2);
		color: #ffb6a6;
		border: 1px solid rgba(210, 94, 72, 0.4);
	}

	.btn-danger:hover:not(:disabled) {
		background: rgba(210, 94, 72, 0.3);
	}

	.btn-secondary {
		background: #2b2f45;
		color: #d4d9f1;
		border: 1px solid #3a3f5a;
	}

	.btn-secondary:hover:not(:disabled) {
		background: #3a3f5a;
	}

	@media (max-width: 720px) {
		.quick-actions {
			width: 100%;
		}
	}
</style>
