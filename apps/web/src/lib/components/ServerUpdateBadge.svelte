<script lang="ts">
	import { getServerUpdates, setUpdateMode, applyServerUpdate } from '$lib/api/client';
	import type { ServerUpdateStatus } from '$lib/api/types';

	let { serverName, running = false }: { serverName: string; running?: boolean } = $props();

	let status = $state<ServerUpdateStatus | null>(null);
	let show = $state(false);
	let showJumpOptions = $state(false);
	let applyingProfileId = $state<string | null>(null);
	let applyError = $state<string | null>(null);
	let appliedJar = $state<string | null>(null);
	let modeError = $state<string | null>(null);

	$effect(() => {
		const name = serverName;
		if (!name) return;
		appliedJar = null;
		getServerUpdates(fetch, name).then((result) => {
			status = result.data;
		});
	});

	async function chooseMode(mode: 'notify' | 'ignore-current' | 'off') {
		if (!serverName || mode === status?.mode) return;
		modeError = null;
		const result = await setUpdateMode(fetch, serverName, mode);
		if (result.error) {
			modeError = result.error;
			return;
		}
		status = result.data ?? status;
		await refresh();
	}

	async function refresh() {
		const result = await getServerUpdates(fetch, serverName);
		status = result.data;
	}

	async function apply(profileId: string | null | undefined) {
		if (!profileId || !serverName || applyingProfileId) return;
		applyingProfileId = profileId;
		applyError = null;

		const result = await applyServerUpdate(fetch, serverName, profileId);
		applyingProfileId = null;

		if (result.error) {
			applyError = result.error;
			return;
		}

		appliedJar = result.data?.newJar ?? profileId;
		showJumpOptions = false;
		await refresh();
	}

	function close() {
		show = false;
		applyError = null;
		modeError = null;
	}

	function handleKeydown(e: KeyboardEvent) {
		if (show && e.key === 'Escape') {
			close();
		}
	}

	const currentLabel = $derived(
		status?.currentVersion
			? status.currentBuild != null
				? `${status.currentVersion} build ${status.currentBuild}`
				: status.currentVersion
			: null
	);
</script>

<svelte:window onkeydown={handleKeydown} />

{#if status?.supported && status?.updateAvailable}
	<button class="update-chip" onclick={() => (show = true)} title="A newer version of this server's software is available">
		<span class="chip-dot"></span>
		Update available
	</button>
{/if}

{#if show}
	<!-- svelte-ignore a11y_no_static_element_interactions, a11y_click_events_have_key_events -->
	<div class="backdrop" onclick={(e) => e.target === e.currentTarget && close()} role="presentation">
		<div class="modal" role="dialog" aria-modal="true" aria-label="Server updates">
			<h2>Server updates</h2>

			{#if appliedJar}
				<div class="success">
					Updated to <strong>{appliedJar}</strong>. Restart this server to start using it.
				</div>
			{/if}

			<p class="installed">
				{#if currentLabel}
					Installed: <strong>{currentLabel}</strong>
					{#if status?.family}<span class="family">({status.family})</span>{/if}
				{:else}
					Could not identify the installed software.
				{/if}
			</p>

			{#if status?.updateAvailable && status?.latestBuildProfileId}
				<div class="offer">
					<button
						class="btn-primary"
						onclick={() => apply(status?.latestBuildProfileId)}
						disabled={running || applyingProfileId !== null}
					>
						{applyingProfileId === status.latestBuildProfileId ? 'Updating…' : `Update to build ${status.latestBuildNumber}`}
					</button>
					<p class="hint">Same Minecraft version ({status.latestBuildVersion}) — safe for worlds and plugins.</p>
				</div>
			{:else if !status?.updateAvailable && !status?.jumpAvailable}
				<p class="uptodate">You're up to date.</p>
			{/if}

			{#if status?.jumpAvailable}
				<div class="jump">
					<button class="jump-toggle" onclick={() => (showJumpOptions = !showJumpOptions)}>
						{showJumpOptions ? '▾' : '▸'} Newer Minecraft available: {status?.jumpVersion}
					</button>
					{#if showJumpOptions}
						<p class="warning">
							A Minecraft version change can break plugins and mods and may upgrade your world format
							(one-way). Back up first — and check your plugins:
							{#if status?.family === 'paper'}
								see the <a href={`/servers/${serverName}/plugins`}>Plugins tab</a>.
							{:else}
								this server does not use plugins.
							{/if}
						</p>
						<button
							class="btn-secondary danger"
							onclick={() => apply(status?.jumpProfileId)}
							disabled={running || applyingProfileId !== null}
						>
							{applyingProfileId === status.jumpProfileId ? 'Updating…' : `Update to ${status.jumpVersion}`}
						</button>
					{/if}
				</div>
			{/if}

			{#if running}
				<p class="hint blocked">Stop this server before updating.</p>
			{/if}
			{#if applyError}
				<p class="error">{applyError}</p>
			{/if}

			<div class="mode-block">
				<span class="mode-label">Notify me about updates</span>
				<div class="mode-options">
					<label><input type="radio" name="upd-mode" checked={status?.mode === 'notify'} onchange={() => chooseMode('notify')} /> Always</label>
					<label><input type="radio" name="upd-mode" checked={status?.mode === 'ignore-current'} onchange={() => chooseMode('ignore-current')} /> Ignore this update</label>
					<label><input type="radio" name="upd-mode" checked={status?.mode === 'off'} onchange={() => chooseMode('off')} /> Never</label>
				</div>
				{#if modeError}
					<p class="error">{modeError}</p>
				{/if}
			</div>

			<div class="actions">
				<button class="btn-secondary" onclick={close}>Close</button>
			</div>
		</div>
	</div>
{/if}

<style>
	.update-chip {
		display: inline-flex;
		align-items: center;
		gap: 7px;
		padding: 5px 14px;
		border-radius: 999px;
		border: 1px solid rgba(230, 170, 60, 0.45);
		background: rgba(230, 170, 60, 0.14);
		color: #ffd9a0;
		font-size: 12px;
		font-weight: 700;
		cursor: pointer;
		transition: background 0.2s;
	}

	.update-chip:hover {
		background: rgba(230, 170, 60, 0.24);
	}

	.chip-dot {
		width: 8px;
		height: 8px;
		border-radius: 50%;
		background: #e6aa3c;
		box-shadow: 0 0 8px rgba(230, 170, 60, 0.8);
	}

	.backdrop {
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

	.modal {
		width: min(480px, 100%);
		padding: 24px;
		border-radius: 16px;
		background: linear-gradient(135deg, rgba(22, 27, 46, 0.98), rgba(10, 14, 24, 0.98));
		border: 1px solid rgba(42, 47, 71, 0.9);
		box-shadow: 0 24px 48px rgba(0, 0, 0, 0.5);
		display: flex;
		flex-direction: column;
		gap: 16px;
		max-height: 85vh;
		overflow-y: auto;
	}

	.modal h2 {
		margin: 0;
		font-size: 18px;
		font-weight: 700;
	}

	.installed {
		margin: 0;
		font-size: 13px;
		color: #aab2d3;
	}

	.installed strong {
		color: #eef0f8;
	}

	.family {
		color: #6d7597;
		margin-left: 4px;
	}

	.offer,
	.jump {
		display: flex;
		flex-direction: column;
		gap: 8px;
	}

	.jump-toggle {
		padding: 0;
		border: none;
		background: none;
		color: #8890b1;
		font-size: 13px;
		text-align: left;
		cursor: pointer;
	}

	.jump-toggle:hover {
		color: #aab2d3;
	}

	.warning {
		margin: 0;
		font-size: 12.5px;
		line-height: 1.5;
		color: #ffc98f;
		background: rgba(230, 140, 60, 0.1);
		border: 1px solid rgba(230, 140, 60, 0.35);
		border-radius: 10px;
		padding: 10px 12px;
	}

	.warning a {
		color: #ffdfae;
	}

	.hint {
		margin: 0;
		font-size: 12px;
		color: #8890b1;
	}

	.hint.blocked {
		color: #ffb3b3;
	}

	.success {
		font-size: 13px;
		line-height: 1.5;
		color: #c9ecb8;
		background: rgba(106, 176, 76, 0.15);
		border: 1px solid rgba(106, 176, 76, 0.4);
		border-radius: 10px;
		padding: 10px 12px;
	}

	.uptodate {
		margin: 0;
		font-size: 13px;
		color: #9fd18b;
	}

	.error {
		margin: 0;
		font-size: 13px;
		color: #ff8f8f;
	}

	.mode-block {
		display: flex;
		flex-direction: column;
		gap: 8px;
		border-top: 1px solid rgba(42, 47, 71, 0.9);
		padding-top: 14px;
	}

	.mode-label {
		font-size: 11px;
		font-weight: 700;
		text-transform: uppercase;
		letter-spacing: 0.07em;
		color: #9aa6d1;
	}

	.mode-options {
		display: flex;
		gap: 16px;
		flex-wrap: wrap;
	}

	.mode-options label {
		display: inline-flex;
		align-items: center;
		gap: 6px;
		font-size: 13px;
		color: #cdd3ee;
		cursor: pointer;
	}

	.actions {
		display: flex;
		justify-content: flex-end;
	}

	.btn-primary {
		align-self: flex-start;
		padding: 9px 18px;
		border-radius: 10px;
		border: 1px solid transparent;
		background: var(--mc-grass, #6ab04c);
		color: #0c1206;
		font-size: 13px;
		font-weight: 700;
		cursor: pointer;
	}

	.btn-secondary {
		padding: 9px 18px;
		border-radius: 10px;
		border: 1px solid rgba(62, 69, 100, 0.8);
		background: rgba(19, 24, 40, 0.9);
		color: #cdd3ee;
		font-size: 13px;
		font-weight: 600;
		cursor: pointer;
	}

	.btn-secondary.danger {
		align-self: flex-start;
		border-color: rgba(230, 140, 60, 0.5);
		color: #ffd9a0;
	}

	.btn-primary:disabled,
	.btn-secondary:disabled {
		opacity: 0.55;
		cursor: not-allowed;
	}
</style>
