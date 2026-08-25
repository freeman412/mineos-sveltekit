<script lang="ts">
	import { page } from '$app/stores';
	import { createEventStream, type EventStreamHandle } from '$lib/utils/eventStream';
	import StatusBadge from '$lib/components/StatusBadge.svelte';
	import ServerQuickActions from '$lib/components/ServerQuickActions.svelte';
	import ServerIconUploader from '$lib/components/ServerIconUploader.svelte';
	import * as api from '$lib/api/client';
	import { buildTabs, type Tab } from '$lib/utils/serverTabs';
	import type { ServerPanelData } from './panelData';
	import type { ServerHeartbeat } from '$lib/api/types';

	let {
		data,
		children
	}: { data: ServerPanelData; children: any } = $props();
	let server = $state(data.server);
	let playerInfo = $state<{ online: number | null; max: number | null; version: string | null }>({
		online: null,
		max: null,
		version: null
	});

	// Display-name rename (issue #180): the label is mutable, the backend name
	// (directory / screen session) never is — show it whenever they differ.
	// Lives here rather than in the route layout because this shell is shared by
	// /servers/[name] and /proxies/[name]; a proxy is a server and gets a label too.
	const shownName = $derived(server?.displayName || server?.name);
	const hasCustomName = $derived(Boolean(server?.displayName));
	let showRenameModal = $state(false);
	let renameValue = $state('');
	let renameError = $state<string | null>(null);
	let renaming = $state(false);
	let renameInput: HTMLInputElement | undefined = $state();

	function openRename() {
		renameValue = server?.displayName ?? '';
		renameError = null;
		showRenameModal = true;
	}

	async function saveRename() {
		if (!server || renaming) return;
		renaming = true;
		renameError = null;

		const result = await api.setDisplayName(fetch, server.name, renameValue.trim() || null);

		if (result.error) {
			renameError = result.error;
			renaming = false;
			return;
		}

		server = { ...server, displayName: renameValue.trim() || null };
		renaming = false;
		showRenameModal = false;
	}

	function handleRenameKeydown(e: KeyboardEvent) {
		if (!showRenameModal) return;
		if (e.key === 'Escape') {
			showRenameModal = false;
		} else if (e.key === 'Enter') {
			void saveRename();
		}
	}

	$effect(() => {
		if (showRenameModal) {
			renameInput?.focus();
		}
	});

	const isBedrock = $derived(server?.serverType === 'bedrock');
	const isProxy = $derived(server?.serverType === 'proxy');
	const profile = $derived(server?.config?.minecraft?.profile?.toLowerCase() ?? '');
	const jarFile = $derived(server?.config?.java?.jarFile?.toLowerCase() ?? '');
	const javaTweaks = $derived(server?.config?.java?.javaTweaks?.toLowerCase() ?? '');
	const jarArgs = $derived(server?.config?.java?.jarArgs?.toLowerCase() ?? '');
	const serverHint = $derived(profile + ' ' + jarFile + ' ' + javaTweaks + ' ' + jarArgs);
	const isModded = $derived(
		!isBedrock &&
			(serverHint.includes('forge') ||
				serverHint.includes('fabric') ||
				serverHint.includes('neoforge') ||
				serverHint.includes('quilt'))
	);
	const isPluginServer = $derived(
		!isBedrock &&
			(serverHint.includes('paper') ||
				serverHint.includes('spigot') ||
				serverHint.includes('purpur') ||
				serverHint.includes('bukkit') ||
				serverHint.includes('folia'))
	);

	// For proxies, the SLP ping returns a protocol-range string ("Velocity 1.7.2-1.18.1")
	// that's confusing in the page header. Derive the actual proxy build (e.g. "Velocity 3.1.1",
	// "BungeeCord build-2068") from the jar filename so the chip says what's running.
	const proxyVersionFromJar = $derived.by(() => {
		if (!isProxy) return null;
		const raw = server?.config?.java?.jarFile ?? '';
		const m = raw.match(/^(velocity|bungeecord|waterfall)-(.+?)\.jar$/i);
		if (!m) return null;
		const name = m[1].charAt(0).toUpperCase() + m[1].slice(1).toLowerCase();
		return `${name} ${m[2]}`;
	});
	const displayVersion = $derived(proxyVersionFromJar ?? playerInfo.version);

	const tabs: Tab[] = $derived(
		buildTabs({
			name: server?.name ?? '',
			serverType: server?.serverType,
			isModded,
			isPluginServer
		})
	);

	function isActiveTab(href: string, exact = false) {
		if (exact) {
			return $page.url.pathname === href;
		}
		return $page.url.pathname.startsWith(href);
	}

	function normalizeStatus(status?: string) {
		if (!status) return { label: 'Unknown', running: false };
		const value = status.toLowerCase();
		if (value === 'running' || value === 'up') return { label: 'Running', running: true };
		if (value === 'stopped' || value === 'down') return { label: 'Stopped', running: false };
		return { label: status, running: false };
	}

	const statusMeta = $derived(normalizeStatus(server?.status));

	let statusStream: EventStreamHandle | null = null;

	function scheduleBurstRefresh() {
		connectStatusStream();
	}

	// Re-runs whenever we navigate to a different server (data.server changes).
	// SvelteKit reuses this component instance across /servers/[name] navigations,
	// so we must reset state and reconnect the heartbeat stream to the new server —
	// otherwise the still-open stream keeps pushing the previous server's live
	// status/PID/ping into the header.
	$effect(() => {
		server = data.server;
		playerInfo = { online: null, max: null, version: null };
		// Pass the name explicitly (not via the reactive `server`) so this effect
		// depends only on data.server — otherwise the SSE handler's writes to
		// `server` would re-trigger it and reconnect on every heartbeat.
		connectStatusStream(data.server?.name);

		return () => {
			statusStream?.close();
			statusStream = null;
		};
	});

	function connectStatusStream(name: string | undefined = server?.name) {
		if (!name) return;
		statusStream?.close();
		// Reconnects with backoff instead of the old every-2-seconds retry loop.
		statusStream = createEventStream<ServerHeartbeat>({
			url: `/api/servers/${encodeURIComponent(name)}/heartbeat/stream`,
			onMessage: (heartbeat) => {
				if (server) {
					server = {
						...server,
						status: heartbeat.status,
						javaPid: heartbeat.javaPid,
						screenPid: heartbeat.screenPid
					};
				}
				playerInfo = {
					online: heartbeat.ping?.playersOnline ?? null,
					max: heartbeat.ping?.playersMax ?? null,
					version: heartbeat.ping?.serverVersion ?? null
				};
			},
			reconnect: {}
		});
	}
</script>

<div class="server-container">
	<div class="server-header">
		<div class="server-info">
			<a href={isProxy ? '/proxies' : '/servers'} class="breadcrumb">
				&lt; Back to {isProxy ? 'Proxies' : 'Servers'}
			</a>
			<div class="title-row">
				<h1>{shownName}</h1>
				<button class="rename-btn" onclick={openRename} aria-label="Rename server" title="Rename server">
					✎
				</button>
				<StatusBadge variant={statusMeta.running ? 'success' : 'warning'} size="lg">
					{statusMeta.label}
				</StatusBadge>
				{#if isProxy}
					<span class="proxy-chip">Proxy</span>
				{/if}
			</div>
			{#if hasCustomName && server?.name}
				<div class="backend-name">Backend name: <code>{server.name}</code></div>
			{/if}
			<div class="server-meta">
				<div class="meta-chip players">
					<span class="chip-label">Players</span>
					<span class="chip-value">{playerInfo.online ?? '--'}</span>
					<span class="chip-sep">/</span>
					<span class="chip-value muted">{playerInfo.max ?? '--'}</span>
				</div>
				{#if displayVersion}
					<div class="meta-chip">
						<span class="chip-label">Version</span>
						<span class="chip-value">{displayVersion}</span>
					</div>
				{/if}
				{#if server?.javaPid}
					<div class="meta-chip">
						<span class="chip-label">PID</span>
						<span class="chip-value">{server.javaPid}</span>
					</div>
				{/if}
			</div>
		</div>
		<div class="server-side">
			<div class="server-icon">
				{#if server?.name}
					<ServerIconUploader serverName={server.name} />
				{/if}
			</div>
			<div class="server-actions">
				<ServerQuickActions server={server} on:refresh={scheduleBurstRefresh} />
			</div>
		</div>
	</div>

	<nav class="tabs">
		{#each tabs as tab}
			{#if tab.disabled}
				<span class="tab disabled" title={tab.tooltip}>
					{tab.label}
				</span>
			{:else}
				<a href={tab.href} class="tab" class:active={isActiveTab(tab.href, tab.exact)}>
					{tab.label}
				</a>
			{/if}
		{/each}
	</nav>

	<div class="content">
		{@render children()}
	</div>
</div>

<svelte:window onkeydown={handleRenameKeydown} />

{#if showRenameModal}
	<!-- svelte-ignore a11y_no_static_element_interactions, a11y_click_events_have_key_events -->
	<div
		class="rename-backdrop"
		onclick={(e) => e.target === e.currentTarget && (showRenameModal = false)}
		role="presentation"
	>
		<div class="rename-modal" role="dialog" aria-modal="true" aria-label="Rename server">
			<h2>Rename server</h2>
			<p class="rename-hint">
				A friendly label shown in the UI. The backend name
				<code>{server?.name}</code> never changes.
			</p>
			<input
				bind:this={renameInput}
				bind:value={renameValue}
				maxlength="64"
				placeholder={server?.name}
				aria-label="Display name"
			/>
			{#if renameError}
				<p class="rename-error">{renameError}</p>
			{/if}
			<div class="rename-actions">
				<button class="btn-secondary" onclick={() => (showRenameModal = false)} disabled={renaming}>
					Cancel
				</button>
				<button class="btn-primary" onclick={saveRename} disabled={renaming}>
					{renaming ? 'Saving…' : 'Save'}
				</button>
			</div>
		</div>
	</div>
{/if}

<style>
	.server-container {
		display: flex;
		flex-direction: column;
		gap: 24px;
	}

	.server-header {
		position: relative;
		display: flex;
		justify-content: space-between;
		align-items: center;
		gap: 24px;
		padding: 24px 28px;
		background: linear-gradient(135deg, rgba(22, 27, 46, 0.95), rgba(10, 14, 24, 0.95));
		border: 1px solid rgba(42, 47, 71, 0.8);
		border-radius: 18px;
		box-shadow: 0 24px 40px rgba(0, 0, 0, 0.35);
		overflow: hidden;
	}

	.server-info {
		min-width: 240px;
		display: flex;
		flex-direction: column;
		gap: 12px;
		position: relative;
		z-index: 1;
	}

	.server-icon {
		display: flex;
		align-items: center;
	}

	.server-side {
		display: flex;
		align-items: center;
		gap: 18px;
		margin-left: auto;
		position: relative;
		z-index: 1;
	}

	.breadcrumb {
		display: inline-block;
		color: #8890b1;
		text-decoration: none;
		font-size: 14px;
		margin-bottom: 12px;
		transition: color 0.2s;
	}

	.breadcrumb:hover {
		color: #aab2d3;
	}

	h1 {
		margin: 0;
		font-size: 34px;
		font-weight: 700;
		letter-spacing: -0.02em;
	}

	.title-row {
		display: flex;
		align-items: center;
		gap: 14px;
		flex-wrap: wrap;
	}

	.proxy-chip {
		padding: 4px 12px;
		border-radius: 999px;
		background: rgba(96, 141, 255, 0.15);
		border: 1px solid rgba(96, 141, 255, 0.4);
		color: #a8c2ff;
		font-size: 11px;
		font-weight: 700;
		text-transform: uppercase;
		letter-spacing: 0.08em;
	}

	.server-meta {
		display: flex;
		align-items: center;
		gap: 12px;
		flex-wrap: wrap;
	}

	.meta-chip {
		display: inline-flex;
		align-items: center;
		gap: 8px;
		padding: 6px 12px;
		border-radius: 999px;
		background: rgba(19, 24, 40, 0.8);
		border: 1px solid rgba(62, 69, 100, 0.6);
		font-size: 12px;
		font-weight: 600;
		color: #cdd3ee;
	}

	.meta-chip.players {
		background: rgba(106, 176, 76, 0.18);
		border-color: rgba(106, 176, 76, 0.45);
		color: #d1f4c3;
	}

	.chip-label {
		color: #9aa6d1;
		text-transform: uppercase;
		letter-spacing: 0.06em;
		font-size: 10px;
	}

	.chip-value {
		color: #eef0f8;
		font-size: 14px;
	}

	.chip-value.muted {
		color: #c0c6e4;
	}

	.chip-sep {
		color: rgba(238, 240, 248, 0.6);
	}

	.server-header::before {
		content: '';
		position: absolute;
		inset: -20% 40% 30% -20%;
		background: radial-gradient(circle at top left, rgba(106, 176, 76, 0.18), transparent 70%);
		opacity: 0.9;
		z-index: 0;
	}

	.server-header::after {
		content: '';
		position: absolute;
		inset: 20% -10% -30% 50%;
		background: radial-gradient(circle at top right, rgba(96, 141, 255, 0.18), transparent 70%);
		opacity: 0.8;
		z-index: 0;
	}

	.tabs {
		display: flex;
		gap: 4px;
		border-bottom: 1px solid #2a2f47;
		overflow-x: auto;
		scroll-snap-type: x proximity;
	}

	.tab {
		padding: 12px 20px;
		color: #8890b1;
		text-decoration: none;
		border-bottom: 2px solid transparent;
		transition: all 0.2s;
		font-size: 14px;
		font-weight: 500;
		white-space: nowrap;
		scroll-snap-align: start;
	}

	.tab:hover {
		color: #aab2d3;
	}

	.tab.active {
		color: var(--mc-grass);
		border-bottom-color: var(--mc-grass);
	}

	.tab.disabled {
		color: #4a5070;
		cursor: not-allowed;
		pointer-events: auto;
	}

	.content {
		flex: 1;
	}

	@media (max-width: 640px) {
		.server-header {
			flex-direction: column;
			align-items: flex-start;
		}

		.server-icon {
			width: 100%;
			justify-content: center;
		}

		.server-side {
			width: 100%;
			justify-content: space-between;
			margin-left: 0;
		}

		.tabs {
			overflow-x: scroll;
		}
	}

	@media (max-width: 900px) {
		.tabs {
			gap: 2px;
		}

		.tab {
			padding: 10px 14px;
			font-size: 13px;
		}
	}

	.rename-btn {
		display: inline-flex;
		align-items: center;
		justify-content: center;
		width: 30px;
		height: 30px;
		padding: 0;
		border-radius: 8px;
		border: 1px solid transparent;
		background: transparent;
		color: #8890b1;
		font-size: 15px;
		cursor: pointer;
		transition: all 0.2s;
	}
	.rename-btn:hover {
		color: var(--mc-grass);
		border-color: rgba(106, 176, 76, 0.4);
		background: rgba(106, 176, 76, 0.12);
	}
	.backend-name {
		font-size: 12px;
		color: #6d7597;
		margin-top: -6px;
	}
	.backend-name code {
		color: #8890b1;
		font-size: 11px;
		padding: 1px 6px;
		border-radius: 5px;
		background: rgba(19, 24, 40, 0.8);
		border: 1px solid rgba(62, 69, 100, 0.6);
	}
	.rename-backdrop {
		position: fixed;
		inset: 0;
		z-index: 200;
		display: flex;
		align-items: center;
		justify-content: center;
		padding: 24px;
		background: rgba(4, 6, 14, 0.72);
		backdrop-filter: blur(3px);
	}
	.rename-modal {
		width: min(420px, 100%);
		padding: 24px;
		border-radius: 16px;
		background: linear-gradient(135deg, rgba(22, 27, 46, 0.98), rgba(10, 14, 24, 0.98));
		border: 1px solid rgba(42, 47, 71, 0.9);
		box-shadow: 0 24px 48px rgba(0, 0, 0, 0.5);
		display: flex;
		flex-direction: column;
		gap: 14px;
	}
	.rename-modal h2 {
		margin: 0;
		font-size: 18px;
		font-weight: 700;
	}
	.rename-hint {
		margin: 0;
		font-size: 13px;
		line-height: 1.5;
		color: #8890b1;
	}
	.rename-modal input {
		width: 100%;
		padding: 10px 14px;
		border-radius: 10px;
		border: 1px solid rgba(62, 69, 100, 0.8);
		background: rgba(19, 24, 40, 0.9);
		color: #eef0f8;
		font-size: 14px;
		box-sizing: border-box;
	}
	.rename-modal input:focus {
		outline: none;
		border-color: var(--mc-grass);
	}
	.rename-error {
		margin: 0;
		font-size: 13px;
		color: #ff8f8f;
	}
	.rename-actions {
		display: flex;
		justify-content: flex-end;
		gap: 10px;
		margin-top: 4px;
	}
	.rename-actions .btn-secondary {
		padding: 9px 18px;
		border-radius: 10px;
		border: 1px solid rgba(62, 69, 100, 0.8);
		background: rgba(19, 24, 40, 0.9);
		color: #cdd3ee;
		font-size: 13px;
		font-weight: 600;
		cursor: pointer;
	}
	.rename-actions .btn-primary {
		padding: 9px 18px;
		border-radius: 10px;
		border: 1px solid transparent;
		background: var(--mc-grass);
		color: #0c1206;
		font-size: 13px;
		font-weight: 700;
		cursor: pointer;
	}
	.rename-actions .btn-primary:disabled,
	.rename-actions .btn-secondary:disabled {
		opacity: 0.6;
		cursor: not-allowed;
	}
</style>
