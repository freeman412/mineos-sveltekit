<script lang="ts">
	import { invalidateAll } from '$app/navigation';
	import * as api from '$lib/api/client';
	import { modal } from '$lib/stores/modal';
	import type { PageData } from './$types';
	import type { PlayerSummary, MojangProfile } from '$lib/api/types';

	let { data }: { data: PageData } = $props();

	let query = $state('');
	let working = $state<string | null>(null);

	// Stats modal state
	let statsOpen = $state(false);
	let statsLoading = $state(false);
	let statsError = $state<string | null>(null);
	let statsJson = $state('');
	let statsPlayer = $state<PlayerSummary | null>(null);

	// Add player modal state
	let addPlayerOpen = $state(false);
	let searchQuery = $state('');
	let searchLoading = $state(false);
	let searchResult = $state<MojangProfile | null>(null);
	let searchError = $state<string | null>(null);

	const filteredPlayers = $derived.by(() => {
		const list = data.players.data ?? [];
		const needle = query.trim().toLowerCase();
		if (!needle) return list;
		return list.filter(
			(player) =>
				player.name.toLowerCase().includes(needle) || player.uuid.toLowerCase().includes(needle)
		);
	});

	function getAvatarUrl(uuid: string) {
		return `https://mc-heads.net/avatar/${uuid}/48`;
	}

	function formatLastSeen(value: string | null) {
		if (!value) return 'Never';
		const date = new Date(value);
		const now = new Date();
		const diff = now.getTime() - date.getTime();
		const days = Math.floor(diff / (1000 * 60 * 60 * 24));

		if (days === 0) return 'Today';
		if (days === 1) return 'Yesterday';
		if (days < 7) return `${days} days ago`;
		if (days < 30) return `${Math.floor(days / 7)} weeks ago`;
		return date.toLocaleDateString();
	}

	function formatPlaytime(seconds: number | null) {
		if (!seconds || seconds <= 0) return '0h';
		const hours = Math.floor(seconds / 3600);
		const minutes = Math.floor((seconds % 3600) / 60);
		if (hours <= 0) return `${minutes}m`;
		return `${hours}h ${minutes}m`;
	}

	function actionKey(uuid: string, action: string) {
		return `${uuid}:${action}`;
	}

	async function refresh() {
		await invalidateAll();
	}

	async function handleWhitelist(player: PlayerSummary | MojangProfile) {
		if (!data.server) return;
		working = actionKey(player.uuid, 'whitelist');
		try {
			const result = await api.whitelistPlayer(fetch, data.server.name, player.uuid, player.name);
			if (result.error) {
				await modal.error(result.error);
			} else {
				await refresh();
				closeAddPlayer();
			}
		} finally {
			working = null;
		}
	}

	async function handleRemoveWhitelist(player: PlayerSummary) {
		if (!data.server) return;
		working = actionKey(player.uuid, 'unwhitelist');
		try {
			const result = await api.removeWhitelist(fetch, data.server.name, player.uuid);
			if (result.error) {
				await modal.error(result.error);
			} else {
				await refresh();
			}
		} finally {
			working = null;
		}
	}

	async function handleOp(player: PlayerSummary | MojangProfile) {
		if (!data.server) return;
		working = actionKey(player.uuid, 'op');
		try {
			const result = await api.opPlayer(fetch, data.server.name, player.uuid, {
				name: player.name
			});
			if (result.error) {
				await modal.error(result.error);
			} else {
				await refresh();
				closeAddPlayer();
			}
		} finally {
			working = null;
		}
	}

	async function handleDeop(player: PlayerSummary) {
		if (!data.server) return;
		working = actionKey(player.uuid, 'deop');
		try {
			const result = await api.deopPlayer(fetch, data.server.name, player.uuid);
			if (result.error) {
				await modal.error(result.error);
			} else {
				await refresh();
			}
		} finally {
			working = null;
		}
	}

	async function handleBan(player: PlayerSummary) {
		if (!data.server) return;
		const reason = prompt(`Reason for banning ${player.name}?`, 'Banned by MineOS');
		if (reason === null) return;

		working = actionKey(player.uuid, 'ban');
		try {
			const result = await api.banPlayer(fetch, data.server.name, player.uuid, {
				name: player.name,
				reason
			});
			if (result.error) {
				await modal.error(result.error);
			} else {
				await refresh();
			}
		} finally {
			working = null;
		}
	}

	async function handleUnban(player: PlayerSummary) {
		if (!data.server) return;
		working = actionKey(player.uuid, 'unban');
		try {
			const result = await api.unbanPlayer(fetch, data.server.name, player.uuid);
			if (result.error) {
				await modal.error(result.error);
			} else {
				await refresh();
			}
		} finally {
			working = null;
		}
	}

	async function openStats(player: PlayerSummary) {
		if (!data.server) return;
		statsOpen = true;
		statsLoading = true;
		statsError = null;
		statsJson = '';
		statsPlayer = player;
		try {
			const result = await api.getPlayerStats(fetch, data.server.name, player.uuid);
			if (result.error) {
				statsError = result.error;
			} else if (result.data) {
				statsJson = JSON.stringify(JSON.parse(result.data.rawJson), null, 2);
			}
		} catch (err) {
			statsError = err instanceof Error ? err.message : 'Failed to load stats';
		} finally {
			statsLoading = false;
		}
	}

	function closeStats() {
		statsOpen = false;
		statsError = null;
		statsJson = '';
		statsPlayer = null;
	}

	function openAddPlayer() {
		addPlayerOpen = true;
		searchQuery = '';
		searchResult = null;
		searchError = null;
	}

	function closeAddPlayer() {
		addPlayerOpen = false;
		searchQuery = '';
		searchResult = null;
		searchError = null;
	}

	async function searchPlayer() {
		if (!searchQuery.trim()) return;

		searchLoading = true;
		searchError = null;
		searchResult = null;

		try {
			const result = await api.lookupMojangPlayer(fetch, searchQuery.trim());
			if (result.error) {
				searchError = result.error;
			} else if (result.data) {
				searchResult = result.data;
			}
		} catch (err) {
			searchError = err instanceof Error ? err.message : 'Failed to search player';
		} finally {
			searchLoading = false;
		}
	}

	function handleSearchKeydown(event: KeyboardEvent) {
		if (event.key === 'Enter') {
			searchPlayer();
		}
	}
</script>

<div class="players-page">
	<header class="page-header">
		<div>
			<h2>Player Management</h2>
			<p class="subtitle">Manage whitelist, ops, and bans for this server</p>
		</div>
		<div class="header-actions">
			<input type="text" placeholder="Search players..." bind:value={query} class="search-input" />
			<button class="btn primary" onclick={openAddPlayer}>+ Add Player</button>
		</div>
	</header>

	{#if data.players.error}
		<p class="error-text">{data.players.error}</p>
	{:else if !data.players.data || data.players.data.length === 0}
		<div class="empty-state">
			<div class="empty-icon">&#x1F3AE;</div>
			<h3>No Players Yet</h3>
			<p>Players will appear here after they join the server, or you can add them manually.</p>
			<button class="btn primary" onclick={openAddPlayer}>+ Add Player</button>
		</div>
	{:else}
		<div class="player-grid">
			{#each filteredPlayers as player (player.uuid)}
				<div class="player-card" class:banned={player.banned}>
					<div class="player-info">
						<img
							src={getAvatarUrl(player.uuid)}
							alt={player.name}
							class="player-avatar"
							loading="lazy"
						/>
						<div class="player-details">
							<strong class="player-name">{player.name}</strong>
							<div class="player-meta">
								<span class="player-uuid">{player.uuid.slice(0, 8)}...</span>
								<span class="separator">|</span>
								<span>{formatLastSeen(player.lastSeen)}</span>
								<span class="separator">|</span>
								<span>{formatPlaytime(player.playTimeSeconds)}</span>
							</div>
							<div class="badge-row">
								{#if player.whitelisted}
									<span class="badge whitelist">Whitelisted</span>
								{/if}
								{#if player.isOp}
									<span class="badge op">OP {player.opLevel ?? 4}</span>
								{/if}
								{#if player.banned}
									<span class="badge banned">Banned</span>
								{/if}
							</div>
						</div>
					</div>
					<div class="player-actions">
						{#if player.banned}
							<button
								class="btn small"
								disabled={working === actionKey(player.uuid, 'unban')}
								onclick={() => handleUnban(player)}
							>
								Unban
							</button>
						{:else}
							{#if player.whitelisted}
								<button
									class="btn small secondary"
									disabled={working === actionKey(player.uuid, 'unwhitelist')}
									onclick={() => handleRemoveWhitelist(player)}
								>
									Remove
								</button>
							{:else}
								<button
									class="btn small"
									disabled={working === actionKey(player.uuid, 'whitelist')}
									onclick={() => handleWhitelist(player)}
								>
									Whitelist
								</button>
							{/if}
							{#if player.isOp}
								<button
									class="btn small secondary"
									disabled={working === actionKey(player.uuid, 'deop')}
									onclick={() => handleDeop(player)}
								>
									De-OP
								</button>
							{:else}
								<button
									class="btn small"
									disabled={working === actionKey(player.uuid, 'op')}
									onclick={() => handleOp(player)}
								>
									OP
								</button>
							{/if}
							<button
								class="btn small danger"
								disabled={working === actionKey(player.uuid, 'ban')}
								onclick={() => handleBan(player)}
							>
								Ban
							</button>
						{/if}
						<button class="btn small secondary" onclick={() => openStats(player)}>Stats</button>
					</div>
				</div>
			{/each}
		</div>
	{/if}
</div>

<!-- Stats Modal -->
{#if statsOpen}
	<!-- svelte-ignore a11y_no_static_element_interactions, a11y_click_events_have_key_events -->
	<div class="modal-backdrop" onclick={closeStats} role="presentation">
		<!-- svelte-ignore a11y_no_static_element_interactions, a11y_click_events_have_key_events, a11y_interactive_supports_focus -->
		<div class="modal" role="dialog" aria-modal="true" tabindex="-1" onclick={(event) => event.stopPropagation()}>
			<header class="modal-header">
				<div class="modal-title">
					{#if statsPlayer}
						<img
							src={getAvatarUrl(statsPlayer.uuid)}
							alt={statsPlayer.name}
							class="modal-avatar"
						/>
					{/if}
					<h3>Stats for {statsPlayer?.name ?? 'Player'}</h3>
				</div>
				<button class="btn secondary" onclick={closeStats}>Close</button>
			</header>
			{#if statsLoading}
				<p class="muted">Loading stats...</p>
			{:else if statsError}
				<p class="error-text">{statsError}</p>
			{:else if statsJson}
				<pre class="stats-json">{statsJson}</pre>
			{:else}
				<p class="muted">No stats available for this player.</p>
			{/if}
		</div>
	</div>
{/if}

<!-- Add Player Modal -->
{#if addPlayerOpen}
	<!-- svelte-ignore a11y_no_static_element_interactions, a11y_click_events_have_key_events -->
	<div class="modal-backdrop" onclick={closeAddPlayer} role="presentation">
		<!-- svelte-ignore a11y_no_static_element_interactions, a11y_click_events_have_key_events, a11y_interactive_supports_focus -->
		<div class="modal add-player-modal" role="dialog" aria-modal="true" tabindex="-1" onclick={(event) => event.stopPropagation()}>
			<header class="modal-header">
				<h3>Add Player</h3>
				<button class="btn secondary" onclick={closeAddPlayer}>Close</button>
			</header>

			<div class="search-section">
				<p class="search-hint">Search for a Minecraft player by username to add them to your server.</p>
				<div class="search-row">
					<input
						type="text"
						placeholder="Enter Minecraft username..."
						bind:value={searchQuery}
						onkeydown={handleSearchKeydown}
						class="search-input full"
					/>
					<button class="btn primary" onclick={searchPlayer} disabled={searchLoading || !searchQuery.trim()}>
						{searchLoading ? 'Searching...' : 'Search'}
					</button>
				</div>
			</div>

			{#if searchError}
				<div class="search-error">
					<p>{searchError}</p>
					<p class="hint">Make sure the username is spelled correctly and the player has a valid Minecraft account.</p>
				</div>
			{/if}

			{#if searchResult}
				<div class="search-result">
					<div class="result-player">
						<img src={searchResult.avatarUrl} alt={searchResult.name} class="result-avatar" />
						<div class="result-info">
							<strong>{searchResult.name}</strong>
							<span class="result-uuid">{searchResult.uuid}</span>
						</div>
					</div>
					<div class="result-actions">
						<button
							class="btn"
							onclick={() => handleWhitelist(searchResult!)}
							disabled={working !== null}
						>
							Add to Whitelist
						</button>
						<button
							class="btn"
							onclick={() => handleOp(searchResult!)}
							disabled={working !== null}
						>
							Add as OP
						</button>
					</div>
				</div>
			{/if}
		</div>
	</div>
{/if}

<style>
	.players-page {
		display: flex;
		flex-direction: column;
		gap: 24px;
	}

	.page-header {
		display: flex;
		justify-content: space-between;
		align-items: flex-start;
		gap: 24px;
		flex-wrap: wrap;
	}

	.page-header h2 {
		margin: 0 0 8px;
		font-size: 24px;
		font-weight: 600;
		color: #eef0f8;
	}

	.subtitle {
		margin: 0;
		color: #9aa2c5;
		font-size: 14px;
	}

	.header-actions {
		display: flex;
		gap: 12px;
		align-items: center;
	}

	.search-input {
		background: #141827;
		border: 1px solid #2a2f47;
		border-radius: 10px;
		padding: 10px 14px;
		color: #eef0f8;
		min-width: 200px;
	}

	.search-input.full {
		flex: 1;
	}

	.player-grid {
		display: flex;
		flex-direction: column;
		gap: 12px;
	}

	.player-card {
		background: #1a1e2f;
		border-radius: 14px;
		padding: 16px 20px;
		display: flex;
		justify-content: space-between;
		align-items: center;
		gap: 16px;
		border: 1px solid #2a2f47;
		transition: border-color 0.2s;
	}

	.player-card:hover {
		border-color: #3a4060;
	}

	.player-card.banned {
		border-color: rgba(234, 85, 83, 0.3);
		background: rgba(234, 85, 83, 0.05);
	}

	.player-info {
		display: flex;
		align-items: center;
		gap: 16px;
		min-width: 0;
	}

	.player-avatar {
		width: 48px;
		height: 48px;
		border-radius: 8px;
		flex-shrink: 0;
		image-rendering: pixelated;
	}

	.player-details {
		display: flex;
		flex-direction: column;
		gap: 4px;
		min-width: 0;
	}

	.player-name {
		font-size: 16px;
		color: #eef0f8;
	}

	.player-meta {
		display: flex;
		align-items: center;
		gap: 8px;
		font-size: 12px;
		color: #8890b1;
		flex-wrap: wrap;
	}

	.player-uuid {
		font-family: 'Cascadia Code', monospace;
	}

	.separator {
		opacity: 0.5;
	}

	.badge-row {
		display: flex;
		gap: 6px;
		flex-wrap: wrap;
		margin-top: 4px;
	}

	.badge {
		padding: 3px 8px;
		border-radius: 999px;
		font-size: 10px;
		font-weight: 600;
		text-transform: uppercase;
		letter-spacing: 0.05em;
	}

	.badge.whitelist {
		background: rgba(106, 176, 76, 0.2);
		color: #b7f5a2;
	}

	.badge.op {
		background: rgba(111, 181, 255, 0.2);
		color: #a6d5fa;
	}

	.badge.banned {
		background: rgba(234, 85, 83, 0.2);
		color: #ff9a98;
	}

	.player-actions {
		display: flex;
		gap: 8px;
		flex-shrink: 0;
		flex-wrap: wrap;
	}

	.btn {
		background: rgba(106, 176, 76, 0.18);
		color: #b7f5a2;
		border: 1px solid rgba(106, 176, 76, 0.4);
		border-radius: 8px;
		padding: 8px 16px;
		font-size: 13px;
		font-weight: 600;
		cursor: pointer;
		transition: all 0.15s;
	}

	.btn:hover:not(:disabled) {
		background: rgba(106, 176, 76, 0.28);
	}

	.btn.primary {
		background: rgba(106, 176, 76, 0.3);
	}

	.btn.small {
		padding: 6px 12px;
		font-size: 12px;
	}

	.btn.secondary {
		background: rgba(88, 96, 120, 0.3);
		color: #d4d9f1;
		border-color: rgba(88, 96, 120, 0.5);
	}

	.btn.secondary:hover:not(:disabled) {
		background: rgba(88, 96, 120, 0.45);
	}

	.btn.danger {
		background: rgba(234, 85, 83, 0.2);
		color: #ff9a98;
		border-color: rgba(234, 85, 83, 0.5);
	}

	.btn.danger:hover:not(:disabled) {
		background: rgba(234, 85, 83, 0.35);
	}

	.btn:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.empty-state {
		text-align: center;
		padding: 64px 20px;
		background: linear-gradient(135deg, #1a1e2f 0%, #141827 100%);
		border-radius: 16px;
		border: 1px solid #2a2f47;
	}

	.empty-icon {
		font-size: 48px;
		margin-bottom: 16px;
	}

	.empty-state h3 {
		margin: 0 0 12px;
		font-size: 20px;
		color: #eef0f8;
	}

	.empty-state p {
		margin: 0 0 24px;
		color: #9aa2c5;
		font-size: 14px;
	}

	.error-text {
		color: #ff9f9f;
		margin: 0;
	}

	.modal-backdrop {
		position: fixed;
		inset: 0;
		background: rgba(6, 8, 12, 0.8);
		display: flex;
		align-items: center;
		justify-content: center;
		z-index: 50;
		padding: 24px;
	}

	.modal {
		background: #141827;
		border-radius: 16px;
		max-width: 700px;
		width: 100%;
		padding: 24px;
		border: 1px solid #2a2f47;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.5);
		display: flex;
		flex-direction: column;
		gap: 20px;
		max-height: 85vh;
		overflow: auto;
	}

	.add-player-modal {
		max-width: 500px;
	}

	.modal-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 16px;
	}

	.modal-header h3 {
		margin: 0;
		font-size: 18px;
		color: #eef0f8;
	}

	.modal-title {
		display: flex;
		align-items: center;
		gap: 12px;
	}

	.modal-avatar {
		width: 32px;
		height: 32px;
		border-radius: 6px;
		image-rendering: pixelated;
	}

	.search-section {
		display: flex;
		flex-direction: column;
		gap: 12px;
	}

	.search-hint {
		margin: 0;
		color: #9aa2c5;
		font-size: 14px;
	}

	.search-row {
		display: flex;
		gap: 12px;
	}

	.search-error {
		background: rgba(234, 85, 83, 0.1);
		border: 1px solid rgba(234, 85, 83, 0.3);
		border-radius: 10px;
		padding: 16px;
	}

	.search-error p {
		margin: 0;
		color: #ff9a98;
	}

	.search-error .hint {
		margin-top: 8px;
		font-size: 13px;
		color: #9aa2c5;
	}

	.search-result {
		background: #1a1e2f;
		border-radius: 12px;
		padding: 16px;
		display: flex;
		flex-direction: column;
		gap: 16px;
	}

	.result-player {
		display: flex;
		align-items: center;
		gap: 16px;
	}

	.result-avatar {
		width: 64px;
		height: 64px;
		border-radius: 10px;
		image-rendering: pixelated;
	}

	.result-info {
		display: flex;
		flex-direction: column;
		gap: 4px;
	}

	.result-info strong {
		font-size: 18px;
		color: #eef0f8;
	}

	.result-uuid {
		font-size: 12px;
		color: #8890b1;
		font-family: 'Cascadia Code', monospace;
	}

	.result-actions {
		display: flex;
		gap: 12px;
	}

	.stats-json {
		background: #0f1322;
		border-radius: 12px;
		padding: 16px;
		font-size: 12px;
		line-height: 1.6;
		color: #d4d9f1;
		white-space: pre-wrap;
		word-break: break-word;
		max-height: 400px;
		overflow: auto;
	}

	.muted {
		color: #8890b1;
		margin: 0;
	}

	@media (max-width: 768px) {
		.page-header {
			flex-direction: column;
			align-items: stretch;
		}

		.header-actions {
			flex-direction: column;
		}

		.player-card {
			flex-direction: column;
			align-items: stretch;
		}

		.player-actions {
			justify-content: flex-start;
		}

		.search-row {
			flex-direction: column;
		}

		.result-actions {
			flex-direction: column;
		}
	}
</style>
