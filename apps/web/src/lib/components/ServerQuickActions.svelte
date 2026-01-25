<script lang="ts">
	import { createEventDispatcher } from 'svelte';
	import { browser } from '$app/environment';
	import { invalidateAll } from '$app/navigation';
	import { onMount } from 'svelte';
	import * as api from '$lib/api/client';
	import { modal } from '$lib/stores/modal';
	import type { ServerDetail } from '$lib/api/types';

	let { server }: { server: ServerDetail | null } = $props();
	const dispatch = createEventDispatcher<{ refresh: void }>();

	let actionLoading = $state(false);
	let copied = $state(false);
	let serverPort = $state<number>(25565);
	let status = $derived((server?.status ?? '').toLowerCase());
	let isRunning = $derived(status === 'up' || status === 'running');

	// Load server port from server.properties
	onMount(async () => {
		if (!server) return;
		try {
			const result = await api.getServerProperties(fetch, server.name);
			if (result.data) {
				const port = result.data['server-port'];
				if (port) {
					const parsed = parseInt(port, 10);
					if (!isNaN(parsed)) {
						serverPort = parsed;
					}
				}
			}
		} catch (err) {
			// Use default port if loading fails
			console.error('Failed to load server port:', err);
		}
	});

	// Calculate the server address to display
	const serverAddress = $derived.by(() => {
		if (!server) return '';
		const envHost = import.meta.env.PUBLIC_MINECRAFT_HOST as string | undefined;
		const host = (envHost && envHost.trim()) || (browser ? window.location.hostname : 'localhost');
		return host.includes(':') ? host : `${host}:${serverPort}`;
	});

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

	async function copyServerAddress() {
		if (!serverAddress) return;

		try {
			await navigator.clipboard.writeText(serverAddress);
			copied = true;
			setTimeout(() => {
				copied = false;
			}, 2000);
		} catch (err) {
			// Fallback: create a temporary textarea and use execCommand
			try {
				const textarea = document.createElement('textarea');
				textarea.value = serverAddress;
				textarea.style.position = 'fixed';
				textarea.style.opacity = '0';
				document.body.appendChild(textarea);
				textarea.select();
				const success = document.execCommand('copy');
				document.body.removeChild(textarea);

				if (success) {
					copied = true;
					setTimeout(() => {
						copied = false;
					}, 2000);
				} else {
					await modal.error('Failed to copy to clipboard');
				}
			} catch (fallbackErr) {
				await modal.error('Failed to copy to clipboard');
			}
		}
	}

</script>

{#if server}
	<div class="server-controls">
		<!-- Server Address Section -->
		<div class="address-section">
			<div class="address-display">
				<code class="server-address">
					{serverAddress}
				</code>
				<button
					class="btn-copy"
					onclick={copyServerAddress}
					title="Copy server address"
					disabled={!serverAddress}
				>
					{#if copied}
						<svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true">
							<path
								d="M5 13l4 4L19 7"
								fill="none"
								stroke="currentColor"
								stroke-width="2.5"
								stroke-linecap="round"
								stroke-linejoin="round"
							/>
						</svg>
					{:else}
						<svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true">
							<rect
								x="9"
								y="9"
								width="13"
								height="13"
								rx="2"
								fill="none"
								stroke="currentColor"
								stroke-width="2"
								stroke-linecap="round"
								stroke-linejoin="round"
							/>
							<path
								d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"
								fill="none"
								stroke="currentColor"
								stroke-width="2"
								stroke-linecap="round"
								stroke-linejoin="round"
							/>
						</svg>
					{/if}
				</button>
			</div>
			{#if server.needsRestart}
				<span class="pill">Restart required</span>
			{/if}
		</div>

		<!-- Action Buttons -->
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
	.server-controls {
		background: var(--mc-panel-dark, #141827);
		border-radius: 14px;
		padding: 16px;
		border: 1px solid var(--border-color, #2a2f47);
		display: flex;
		flex-direction: column;
		gap: 14px;
		min-width: 300px;
	}

	.address-section {
		display: flex;
		flex-direction: column;
		gap: 8px;
	}

	.address-display {
		display: flex;
		align-items: center;
		gap: 10px;
	}

	.server-address {
		flex: 1;
		background: var(--mc-panel-darkest, #0d1117);
		border: 1px solid var(--border-color, #2a2f47);
		padding: 10px 14px;
		border-radius: 8px;
		font-family: 'Courier New', 'Consolas', monospace;
		font-size: 14px;
		color: var(--mc-grass, #6ab04c);
		font-weight: 600;
		letter-spacing: 0.5px;
		display: block;
	}

	.btn-copy {
		background: var(--mc-grass, #6ab04c);
		color: white;
		border: none;
		padding: 10px;
		border-radius: 8px;
		cursor: pointer;
		display: flex;
		align-items: center;
		justify-content: center;
		transition: all 0.2s;
		flex-shrink: 0;
		width: 40px;
		height: 40px;
	}

	.btn-copy:hover {
		background: var(--mc-grass-dark, #4a8b34);
		transform: translateY(-1px);
		box-shadow: 0 4px 12px rgba(106, 176, 76, 0.3);
	}

	.btn-copy svg {
		flex-shrink: 0;
	}

	.pill {
		background: rgba(255, 200, 87, 0.15);
		border: 1px solid rgba(255, 200, 87, 0.3);
		color: #f4c08e;
		padding: 6px 12px;
		border-radius: 999px;
		font-size: 11px;
		font-weight: 600;
		text-transform: uppercase;
		letter-spacing: 0.03em;
		align-self: flex-start;
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
		.server-controls {
			width: 100%;
			min-width: unset;
		}

		.server-address {
			font-size: 13px;
		}
	}
</style>
