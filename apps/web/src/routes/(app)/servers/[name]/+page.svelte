<script lang="ts">
	import { onMount } from 'svelte';
	import { invalidateAll } from '$app/navigation';
	import '@xterm/xterm/css/xterm.css';
	import * as api from '$lib/api/client';
	import type { PageData } from './$types';
	import type { LayoutData } from './$types';

	type TerminalType = import('@xterm/xterm').Terminal;
	type FitAddonType = import('@xterm/addon-fit').FitAddon;

	let { data }: { data: PageData & { server: LayoutData['server'] } } = $props();

	let actionLoading = $state(false);
	let terminalContainer: HTMLDivElement;
	let terminal: TerminalType | null = $state(null);
	let fitAddon: FitAddonType | null = null;
	let resizeObserver: ResizeObserver | null = null;
	let eventSource: EventSource | null = null;
	let command = $state('');
	let sending = $state(false);
	let heartbeat = $state(data.heartbeat.data ?? null);
	let heartbeatError = $state<string | null>(data.heartbeat.error);
	let heartbeatSource: EventSource | null = null;
	let heartbeatStatus = $derived((heartbeat?.status ?? '').toLowerCase());
	let isRunning = $derived(heartbeatStatus === 'up' || heartbeatStatus === 'running');
	let memoryHistory = $state<number[]>([]);

	const maxMemoryPoints = 40;

	const resolveModule = <T>(module: T | { default: T }): T =>
		(module as { default?: T }).default ?? (module as T);

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
				await invalidateAll();
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

	function buildSparkline(values: number[], width = 200, height = 60) {
		if (!values || values.length < 2) return '';
		const min = Math.min(...values);
		const max = Math.max(...values);
		const range = max - min || 1;
		return values
			.map((value, idx) => {
				const x = (idx / (values.length - 1)) * width;
				const y = height - ((value - min) / range) * height;
				return `${x},${y}`;
			})
			.join(' ');
	}

	function updateMemoryHistory(value: number | null) {
		if (value == null || value <= 0) return;
		const next = [...memoryHistory, value];
		if (next.length > maxMemoryPoints) {
			next.shift();
		}
		memoryHistory = next;
	}

	const formatDate = (dateStr: string) => {
		const date = new Date(dateStr);
		return date.toLocaleDateString() + ' ' + date.toLocaleTimeString();
	};

	onMount(() => {
		if (!data.server) return;

		let disposed = false;

		const initTerminal = async () => {
			const xtermModule = resolveModule(await import('@xterm/xterm'));
			const fitModule = resolveModule(await import('@xterm/addon-fit'));
			const TerminalCtor = (xtermModule as typeof import('@xterm/xterm')).Terminal;
			const FitAddonCtor = (fitModule as typeof import('@xterm/addon-fit')).FitAddon;

			if (disposed) {
				return;
			}

			if (!TerminalCtor || !FitAddonCtor) {
				console.error('Failed to load xterm modules');
				return;
			}

			terminal = new TerminalCtor({
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

			fitAddon = new FitAddonCtor();
			terminal.loadAddon(fitAddon);
			terminal.open(terminalContainer);
			fitAddon.fit();

			terminal.writeln('\x1b[1;36m=== MineOS Console ===\x1b[0m');
			terminal.writeln('\x1b[90mConnecting to server logs...\x1b[0m');
			terminal.writeln('');

			connectToLogs();
			connectToHeartbeat();

			resizeObserver = new ResizeObserver(() => {
				fitAddon?.fit();
			});
			resizeObserver.observe(terminalContainer);
		};

		initTerminal();

		return () => {
			disposed = true;
			terminal?.dispose();
			eventSource?.close();
			heartbeatSource?.close();
			resizeObserver?.disconnect();
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

	function connectToHeartbeat() {
		if (!data.server) return;

		heartbeatSource = new EventSource(`/api/servers/${data.server.name}/heartbeat/stream`);
		heartbeatSource.onmessage = (event) => {
			try {
				heartbeat = JSON.parse(event.data);
				heartbeatError = null;
				if (heartbeat?.memoryBytes != null) {
					updateMemoryHistory(heartbeat.memoryBytes);
				}
			} catch (err) {
				console.error('Failed to parse heartbeat:', err);
			}
		};
		heartbeatSource.onerror = () => {
			heartbeatSource?.close();
			heartbeatSource = null;
			setTimeout(connectToHeartbeat, 2000);
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
		{#if data.server?.needsRestart}
			<div class="alert alert-warning">
				Server files changed. Restart required to apply updates.
			</div>
		{/if}
		<div class="action-buttons">
			{#if isRunning}
				<button class="btn btn-warning" onclick={() => handleAction('stop')} disabled={actionLoading}>
					Stop Server
				</button>
				<button class="btn btn-primary" onclick={() => handleAction('restart')} disabled={actionLoading}>
					Restart Server
				</button>
				<button class="btn btn-danger" onclick={() => handleAction('kill')} disabled={actionLoading}>
					Kill Server
				</button>
			{:else}
				<button class="btn btn-success" onclick={() => handleAction('start')} disabled={actionLoading}>
					Start Server
				</button>
				<button
					class="btn btn-secondary"
					onclick={handleAcceptEula}
					disabled={actionLoading || data.server?.eulaAccepted}
				>
					{data.server?.eulaAccepted ? 'EULA Accepted' : 'Accept EULA'}
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
					<span class="value">{heartbeat ? (isRunning ? 'Running' : 'Stopped') : 'Unknown'}</span>
				</div>
				<div class="info-row">
					<span class="label">Java PID</span>
					<span class="value">{heartbeat?.javaPid || 'N/A'}</span>
				</div>
				<div class="info-row">
					<span class="label">Screen PID</span>
					<span class="value">{heartbeat?.screenPid || 'N/A'}</span>
				</div>
				<div class="info-row">
					<span class="label">Memory</span>
					<span class="value">{formatBytes(heartbeat?.memoryBytes || null)}</span>
				</div>
			</div>
		</div>

		{#if heartbeat?.memoryBytes}
			<div class="card memory-card">
				<h3>Memory Usage</h3>
				<div class="memory-value">{formatBytes(heartbeat.memoryBytes)}</div>
				{#if memoryHistory.length > 1}
					<svg class="memory-chart" viewBox="0 0 200 60" preserveAspectRatio="none">
						<polyline
							points={buildSparkline(memoryHistory)}
							fill="none"
							stroke="rgba(106, 176, 76, 0.9)"
							stroke-width="2.5"
							stroke-linecap="round"
							stroke-linejoin="round"
						/>
					</svg>
				{/if}
			</div>
		{/if}

		{#if heartbeat?.ping}
			<div class="card">
				<h3>Ping Information</h3>
				<div class="info-grid">
					<div class="info-row">
						<span class="label">Version</span>
						<span class="value">{heartbeat.ping.serverVersion}</span>
					</div>
					<div class="info-row">
						<span class="label">Protocol</span>
						<span class="value">{heartbeat.ping.protocol}</span>
					</div>
					<div class="info-row">
						<span class="label">MOTD</span>
						<span class="value">{heartbeat.ping.motd}</span>
					</div>
					<div class="info-row">
						<span class="label">Players</span>
						<span class="value">
							{heartbeat.ping.playersOnline} / {heartbeat.ping.playersMax}
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
					disabled={sending || !isRunning}
					onkeydown={(e) => e.key === 'Enter' && sendCommand()}
				/>
				<button
					class="btn btn-primary"
					onclick={sendCommand}
					disabled={sending || !command.trim() || !isRunning}
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

	.alert {
		padding: 12px 16px;
		border-radius: 10px;
		font-size: 13px;
		margin-bottom: 16px;
	}

	.alert-warning {
		background: rgba(255, 200, 87, 0.15);
		border: 1px solid rgba(255, 200, 87, 0.3);
		color: #f4c08e;
	}

	.card h3 {
		margin: 0 0 16px;
		font-size: 16px;
		font-weight: 600;
		color: #9aa2c5;
	}

	.memory-card {
		gap: 12px;
	}

	.memory-value {
		font-size: 22px;
		font-weight: 600;
		color: #eef0f8;
	}

	.memory-chart {
		width: 100%;
		height: 60px;
		opacity: 0.9;
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
