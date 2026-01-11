<script lang="ts">
	import { invalidateAll } from '$app/navigation';
	import * as api from '$lib/api/client';
	import type { PageData } from './$types';
	import type { PlayerSummary } from '$lib/api/types';

	let { data }: { data: PageData } = $props();

	let query = $state('');
	let working = $state<string | null>(null);
	let statsOpen = $state(false);
	let statsLoading = $state(false);
	let statsError = $state<string | null>(null);
	let statsJson = $state('');
	let statsPlayer = $state<PlayerSummary | null>(null);

	const filteredPlayers = $derived(() => {
		const list = data.players.data ?? [];
		const needle = query.trim().toLowerCase();
		if (!needle) return list;
		return list.filter(
			(player) =>
				player.name.toLowerCase().includes(needle) || player.uuid.toLowerCase().includes(needle)
		);
	});

	function formatLastSeen(value: string | null) {
		if (!value) return 'Unknown';
		return new Date(value).toLocaleString();
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

	async function handleWhitelist(player: PlayerSummary) {
		working = actionKey(player.uuid, 'whitelist');
		try {
			const result = await api.whitelistPlayer(fetch, data.server.name, player.uuid, player.name);
			if (result.error) {
				alert(result.error);
			} else {
				await refresh();
			}
		} finally {
			working = null;
		}
	}

	async function handleRemoveWhitelist(player: PlayerSummary) {
		working = actionKey(player.uuid, 'unwhitelist');
		try {
			const result = await api.removeWhitelist(fetch, data.server.name, player.uuid);
			if (result.error) {
				alert(result.error);
			} else {
				await refresh();
			}
		} finally {
			working = null;
		}
	}

	async function handleOp(player: PlayerSummary) {
		working = actionKey(player.uuid, 'op');
		try {
			const result = await api.opPlayer(fetch, data.server.name, player.uuid, {
				name: player.name
			});
			if (result.error) {
				alert(result.error);
			} else {
				await refresh();
			}
		} finally {
			working = null;
		}
	}

	async function handleBan(player: PlayerSummary) {
		const reason = prompt(`Reason for banning ${player.name}?`, 'Banned by MineOS');
		if (reason === null) return;

		working = actionKey(player.uuid, 'ban');
		try {
			const result = await api.banPlayer(fetch, data.server.name, player.uuid, {
				name: player.name,
				reason
			});
			if (result.error) {
				alert(result.error);
			} else {
				await refresh();
			}
		} finally {
			working = null;
		}
	}

	async function openStats(player: PlayerSummary) {
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
				statsJson = result.data.rawJson;
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
</script>

<div class="players-page">
	<header class="page-header">
		<div>
			<h2>Player Management</h2>
			<p class="subtitle">Whitelist, op, and ban players for this server</p>
		</div>
		<div class="search">
			<input
				type="text"
				placeholder="Search players..."
				bind:value={query}
			/>
		</div>
	</header>

	{#if data.players.error}
		<p class="error-text">{data.players.error}</p>
	{:else if !data.players.data || data.players.data.length === 0}
		<div class="empty-state">
			<h3>No Players Yet</h3>
			<p>Players will appear after they join the server.</p>
		</div>
	{:else}
		<div class="player-card">
			<table>
				<thead>
					<tr>
						<th>Player</th>
						<th>Status</th>
						<th>Last Seen</th>
						<th>Play Time</th>
						<th>Actions</th>
					</tr>
				</thead>
				<tbody>
					{#each filteredPlayers as player}
						<tr>
							<td>
								<div class="player-name">
									<strong>{player.name}</strong>
									<span class="player-uuid">{player.uuid}</span>
								</div>
							</td>
							<td>
								<div class="badge-row">
									{#if player.whitelisted}
										<span class="badge">Whitelisted</span>
									{/if}
									{#if player.isOp}
										<span class="badge op">OP {player.opLevel ?? 4}</span>
									{/if}
									{#if player.banned}
										<span class="badge danger">Banned</span>
									{/if}
									{#if !player.whitelisted && !player.isOp && !player.banned}
										<span class="badge muted">Normal</span>
									{/if}
								</div>
							</td>
							<td>{formatLastSeen(player.lastSeen)}</td>
							<td>{formatPlaytime(player.playTimeSeconds)}</td>
							<td>
								<div class="actions">
									{#if player.whitelisted}
										<button
											class="btn secondary"
											disabled={working === actionKey(player.uuid, 'unwhitelist')}
											onclick={() => handleRemoveWhitelist(player)}
										>
											Unwhitelist
										</button>
									{:else}
										<button
											class="btn"
											disabled={working === actionKey(player.uuid, 'whitelist')}
											onclick={() => handleWhitelist(player)}
										>
											Whitelist
										</button>
									{/if}
									<button
										class="btn"
										disabled={player.isOp || working === actionKey(player.uuid, 'op')}
										onclick={() => handleOp(player)}
									>
										OP
									</button>
									<button
										class="btn danger"
										disabled={player.banned || working === actionKey(player.uuid, 'ban')}
										onclick={() => handleBan(player)}
									>
										Ban
									</button>
									<button class="btn secondary" onclick={() => openStats(player)}>
										Stats
									</button>
								</div>
							</td>
						</tr>
					{/each}
				</tbody>
			</table>
		</div>
	{/if}
</div>

{#if statsOpen}
	<div class="modal-backdrop" onclick={closeStats}>
		<div class="modal" onclick={(event) => event.stopPropagation()}>
			<header class="modal-header">
				<h3>Stats for {statsPlayer?.name ?? 'Player'}</h3>
				<button class="btn secondary" onclick={closeStats}>Close</button>
			</header>
			{#if statsLoading}
				<p class="muted">Loading stats...</p>
			{:else if statsError}
				<p class="error-text">{statsError}</p>
			{:else if statsJson}
				<pre class="stats-json">{statsJson}</pre>
			{:else}
				<p class="muted">No stats available.</p>
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
		align-items: center;
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

	.search input {
		background: #141827;
		border: 1px solid #2a2f47;
		border-radius: 10px;
		padding: 10px 14px;
		color: #eef0f8;
		min-width: 240px;
	}

	.player-card {
		background: #1a1e2f;
		border-radius: 16px;
		padding: 20px;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.35);
	}

	table {
		width: 100%;
		border-collapse: collapse;
	}

	th,
	td {
		padding: 12px 16px;
		text-align: left;
		border-bottom: 1px solid #2a2f47;
	}

	th {
		font-size: 12px;
		text-transform: uppercase;
		letter-spacing: 0.12em;
		color: #8890b1;
		font-weight: 600;
	}

	td {
		color: #eef0f8;
		font-size: 14px;
	}

	.player-name {
		display: flex;
		flex-direction: column;
		gap: 4px;
	}

	.player-uuid {
		font-size: 12px;
		color: #8890b1;
		font-family: 'Cascadia Code', monospace;
	}

	.badge-row {
		display: flex;
		gap: 6px;
		flex-wrap: wrap;
	}

	.badge {
		padding: 4px 8px;
		border-radius: 999px;
		background: rgba(106, 176, 76, 0.2);
		color: #b7f5a2;
		font-size: 11px;
		font-weight: 600;
	}

	.badge.op {
		background: rgba(111, 181, 255, 0.2);
		color: #a6d5fa;
	}

	.badge.danger {
		background: rgba(234, 85, 83, 0.2);
		color: #ff9a98;
	}

	.badge.muted {
		background: rgba(130, 140, 170, 0.2);
		color: #aab2d3;
	}

	.actions {
		display: flex;
		gap: 8px;
		flex-wrap: wrap;
	}

	.btn {
		background: rgba(106, 176, 76, 0.18);
		color: #b7f5a2;
		border: 1px solid rgba(106, 176, 76, 0.4);
		border-radius: 8px;
		padding: 6px 12px;
		font-size: 12px;
		font-weight: 600;
		cursor: pointer;
	}

	.btn.secondary {
		background: rgba(88, 96, 120, 0.3);
		color: #d4d9f1;
		border-color: rgba(88, 96, 120, 0.5);
	}

	.btn.danger {
		background: rgba(234, 85, 83, 0.2);
		color: #ff9a98;
		border-color: rgba(234, 85, 83, 0.5);
	}

	.btn:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.empty-state {
		text-align: center;
		padding: 64px 20px;
		background: linear-gradient(135deg, #1a1e2f 0%, #141827 100%);
		border-radius: 12px;
		border: 1px solid #2a2f47;
	}

	.empty-state h3 {
		margin: 0 0 12px;
		font-size: 20px;
		color: #eef0f8;
	}

	.empty-state p {
		margin: 0;
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
		background: rgba(6, 8, 12, 0.7);
		display: flex;
		align-items: center;
		justify-content: center;
		z-index: 50;
		padding: 24px;
	}

	.modal {
		background: #141827;
		border-radius: 16px;
		max-width: 800px;
		width: 100%;
		padding: 20px;
		border: 1px solid #2a2f47;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.4);
		display: flex;
		flex-direction: column;
		gap: 16px;
		max-height: 80vh;
		overflow: auto;
	}

	.modal-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 12px;
	}

	.stats-json {
		background: #0f1322;
		border-radius: 12px;
		padding: 16px;
		font-size: 12px;
		line-height: 1.5;
		color: #d4d9f1;
		white-space: pre-wrap;
		word-break: break-word;
	}

	.muted {
		color: #8890b1;
		margin: 0;
	}

	@media (max-width: 900px) {
		.actions {
			flex-direction: column;
			align-items: stretch;
		}

		.search input {
			min-width: 100%;
		}
	}

	@media (max-width: 720px) {
		table {
			display: block;
			overflow-x: auto;
		}
	}
</style>
