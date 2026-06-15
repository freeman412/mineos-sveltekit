<script lang="ts">
	import * as api from '$lib/api/client';
	import type { NeoForgeVersion, ForgeVersion } from '$lib/api/types';
	import InstallProgress from './InstallProgress.svelte';

	interface Props {
		serverName: string;
		/** Detected loader key: forge | neoforge | fabric | quilt */
		loader: string;
		/** Currently installed loader build, if known (e.g. "21.1.227") */
		currentVersion: string | null;
		/** The server's Minecraft version, if known from the running server (e.g. "1.21.1") */
		currentMcVersion: string | null;
		/** Whether the server is currently running (updates require it stopped) */
		isServerRunning: boolean;
		/** Display name for the loader, e.g. "NeoForge" */
		loaderName: string;
		/** Called once an update completes so the parent can re-detect the version */
		onUpdated?: () => void;
	}

	let {
		serverName,
		loader,
		currentVersion,
		currentMcVersion,
		isServerRunning,
		loaderName,
		onUpdated
	}: Props = $props();

	const supported = $derived(['forge', 'neoforge', 'fabric', 'quilt'].includes(loader));
	/** NeoForge/Forge builds are Minecraft-version-specific; Fabric/Quilt loaders are not. */
	const mcSpecific = $derived(loader === 'forge' || loader === 'neoforge');

	type Build = { version: string; isLatest?: boolean; isRecommended?: boolean };

	let expanded = $state(false);
	let loading = $state(false);
	let error = $state('');

	/** All Minecraft versions this loader offers (for the optional MC switcher). */
	let mcVersions = $state<string[]>([]);
	/** The resolved Minecraft version we install against. */
	let mcVersion = $state<string | null>(null);
	let builds = $state<Build[]>([]);
	let selected = $state<string | null>(null);
	let changingMc = $state(false);

	let streamUrl = $state<string | null>(null);
	let installing = $state(false);
	let done = $state(false);

	// Cache of the full version list so switching MC doesn't refetch.
	let allNeoForge: NeoForgeVersion[] = [];
	let allForge: ForgeVersion[] = [];
	let allLoaderVersions: Build[] = [];

	/** Best-effort Minecraft version from a NeoForge build, e.g. "21.1.227" -> "1.21.1". */
	function mcFromNeoForge(build: string): string | null {
		const p = build.split('.');
		if (p.length < 2) return null;
		return p[1] === '0' ? `1.${p[0]}` : `1.${p[0]}.${p[1]}`;
	}

	function versionDesc(a: string, b: string): number {
		const pa = a.split(/[^0-9]+/).filter(Boolean).map(Number);
		const pb = b.split(/[^0-9]+/).filter(Boolean).map(Number);
		for (let i = 0; i < Math.max(pa.length, pb.length); i++) {
			if ((pb[i] ?? 0) !== (pa[i] ?? 0)) return (pb[i] ?? 0) - (pa[i] ?? 0);
		}
		return 0;
	}

	function rebuildForMc() {
		selected = null;
		if (loader === 'neoforge') {
			builds = allNeoForge
				.filter((v) => v.minecraftVersion === mcVersion)
				.map((v) => ({ version: v.neoForgeVersion, isLatest: v.isLatest }))
				.sort((x, y) => versionDesc(x.version, y.version));
		} else if (loader === 'forge') {
			builds = allForge
				.filter((v) => v.minecraftVersion === mcVersion)
				.map((v) => ({ version: v.forgeVersion, isLatest: v.isLatest, isRecommended: v.isRecommended }))
				.sort((x, y) => versionDesc(x.version, y.version));
		} else {
			builds = allLoaderVersions;
		}
	}

	async function open() {
		expanded = true;
		loading = true;
		error = '';
		try {
			if (loader === 'neoforge') {
				const r = await api.getNeoForgeVersions(fetch);
				if (r.error) throw new Error(r.error);
				allNeoForge = r.data ?? [];
				mcVersions = [...new Set(allNeoForge.map((v) => v.minecraftVersion))].sort(versionDesc);
				mcVersion =
					currentMcVersion ??
					allNeoForge.find((v) => v.neoForgeVersion === currentVersion)?.minecraftVersion ??
					(currentVersion ? mcFromNeoForge(currentVersion) : null) ??
					mcVersions[0] ??
					null;
			} else if (loader === 'forge') {
				const r = await api.getForgeVersions(fetch);
				if (r.error) throw new Error(r.error);
				allForge = r.data ?? [];
				mcVersions = [...new Set(allForge.map((v) => v.minecraftVersion))].sort(versionDesc);
				mcVersion =
					currentMcVersion ??
					allForge.find((v) => v.forgeVersion === currentVersion)?.minecraftVersion ??
					(currentVersion ? currentVersion.split('-')[0] : null) ??
					mcVersions[0] ??
					null;
			} else if (loader === 'fabric') {
				const [g, l] = await Promise.all([
					api.getFabricGameVersions(fetch),
					api.getFabricLoaderVersions(fetch)
				]);
				if (l.error) throw new Error(l.error);
				allLoaderVersions = (l.data ?? []).filter((v) => v.isStable).map((v) => ({ version: v.version }));
				mcVersions = (g.data ?? []).filter((v) => v.isStable).map((v) => v.version);
				mcVersion = currentMcVersion ?? mcVersions[0] ?? null;
			} else if (loader === 'quilt') {
				const [g, l] = await Promise.all([
					api.getQuiltGameVersions(fetch),
					api.getQuiltLoaderVersions(fetch)
				]);
				if (l.error) throw new Error(l.error);
				allLoaderVersions = (l.data ?? []).filter((v) => v.isStable).map((v) => ({ version: v.version }));
				mcVersions = (g.data ?? []).filter((v) => v.isStable).map((v) => v.version);
				mcVersion = currentMcVersion ?? mcVersions[0] ?? null;
			}
			rebuildForMc();
		} catch (e) {
			error = e instanceof Error ? e.message : 'Failed to load versions.';
		} finally {
			loading = false;
		}
	}

	function reset() {
		expanded = false;
		changingMc = false;
		selected = null;
		streamUrl = null;
		installing = false;
		error = '';
		done = false;
		builds = [];
	}

	async function startUpdate() {
		if (!selected || !mcVersion || isServerRunning) return;
		error = '';
		installing = true;
		try {
			let installId: string | null = null;
			if (loader === 'forge') {
				const r = await api.installForge(fetch, mcVersion, selected, serverName);
				if (r.error) throw new Error(r.error);
				installId = r.data?.installId ?? null;
				if (installId) streamUrl = `/api/forge/install/${installId}/stream`;
			} else if (loader === 'neoforge') {
				const r = await api.installNeoForge(fetch, mcVersion, selected, serverName);
				if (r.error) throw new Error(r.error);
				installId = r.data?.installId ?? null;
				if (installId) streamUrl = `/api/neoforge/install/${installId}/stream`;
			} else if (loader === 'fabric') {
				const r = await api.installFabric(fetch, mcVersion, selected, serverName);
				if (r.error) throw new Error(r.error);
				installId = r.data?.installId ?? null;
				if (installId) streamUrl = `/api/fabric/install/${installId}/stream`;
			} else if (loader === 'quilt') {
				const r = await api.installQuilt(fetch, mcVersion, selected, serverName);
				if (r.error) throw new Error(r.error);
				installId = r.data?.installId ?? null;
				if (installId) streamUrl = `/api/quilt/install/${installId}/stream`;
			}
			if (!streamUrl) throw new Error('Failed to start the update.');
		} catch (e) {
			error = e instanceof Error ? e.message : 'Failed to start the update.';
			installing = false;
		}
	}

	function handleComplete() {
		done = true;
		installing = false;
		onUpdated?.();
	}
</script>

{#if supported}
	<div class="loader-version-manager">
		{#if streamUrl}
			<InstallProgress
				{streamUrl}
				label={`Updating ${loaderName} to ${selected ?? ''}`}
				oncomplete={handleComplete}
				onerror={(e) => {
					error = e;
					installing = false;
				}}
			/>
			{#if done}
				<p class="done-note">
					Updated to {loaderName} {selected}. Start the server to apply the change.
				</p>
				<button class="btn-secondary" onclick={reset}>Done</button>
			{/if}
			{#if error}
				<button class="btn-secondary" onclick={reset}>Close</button>
			{/if}
		{:else if !expanded}
			<button class="btn-change" onclick={open} title="Upgrade or downgrade the mod loader version">
				Change version
			</button>
		{:else}
			<div class="picker-panel">
				<div class="picker-head">
					<div class="picker-title">
						<span>Choose a {loaderName} version</span>
						{#if mcVersion}
							<span class="mc-line">
								for Minecraft <strong>{mcVersion}</strong>
								{#if mcSpecific}
									<button class="btn-link inline" onclick={() => (changingMc = !changingMc)}>
										{changingMc ? 'keep' : 'change'}
									</button>
								{/if}
							</span>
						{/if}
					</div>
					<button class="btn-link" onclick={reset}>Cancel</button>
				</div>

				{#if changingMc && mcSpecific}
					<label class="mc-switch">
						Minecraft version
						<select
							value={mcVersion}
							onchange={(e) => {
								mcVersion = (e.currentTarget as HTMLSelectElement).value;
								rebuildForMc();
							}}
						>
							{#each mcVersions as mc}
								<option value={mc}>{mc}</option>
							{/each}
						</select>
					</label>
				{/if}

				{#if isServerRunning}
					<p class="warn">Stop the server before changing the mod loader version.</p>
				{/if}

				{#if loading}
					<p class="muted">Loading versions…</p>
				{:else if error}
					<p class="warn">{error}</p>
				{:else if builds.length === 0}
					<p class="muted">No versions available for this Minecraft version.</p>
				{:else}
					<div class="build-list">
						{#each builds as b}
							<button
								class="build-row"
								class:selected={selected === b.version}
								class:current={currentVersion === b.version}
								onclick={() => (selected = b.version)}
							>
								<span class="build-ver">{b.version}</span>
								<span class="badges">
									{#if currentVersion === b.version}<span class="badge current-badge">current</span>{/if}
									{#if b.isLatest}<span class="badge">latest</span>{/if}
									{#if b.isRecommended}<span class="badge rec">recommended</span>{/if}
								</span>
							</button>
						{/each}
					</div>
				{/if}

				<div class="picker-actions">
					<button
						class="btn-update"
						disabled={!selected || installing || isServerRunning || selected === currentVersion}
						onclick={startUpdate}
					>
						{#if installing}
							Starting…
						{:else if selected && selected === currentVersion}
							Already installed
						{:else if selected}
							Update to {selected}
						{:else}
							Select a version
						{/if}
					</button>
				</div>
			</div>
		{/if}
	</div>
{/if}

<style>
	.loader-version-manager {
		display: flex;
		flex-direction: column;
		gap: 12px;
		width: 100%;
	}

	.btn-change {
		background: #2b2f45;
		color: #d4d9f1;
		border: 1px solid #3a3f5a;
		border-radius: 8px;
		padding: 6px 14px;
		font-family: inherit;
		font-size: 13px;
		font-weight: 600;
		cursor: pointer;
		align-self: flex-start;
	}

	.btn-change:hover {
		background: #353a55;
	}

	.picker-panel {
		display: flex;
		flex-direction: column;
		gap: 12px;
		background: #0d0f16;
		border: 1px solid #2a2f47;
		border-radius: 12px;
		padding: 16px;
		width: 100%;
		box-sizing: border-box;
	}

	.picker-head {
		display: flex;
		justify-content: space-between;
		align-items: flex-start;
		gap: 12px;
	}

	.picker-title {
		display: flex;
		flex-direction: column;
		gap: 2px;
		font-size: 14px;
		color: #d4d9f1;
		font-weight: 600;
	}

	.mc-line {
		font-size: 13px;
		font-weight: 400;
		color: #9aa2c5;
	}

	.mc-line strong {
		color: #d4d9f1;
	}

	.mc-switch {
		display: flex;
		align-items: center;
		gap: 10px;
		font-size: 13px;
		color: #9aa2c5;
	}

	.mc-switch select {
		background: #141827;
		border: 1px solid #2a2f47;
		border-radius: 6px;
		padding: 6px 10px;
		color: #eef0f8;
		font-family: inherit;
		font-size: 13px;
		cursor: pointer;
	}

	.build-list {
		display: flex;
		flex-direction: column;
		gap: 4px;
		max-height: 320px;
		overflow-y: auto;
		padding-right: 4px;
		scrollbar-gutter: stable;
	}

	.build-row {
		display: flex;
		justify-content: space-between;
		align-items: center;
		gap: 12px;
		background: #141827;
		border: 1px solid transparent;
		border-radius: 8px;
		padding: 10px 14px;
		font-family: inherit;
		font-size: 14px;
		color: #eef0f8;
		cursor: pointer;
		text-align: left;
		width: 100%;
		box-sizing: border-box;
	}

	.build-row:hover {
		background: #1a1f33;
	}

	.build-row.selected {
		border-color: var(--mc-grass);
		background: rgba(106, 176, 76, 0.12);
	}

	.build-row.current {
		opacity: 0.85;
	}

	.build-ver {
		font-variant-numeric: tabular-nums;
	}

	.badges {
		display: flex;
		gap: 6px;
	}

	.badge {
		font-size: 11px;
		padding: 2px 8px;
		border-radius: 4px;
		font-weight: 600;
		background: rgba(255, 255, 255, 0.08);
		color: #9aa2c5;
	}

	.badge.current-badge {
		background: rgba(90, 107, 255, 0.2);
		color: #a4b0ff;
	}

	.badge.rec {
		background: rgba(106, 176, 76, 0.2);
		color: #9ee6a8;
	}

	.picker-actions {
		display: flex;
		justify-content: flex-end;
	}

	.btn-update {
		background: var(--mc-grass);
		color: white;
		border: none;
		border-radius: 8px;
		padding: 10px 20px;
		font-family: inherit;
		font-size: 14px;
		font-weight: 600;
		cursor: pointer;
	}

	.btn-update:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.btn-link {
		background: none;
		border: none;
		color: #8890b1;
		font-family: inherit;
		font-size: 13px;
		cursor: pointer;
		text-decoration: underline;
		text-underline-offset: 3px;
	}

	.btn-link.inline {
		padding: 0 0 0 4px;
	}

	.btn-secondary {
		align-self: flex-start;
		background: #2b2f45;
		color: #d4d9f1;
		border: none;
		border-radius: 8px;
		padding: 8px 16px;
		font-family: inherit;
		font-size: 13px;
		font-weight: 600;
		cursor: pointer;
	}

	.warn {
		margin: 0;
		color: #ffb454;
		font-size: 13px;
	}

	.muted {
		margin: 0;
		color: #8890b1;
		font-size: 13px;
	}

	.done-note {
		margin: 0;
		color: #7ae68d;
		font-size: 13px;
	}
</style>
