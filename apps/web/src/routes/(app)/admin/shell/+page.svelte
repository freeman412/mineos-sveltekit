<script lang="ts">
	import { onMount } from 'svelte';
	import '@xterm/xterm/css/xterm.css';
	import { env } from '$env/dynamic/public';

	type TerminalType = import('@xterm/xterm').Terminal;
	type FitAddonType = import('@xterm/addon-fit').FitAddon;
	type TerminalCtor = typeof import('@xterm/xterm').Terminal;
	type FitAddonCtor = typeof import('@xterm/addon-fit').FitAddon;

	let terminalContainer: HTMLDivElement;
	let terminal: TerminalType | null = null;
	let fitAddon: FitAddonType | null = null;
	let terminalCtor: TerminalCtor | null = null;
	let fitAddonCtor: FitAddonCtor | null = null;
	let socket: WebSocket | null = null;
	let resizeObserver: ResizeObserver | null = null;
	let lastResize = { cols: 0, rows: 0 };

	let status = $state('disconnected');
	let errorMessage = $state('');

	const resolveModule = <T>(module: T | { default: T }): T =>
		(module as { default?: T }).default ?? (module as T);

	const terminalTheme = {
		background: '#0d1117',
		foreground: '#c9d1d9',
		cursor: '#79c0ff',
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

	const getWebSocketUrl = () => {
		const baseUrl = env.PUBLIC_API_BASE_URL || window.location.origin;
		const wsBase = baseUrl.replace(/^http/, 'ws');
		return `${wsBase}/api/v1/admin/shell/ws`;
	};

	function sendResize(cols?: number, rows?: number) {
		if (!terminal || socket?.readyState !== WebSocket.OPEN) return;
		const nextCols = cols ?? terminal.cols;
		const nextRows = rows ?? terminal.rows;
		if (!nextCols || !nextRows) return;
		if (nextCols === lastResize.cols && nextRows === lastResize.rows) return;
		lastResize = { cols: nextCols, rows: nextRows };
		socket.send(JSON.stringify({ type: 'resize', cols: nextCols, rows: nextRows }));
	}

	function connect() {
		if (!terminal) return;
		errorMessage = '';
		status = 'connecting';

		socket?.close();
		const wsUrl = getWebSocketUrl();
		socket = new WebSocket(wsUrl);

		socket.onopen = () => {
			status = 'connected';
			terminal?.writeln('\x1b[1;32m[Connected]\x1b[0m');
			terminal?.focus();
			sendResize();
		};

		socket.onclose = () => {
			status = 'disconnected';
			terminal?.writeln('\x1b[1;31m[Disconnected]\x1b[0m');
		};

		socket.onerror = () => {
			errorMessage = 'Failed to connect to admin shell.';
		};

		socket.onmessage = async (event) => {
			if (!terminal) return;
			if (typeof event.data === 'string') {
				terminal.write(event.data);
				return;
			}
			if (event.data instanceof ArrayBuffer) {
				terminal.write(new TextDecoder().decode(event.data));
				return;
			}
			if (event.data instanceof Blob) {
				terminal.write(await event.data.text());
			}
		};
	}

	onMount(() => {
		let disposed = false;

		const initTerminal = async () => {
			const xtermModule = resolveModule(await import('@xterm/xterm'));
			const fitModule = resolveModule(await import('@xterm/addon-fit'));
			terminalCtor = (xtermModule as typeof import('@xterm/xterm')).Terminal;
			fitAddonCtor = (fitModule as typeof import('@xterm/addon-fit')).FitAddon;

			if (disposed || !terminalCtor || !fitAddonCtor) {
				return;
			}

			terminal = new terminalCtor({
				cursorBlink: true,
				theme: terminalTheme,
				fontFamily: '"Cascadia Code", "Fira Code", "Consolas", monospace',
				fontSize: 13,
				lineHeight: 1.2,
				scrollback: 5000
			});

			fitAddon = new fitAddonCtor();
			terminal.loadAddon(fitAddon);
			terminal.open(terminalContainer);
			fitAddon.fit();
			sendResize();

			terminal.writeln('\x1b[1;36m=== MineOS Admin Shell ===\x1b[0m');
			terminal.writeln('\x1b[90mConnected to container TTY\x1b[0m');
			terminal.writeln('');

			terminal.onData((data) => {
				if (socket?.readyState === WebSocket.OPEN) {
					socket.send(data);
				}
			});

			terminal.onResize(({ cols, rows }) => {
				sendResize(cols, rows);
			});

			resizeObserver = new ResizeObserver(() => {
				fitAddon?.fit();
				sendResize();
			});
			resizeObserver.observe(terminalContainer);

			connect();
		};

		initTerminal();

		return () => {
			disposed = true;
			socket?.close();
			terminal?.dispose();
			resizeObserver?.disconnect();
		};
	});
</script>

<div class="shell-page">
	<div class="page-header">
		<div>
			<h1>Admin Shell</h1>
			<p class="subtitle">Direct interactive TTY on the running container.</p>
		</div>
		<div class="header-actions">
			<div class="status-pill" class:status-running={status === 'connected'}>
				{status}
			</div>
			<button class="btn-secondary" onclick={connect} disabled={status === 'connected'}>
				Reconnect
			</button>
		</div>
	</div>

	<div class="shell-panel">
		<div class="terminal" bind:this={terminalContainer}></div>
		{#if errorMessage}
			<p class="error">{errorMessage}</p>
		{/if}
	</div>
</div>

<style>
	:global(.main-content) {
		display: flex;
		flex-direction: column;
		min-height: 0;
	}

	.shell-page {
		display: flex;
		flex-direction: column;
		flex: 1;
		min-height: 0;
		gap: 24px;
	}

	.page-header {
		display: flex;
		justify-content: space-between;
		align-items: center;
		gap: 16px;
	}

	h1 {
		margin: 0 0 8px;
		font-size: 30px;
	}

	.subtitle {
		margin: 0;
		color: #9aa2c5;
		font-size: 14px;
	}

	.header-actions {
		display: flex;
		align-items: center;
		gap: 12px;
	}

	.status-pill {
		padding: 6px 12px;
		border-radius: 999px;
		font-size: 12px;
		text-transform: uppercase;
		letter-spacing: 0.06em;
		background: rgba(255, 200, 87, 0.15);
		color: #f4c08e;
		border: 1px solid rgba(255, 200, 87, 0.3);
	}

	.status-running {
		background: rgba(106, 176, 76, 0.2);
		color: #b7f5a2;
		border-color: rgba(106, 176, 76, 0.4);
	}

	.shell-panel {
		background: #1a1e2f;
		border-radius: 16px;
		padding: 20px;
		border: 1px solid rgba(106, 176, 76, 0.12);
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.35);
		flex: 1;
		min-height: 0;
		display: flex;
		flex-direction: column;
	}

	.terminal {
		flex: 1;
		min-height: 0;
		background: #0d1117;
		border-radius: 12px;
		overflow: hidden;
	}

	.btn-secondary {
		background: #1f2a4a;
		color: #e6e9f6;
		border: 1px solid #2e3a5f;
		border-radius: 8px;
		padding: 8px 14px;
		font-size: 13px;
		cursor: pointer;
		transition: background 0.2s;
	}

	.btn-secondary:hover:not(:disabled) {
		background: #273157;
	}

	.btn-secondary:disabled {
		opacity: 0.6;
		cursor: not-allowed;
	}

	.error {
		margin-top: 12px;
		color: #ff9f9f;
		font-size: 13px;
	}
</style>
