<script lang="ts">
	import { onMount } from 'svelte';
	import { Terminal } from '@xterm/xterm';
	import { FitAddon } from '@xterm/addon-fit';
	import '@xterm/xterm/css/xterm.css';
	import type { PageData } from './$types';
	import type { LayoutData } from '../$layout';

	type LogTab = 'server' | 'java';

	let { data }: { data: PageData & { server: LayoutData['server'] } } = $props();

	let terminalWrapper: HTMLDivElement;
	let serverTerminalContainer: HTMLDivElement;
	let javaTerminalContainer: HTMLDivElement;

	let serverTerminal: Terminal | null = null;
	let javaTerminal: Terminal | null = null;
	let serverFitAddon: FitAddon | null = null;
	let javaFitAddon: FitAddon | null = null;
	let serverEventSource: EventSource | null = null;
	let javaEventSource: EventSource | null = null;

	let activeTab = $state<LogTab>('server');
	let command = $state('');
	let sending = $state(false);

	const terminalTheme = {
		background: '#0d1117',
		foreground: '#c9d1d9',
		cursor: '#4299e1',
		black: '#484f58',
		red: '#ff7b72',
		green: '#3fb950',
		yellow: '#d29922',
		blue: '#58a6ff',
		magenta: '#bc8cff',
		cyan: '#39c5cf',
		white: '#b1bac4',
		brightBlack: '#6e7681',
		brightRed: '#ffa198',
		brightGreen: '#56d364',
		brightYellow: '#e3b341',
		brightBlue: '#79c0ff',
		brightMagenta: '#d2a8ff',
		brightCyan: '#56d4dd',
		brightWhite: '#f0f6fc'
	};

	onMount(() => {
		if (!data.server) return;

		const serverSetup = initTerminal(
			serverTerminalContainer,
			'MineOS Server Logs',
			'Connecting to server logs...'
		);
		serverTerminal = serverSetup.terminal;
		serverFitAddon = serverSetup.fitAddon;

		const javaSetup = initTerminal(javaTerminalContainer, 'MineOS Java Logs', 'Connecting to Java logs...');
		javaTerminal = javaSetup.terminal;
		javaFitAddon = javaSetup.fitAddon;

		connectToLogs('server');
		connectToLogs('java');

		const resizeObserver = new ResizeObserver(() => {
			fitActiveTerminal();
		});

		resizeObserver.observe(terminalWrapper);

		return () => {
			serverTerminal?.dispose();
			javaTerminal?.dispose();
			serverEventSource?.close();
			javaEventSource?.close();
			resizeObserver.disconnect();
		};
	});

	function initTerminal(container: HTMLDivElement, title: string, subtitle: string) {
		const terminal = new Terminal({
			cursorBlink: true,
			theme: terminalTheme,
			fontFamily: '"Cascadia Code", "Fira Code", "Consolas", monospace',
			fontSize: 13,
			lineHeight: 1.2,
			scrollback: 10000
		});

		const fitAddon = new FitAddon();
		terminal.loadAddon(fitAddon);
		terminal.open(container);
		fitAddon.fit();

		terminal.writeln(`\x1b[1;36m=== ${title} ===\x1b[0m`);
		terminal.writeln(`\x1b[90m${subtitle}\x1b[0m`);
		terminal.writeln('');

		return { terminal, fitAddon };
	}

	function connectToLogs(tab: LogTab) {
		if (!data.server) return;

		const source = tab === 'java' ? 'java' : 'server';
		const terminal = tab === 'java' ? javaTerminal : serverTerminal;
		const existing = tab === 'java' ? javaEventSource : serverEventSource;

		existing?.close();

		const eventSource = new EventSource(`/api/servers/${data.server.name}/console/stream?source=${source}`);
		if (tab === 'java') {
			javaEventSource = eventSource;
		} else {
			serverEventSource = eventSource;
		}

		eventSource.onmessage = (event) => {
			try {
				const log = JSON.parse(event.data);
				terminal?.writeln(log.message);
			} catch (err) {
				console.error('Failed to parse log entry:', err);
			}
		};

		eventSource.onerror = () => {
			terminal?.writeln('\x1b[1;31m[Connection lost. Reconnecting...]\x1b[0m');
			eventSource?.close();
			setTimeout(() => connectToLogs(tab), 3000);
		};

		eventSource.onopen = () => {
			terminal?.writeln('\x1b[1;32m[Connected]\x1b[0m');
		};
	}

	function setActiveTab(tab: LogTab) {
		if (activeTab === tab) return;
		activeTab = tab;
		requestAnimationFrame(() => {
			fitActiveTerminal();
		});
	}

	function fitActiveTerminal() {
		if (activeTab === 'server') {
			serverFitAddon?.fit();
		} else {
			javaFitAddon?.fit();
		}
	}

	async function sendCommand() {
		if (!data.server || !command.trim() || sending) return;

		sending = true;
		serverTerminal?.writeln(`\x1b[1;33m> ${command}\x1b[0m`);

		try {
			const res = await fetch(`/api/servers/${data.server.name}/console`, {
				method: 'POST',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify({ command })
			});

			if (!res.ok) {
				const error = await res.json().catch(() => ({ error: 'Failed to send command' }));
				serverTerminal?.writeln(`\x1b[1;31m[Error: ${error.error}]\x1b[0m`);
			}
		} catch (err) {
			serverTerminal?.writeln(`\x1b[1;31m[Error sending command]\x1b[0m`);
		} finally {
			command = '';
			sending = false;
		}
	}

	function handleKeydown(e: KeyboardEvent) {
		if (e.key === 'Enter') {
			e.preventDefault();
			sendCommand();
		}
	}
</script>

<div class="console-container">
	<div class="console-header">
		<div class="tab-bar">
			<button
				class:active={activeTab === 'server'}
				class="tab-button"
				onclick={() => setActiveTab('server')}
			>
				Server Logs
			</button>
			<button
				class:active={activeTab === 'java'}
				class="tab-button"
				onclick={() => setActiveTab('java')}
			>
				Java Logs
			</button>
		</div>
	</div>

	<div bind:this={terminalWrapper} class="terminal-wrapper">
		<div bind:this={serverTerminalContainer} class="terminal" class:hidden={activeTab !== 'server'}></div>
		<div bind:this={javaTerminalContainer} class="terminal" class:hidden={activeTab !== 'java'}></div>
	</div>

	<div class="command-bar">
		<input
			type="text"
			bind:value={command}
			onkeydown={handleKeydown}
			placeholder="Type a command and press Enter..."
			disabled={sending}
			class="command-input"
		/>
		<button onclick={sendCommand} disabled={sending || !command.trim()} class="send-button">
			{sending ? 'Sending...' : 'Send'}
		</button>
	</div>
</div>

<style>
	.console-container {
		display: flex;
		flex-direction: column;
		height: 100%;
		gap: 1rem;
	}

	.console-header {
		display: flex;
		justify-content: space-between;
		align-items: center;
	}

	.tab-bar {
		display: flex;
		gap: 0.5rem;
		background: #121725;
		padding: 0.35rem;
		border-radius: 10px;
	}

	.tab-button {
		background: transparent;
		border: none;
		color: #9aa2c5;
		padding: 0.4rem 0.9rem;
		border-radius: 8px;
		font-size: 0.9rem;
		cursor: pointer;
		transition: background 0.2s, color 0.2s;
	}

	.tab-button.active {
		background: #1f2a4a;
		color: #e6e9f6;
	}

	.tab-button:hover:not(.active) {
		background: #1a223a;
		color: #c9d1e6;
	}

	.terminal-wrapper {
		flex: 1;
		background: #0d1117;
		border-radius: 8px;
		padding: 1rem;
		overflow: hidden;
		box-shadow: 0 4px 12px rgba(0, 0, 0, 0.3);
	}

	.terminal {
		width: 100%;
		height: 100%;
	}

	.terminal.hidden {
		display: none;
	}

	.command-bar {
		display: flex;
		gap: 0.5rem;
		background: #1a1a1a;
		padding: 1rem;
		border-radius: 8px;
	}

	.command-input {
		flex: 1;
		background: #0d1117;
		color: #c9d1d9;
		border: 1px solid #333;
		border-radius: 4px;
		padding: 0.5rem 1rem;
		font-family: 'Cascadia Code', 'Fira Code', 'Consolas', monospace;
		font-size: 0.9rem;
	}

	.command-input:focus {
		outline: none;
		border-color: #4299e1;
	}

	.command-input:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.send-button {
		background: #4299e1;
		color: #fff;
		border: none;
		padding: 0.5rem 1.5rem;
		border-radius: 4px;
		cursor: pointer;
		font-size: 0.9rem;
		font-weight: 500;
		transition: background 0.2s;
	}

	.send-button:hover:not(:disabled) {
		background: #3182ce;
	}

	.send-button:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}
</style>
