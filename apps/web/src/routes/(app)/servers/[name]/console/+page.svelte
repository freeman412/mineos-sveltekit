<script lang="ts">
	import { onMount } from 'svelte';
	import { Terminal } from '@xterm/xterm';
	import { FitAddon } from '@xterm/addon-fit';
	import '@xterm/xterm/css/xterm.css';
	import type { PageData } from './$types';
	import type { LayoutData } from '../$layout';

	let { data }: { data: PageData & { server: LayoutData['server'] } } = $props();

	let terminalContainer: HTMLDivElement;
	let terminal: Terminal | null = null;
	let fitAddon: FitAddon | null = null;
	let eventSource: EventSource | null = null;
	let command = $state('');
	let sending = $state(false);

	onMount(() => {
		if (!data.server) return;

		// Create terminal
		terminal = new Terminal({
			cursorBlink: true,
			theme: {
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
				console.error('Failed to parse log entry:', err);
			}
		};

		eventSource.onerror = () => {
			terminal?.writeln('\x1b[1;31m[Connection lost. Reconnecting...]\x1b[0m');
			eventSource?.close();
			setTimeout(connectToLogs, 3000);
		};

		eventSource.onopen = () => {
			terminal?.writeln('\x1b[1;32m[Connected]\x1b[0m');
		};
	}

	async function sendCommand() {
		if (!data.server || !command.trim() || sending) return;

		sending = true;
		terminal?.writeln(`\x1b[1;33m> ${command}\x1b[0m`);

		try {
			const res = await fetch(`/api/servers/${data.server.name}/console`, {
				method: 'POST',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify({ command })
			});

			if (!res.ok) {
				const error = await res.json().catch(() => ({ error: 'Failed to send command' }));
				terminal?.writeln(`\x1b[1;31m[Error: ${error.error}]\x1b[0m`);
			}
		} catch (err) {
			terminal?.writeln(`\x1b[1;31m[Error sending command]\x1b[0m`);
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
	<div class="terminal-wrapper">
		<div bind:this={terminalContainer} class="terminal"></div>
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
