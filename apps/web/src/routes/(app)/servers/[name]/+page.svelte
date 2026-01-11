<script lang="ts">
	import { onMount } from 'svelte';
	import { invalidateAll } from '$app/navigation';
	import { Terminal } from '@xterm/xterm';
	import { FitAddon } from '@xterm/addon-fit';
	import '@xterm/xterm/css/xterm.css';
	import * as api from '$lib/api/client';
	import type { PageData } from './$types';
	import type { LayoutData } from './$types';

	let { data }: { data: PageData & { server: LayoutData['server'] } } = $props();

	let actionLoading = $state(false);
	let terminalContainer: HTMLDivElement;
	let terminal: Terminal | null = $state(null);
	let fitAddon: FitAddon | null = null;
	let eventSource: EventSource | null = null;
	let command = $state('');
	let sending = $state(false);

	async function handleAction(action: 'start' | 'stop' | 'restart' | 'kill') {
		if (!data.server) return;

		actionLoading = true;
		try {
			let result;
			switch (action) {
				case 'start':
					result = await api.startServer(fetch, data.server.name);
					break;
				case 'stop':
					result = await api.stopServer(fetch, data.server.name);
					break;
				case 'restart':
					result = await api.restartServer(fetch, data.server.name);
					break;
				case 'kill':
					result = await api.killServer(fetch, data.server.name);
					break;
			}

			if (result.error) {
				alert(`Failed to ${action} server: ${result.error}`);
			} else {
				// Wait for the action to complete, then refresh
				setTimeout(() => invalidateAll(), 2000);
			}
		} finally {
			actionLoading = false;
		}
	}

	async function handleAcceptEula() {
		if (!data.server) return;

		actionLoading = true;
		try {
			const result = await api.acceptEula(fetch, data.server.name);
			if (result.error) {
				alert(`Failed to accept EULA: ${result.error}`);
			} else {
				alert('EULA accepted successfully! You can now start the server.');
			}
		} finally {
			actionLoading = false;
		}
	}

	const formatBytes = (value: number | null) => {
		if (!value) return 'N/A';
		const units = ['B', 'KB', 'MB', 'GB', 'TB'];
		const i = Math.floor(Math.log(value) / Math.log(1024));
		return `${(value / Math.pow(1024, i)).toFixed(1)} ${units[i]}`;
	};

	const formatDate = (dateStr: string) => {
		const date = new Date(dateStr);
		return date.toLocaleDateString() + ' ' + date.toLocaleTimeString();
	};

	onMount(() => {
		if (!data.server) return;

		// Create terminal
		terminal = new Terminal({
			cursorBlink: true,
			theme: {
				background: '#0d1117',
				foreground: '#c9d1d9',
				cursor: '#4299e1'
			},
			fontFamily: '"Cascadia Code", "Fira Code", "Consolas", monospace',
			fontSize: 13,
			lineHeight: 1.2,
			scrollback: 10000
		});

		fitAddon = new FitAddon();
		terminal.loadAddon(fitAddon);
		terminal.open(terminalContainer);
		fitAddon.fit();

		terminal.writeln('\x1b[1;36m=== MineOS Console ===\x1b[0m');
		terminal.writeln('\x1b[90mConnecting to server logs...\x1b[0m');
		terminal.writeln('');

		// Connect to SSE stream
		connectToLogs();

		// Handle resize
		const resizeObserver = new ResizeObserver(() => {
			fitAddon?.fit();
		});
		resizeObserver.observe(terminalContainer);

		return () => {
			terminal?.dispose();
			eventSource?.close();
			resizeObserver.disconnect();
		};
	});

	function connectToLogs() {
		if (!data.server) return;

		eventSource = new EventSource(`/api/servers/${data.server.name}/console/stream`);

		eventSource.onmessage = (event) => {
			try {
				const log = JSON.parse(event.data);
				terminal?.writeln(log.message);
			} catch (err) {
				console.error('Failed to parse log message:', err);
			}
		};

		eventSource.onerror = () => {
			terminal?.writeln('\x1b[31mConnection lost. Reconnecting...\x1b[0m');
			eventSource?.close();
			setTimeout(connectToLogs, 2000);
		};
	}

	async function sendCommand() {
		if (!command.trim() || !data.server || sending) return;

		sending = true;
		try {
			const res = await fetch(`/api/servers/${data.server.name}/console`, {
				method: 'POST',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify({ command: command.trim() })
			});

			if (!res.ok) {
				terminal?.writeln('\x1b[31mFailed to send command\x1b[0m');
			} else {
				terminal?.writeln(`\x1b[90m> ${command.trim()}\x1b[0m`);
				command = '';
			}
		} finally {
			sending = false;
		}
	}
</script>

<div class="dashboard">
	<section class="section">
		<h2>Quick Actions</h2>
		<div class="action-buttons">
			{#if data.heartbeat.data?.status?.toLowerCase() === 'running'}
				<button class="btn btn-warning" onclick={() => handleAction('stop')} disabled={actionLoading}>
					⏹️ Stop Server
				</button>
				<button class="btn btn-primary" onclick={() => handleAction('restart')} disabled={actionLoading}>
					🔄 Restart Server
				</button>
				<button class="btn btn-danger" onclick={() => handleAction('kill')} disabled={actionLoading}>
					💀 Kill Server
				</button>
			{:else}
				<button class="btn btn-success" onclick={() => handleAction('start')} disabled={actionLoading}>
					▶️ Start Server
				</button>
				<button class="btn btn-secondary" onclick={handleAcceptEula} disabled={actionLoading}>
					📜 Accept EULA
				</button>
			{/if}
		</div>
	</section>

	<div class="grid">
		<div class="card">
			<h3>Server Information</h3>
			<div class="info-grid">
				<div class="info-row">
					<span class="label">Created</span>
					<span class="value">{data.server ? formatDate(data.server.createdAt) : 'N/A'}</span>
				</div>
				<div class="info-row">
					<span class="label">Owner</span>
					<span class="value">{data.server?.ownerUsername || 'N/A'}</span>
				</div>
				<div class="info-row">
					<span class="label">Group</span>
					<span class="value">{data.server?.ownerGroupname || 'N/A'}</span>
				</div>
				<div class="info-row">
					<span class="label">UID / GID</span>
					<span class="value">{data.server?.ownerUid} / {data.server?.ownerGid}</span>
				</div>
			</div>
		</div>

		<div class="card">
			<h3>Process Status</h3>
			<div class="info-grid">
				<div class="info-row">
					<span class="label">Status</span>
					<span class="value">{data.heartbeat.data?.status || 'Unknown'}</span>
				</div>
				<div class="info-row">
					<span class="label">Java PID</span>
					<span class="value">{data.heartbeat.data?.javaPid || 'N/A'}</span>
				</div>
				<div class="info-row">
					<span class="label">Screen PID</span>
					<span class="value">{data.heartbeat.data?.screenPid || 'N/A'}</span>
				</div>
				<div class="info-row">
					<span class="label">Memory</span>
					<span class="value">{formatBytes(data.heartbeat.data?.memoryBytes || null)}</span>
				</div>
			</div>
		</div>

		{#if data.heartbeat.data?.ping}
			<div class="card">
				<h3>Ping Information</h3>
				<div class="info-grid">
					<div class="info-row">
						<span class="label">Version</span>
						<span class="value">{data.heartbeat.data.ping.serverVersion}</span>
					</div>
					<div class="info-row">
						<span class="label">Protocol</span>
						<span class="value">{data.heartbeat.data.ping.protocol}</span>
					</div>
					<div class="info-row">
						<span class="label">MOTD</span>
						<span class="value">{data.heartbeat.data.ping.motd}</span>
					</div>
					<div class="info-row">
						<span class="label">Players</span>
						<span class="value">
							{data.heartbeat.data.ping.playersOnline} / {data.heartbeat.data.ping.playersMax}
						</span>
					</div>
				</div>
			</div>
		{/if}

		{#if data.server?.config}
			<div class="card">
				<h3>Java Configuration</h3>
				<div class="info-grid">
					<div class="info-row">
						<span class="label">Java Binary</span>
						<span class="value">{data.server.config.java.javaBinary || 'N/A'}</span>
					</div>
					<div class="info-row">
						<span class="label">Xmx / Xms</span>
						<span class="value">{data.server.config.java.javaXmx}M / {data.server.config.java.javaXms}M</span>
					</div>
					<div class="info-row">
						<span class="label">JAR File</span>
						<span class="value">{data.server.config.java.jarFile || 'N/A'}</span>
					</div>
					<div class="info-row">
						<span class="label">Profile</span>
						<span class="value">{data.server.config.minecraft.profile || 'N/A'}</span>
					</div>
				</div>
			</div>
		{/if}
	</div>

	<!-- Console Section -->
	<section class="section console-section">
		<h2>Console</h2>
		<div class="console-container">
			<div class="terminal-wrapper">
				<div bind:this={terminalContainer} class="terminal"></div>
			</div>
			<div class="command-input">
				<input
					type="text"
					bind:value={command}
					placeholder="Enter command..."
					disabled={sending || data.heartbeat.data?.status?.toLowerCase() !== 'running'}
					onkeydown={(e) => e.key === 'Enter' && sendCommand()}
				/>
				<button
					class="btn btn-primary"
					onclick={sendCommand}
					disabled={sending || !command.trim() || data.heartbeat.data?.status?.toLowerCase() !== 'running'}
				>
					{sending ? 'Sending...' : 'Send'}
				</button>
			</div>
		</div>
	</section>
</div>

<style>
	.dashboard {
		display: flex;
		flex-direction: column;
		gap: 28px;
	}

	.section h2 {
		margin: 0 0 16px;
		font-size: 20px;
		font-weight: 600;
	}

	.action-buttons {
		display: flex;
		gap: 12px;
		flex-wrap: wrap;
	}

	.btn {
		background: #2b2f45;
		color: #d4d9f1;
		border: none;
		border-radius: 8px;
		padding: 12px 24px;
		font-family: inherit;
		font-size: 14px;
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
		background: #5865f2;
		color: white;
	}

	.btn-primary:hover:not(:disabled) {
		background: #4752c4;
	}

	.btn-success {
		background: rgba(122, 230, 141, 0.15);
		color: #7ae68d;
		border: 1px solid rgba(122, 230, 141, 0.3);
	}

	.btn-success:hover:not(:disabled) {
		background: rgba(122, 230, 141, 0.25);
	}

	.btn-warning {
		background: rgba(255, 200, 87, 0.15);
		color: #ffc857;
		border: 1px solid rgba(255, 200, 87, 0.3);
	}

	.btn-warning:hover:not(:disabled) {
		background: rgba(255, 200, 87, 0.25);
	}

	.btn-danger {
		background: rgba(255, 92, 92, 0.15);
		color: #ff9f9f;
		border: 1px solid rgba(255, 92, 92, 0.3);
	}

	.btn-danger:hover:not(:disabled) {
		background: rgba(255, 92, 92, 0.25);
	}

	.btn-secondary {
		background: #2b2f45;
		color: #d4d9f1;
		border: 1px solid #3a3f5a;
	}

	.btn-secondary:hover:not(:disabled) {
		background: #3a3f5a;
	}

	.grid {
		display: grid;
		grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
		gap: 20px;
	}

	.card {
		background: #1a1e2f;
		border-radius: 16px;
		padding: 20px;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.35);
	}

	.card h3 {
		margin: 0 0 16px;
		font-size: 16px;
		font-weight: 600;
		color: #9aa2c5;
	}

	.info-grid {
		display: flex;
		flex-direction: column;
		gap: 12px;
	}

	.info-row {
		display: flex;
		justify-content: space-between;
		align-items: center;
		font-size: 14px;
	}

	.label {
		color: #8890b1;
	}

	.value {
		color: #eef0f8;
		font-weight: 500;
		text-align: right;
	}

	@media (max-width: 640px) {
		.grid {
			grid-template-columns: 1fr;
		}
	}

	/* Console Section */
	.console-section {
		margin-top: 8px;
	}

	.console-container {
		background: #0d1117;
		border-radius: 12px;
		overflow: hidden;
		border: 1px solid #2a2f47;
	}

	.terminal-wrapper {
		height: 500px;
		padding: 12px;
	}

	.terminal {
		height: 100%;
		width: 100%;
	}

	.command-input {
		display: flex;
		gap: 12px;
		padding: 12px;
		background: #141827;
		border-top: 1px solid #2a2f47;
	}

	.command-input input {
		flex: 1;
		background: #1a1e2f;
		border: 1px solid #2a2f47;
		border-radius: 6px;
		padding: 10px 14px;
		color: #eef0f8;
		font-family: 'Consolas', 'Monaco', monospace;
		font-size: 13px;
	}

	.command-input input:focus {
		outline: none;
		border-color: #5865f2;
	}

	.command-input input:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.command-input button {
		min-width: 100px;
	}
</style>
