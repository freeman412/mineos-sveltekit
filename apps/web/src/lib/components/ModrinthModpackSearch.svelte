<script lang="ts">
	import { tick } from 'svelte';
	import { modal } from '$lib/stores/modal';
	import type {
		ModrinthSearchResult,
		ModrinthProjectHit,
		ModrinthProject,
		ModrinthVersion
	} from '$lib/api/types';
	import ProgressBar from './ProgressBar.svelte';

	interface Props {
		serverName: string;
		onInstallComplete?: () => void;
	}

	let { serverName, onInstallComplete }: Props = $props();

	let searchQuery = $state('');
	let searchResults = $state<ModrinthProjectHit[]>([]);
	let searchLoading = $state(false);
	let searchError = $state<string | null>(null);
	let pageIndex = 0;
	let pageSize = 20;
	let hasMore = true;
	let loadingMore = $state(false);
	let searchDebounce: ReturnType<typeof setTimeout> | null = null;

	let selectedLoader = $state('auto');
	let selectedVersion = $state('auto');

	let detailOpen = $state(false);
	let detailLoading = $state(false);
	let detailError = $state<string | null>(null);
	let detailProject = $state<ModrinthProject | null>(null);
	let detailVersions = $state<ModrinthVersion[]>([]);
	let installingVersionId = $state<string | null>(null);
	let installProgress = $state<{ status: string; percentage: number; message?: string } | null>(null);
	let installEventSource: EventSource | null = null;
	let outputEl: HTMLDivElement | null = null;

	const loaderOptions = [
		{ value: 'auto', label: 'Auto (server)' },
		{ value: 'forge', label: 'Forge' },
		{ value: 'fabric', label: 'Fabric' },
		{ value: 'quilt', label: 'Quilt' },
		{ value: 'neoforge', label: 'NeoForge' }
	];

	const commonMinecraftVersions = [
		'auto',
		'1.21.1',
		'1.21',
		'1.20.6',
		'1.20.4',
		'1.20.1',
		'1.19.4',
		'1.19.2',
		'1.18.2',
		'1.16.5',
		'1.12.2'
	];

	function scheduleSearch() {
		if (searchDebounce) {
			clearTimeout(searchDebounce);
		}

		searchDebounce = setTimeout(() => {
			searchModpacks(true);
		}, 350);
	}

	async function searchModpacks(reset = false) {
		const trimmedQuery = searchQuery.trim();
		if (trimmedQuery.length < 2) {
			searchResults = [];
			searchError = null;
			hasMore = false;
			searchLoading = false;
			loadingMore = false;
			return;
		}

		if (reset) {
			pageIndex = 0;
			hasMore = true;
			searchResults = [];
			searchLoading = true;
		} else {
			if (searchLoading || loadingMore || !hasMore) return;
			loadingMore = true;
		}

		searchError = null;
		try {
			const params = new URLSearchParams({
				query: trimmedQuery,
				index: String(pageIndex),
				pageSize: String(pageSize)
			});

			if (selectedLoader !== 'auto') {
				params.set('loader', selectedLoader);
			}

			if (selectedVersion !== 'auto') {
				params.set('gameVersion', selectedVersion);
			}

			const res = await fetch(
				`/api/servers/${encodeURIComponent(serverName)}/mods/modrinth/modpacks/search?${params.toString()}`
			);

			if (res.ok) {
				const payload = (await res.json()) as ModrinthSearchResult;
				const results = payload.results || [];
				searchResults = reset ? results : [...searchResults, ...results];
				const nextIndex = (payload.index ?? pageIndex) + (payload.pageSize ?? pageSize);
				pageIndex = nextIndex;
				const totalHits = payload.totalHits ?? nextIndex;
				hasMore = results.length > 0 && nextIndex < totalHits;
			} else {
				const payload = await res.json().catch(() => ({}));
				searchError = payload.error || 'Search failed';
			}
		} catch (err) {
			searchError = err instanceof Error ? err.message : 'Search failed';
		} finally {
			if (reset) {
				searchLoading = false;
			} else {
				loadingMore = false;
			}
		}
	}

	async function showDetail(projectId: string) {
		detailOpen = true;
		detailLoading = true;
		detailError = null;
		detailProject = null;
		detailVersions = [];
		installProgress = null;

		try {
			const res = await fetch(
				`/api/servers/${encodeURIComponent(serverName)}/mods/modrinth/modpacks/project/${encodeURIComponent(projectId)}`
			);
			if (!res.ok) {
				const payload = await res.json().catch(() => ({}));
				throw new Error(payload.error || 'Failed to load modpack');
			}

			detailProject = await res.json();
			await loadVersions(projectId);
		} catch (err) {
			detailError = err instanceof Error ? err.message : 'Failed to load modpack';
		} finally {
			detailLoading = false;
		}
	}

	async function loadVersions(projectId: string) {
		try {
			const params = new URLSearchParams();
			if (selectedLoader !== 'auto') params.set('loader', selectedLoader);
			if (selectedVersion !== 'auto') params.set('gameVersion', selectedVersion);

			const res = await fetch(
				`/api/servers/${encodeURIComponent(
					serverName
				)}/mods/modrinth/modpacks/project/${encodeURIComponent(projectId)}/versions?${params.toString()}`
			);
			if (res.ok) {
				detailVersions = await res.json();
			}
		} catch (err) {
			console.error('Failed to load modpack versions', err);
		}
	}

	async function installVersion(versionId: string) {
		if (!detailProject || !versionId) return;
		installingVersionId = versionId;
		installProgress = { status: 'queued', percentage: 0, message: 'Starting installation...' };

		try {
			const res = await fetch(`/api/servers/${encodeURIComponent(serverName)}/mods/modrinth/modpacks/install`, {
				method: 'POST',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify({
					projectId: detailProject.id,
					versionId,
					projectName: detailProject.title,
					projectVersion: detailVersions.find((version) => version.id === versionId)?.name,
					logoUrl: detailProject.iconUrl
				})
			});

			if (!res.ok) {
				const payload = await res.json().catch(() => ({}));
				throw new Error(payload.error || 'Failed to install modpack');
			}

			const { jobId } = await res.json();
			startInstallStream(jobId);
		} catch (err) {
			installProgress = {
				status: 'failed',
				percentage: 0,
				message: err instanceof Error ? err.message : 'Failed to install modpack'
			};
		} finally {
			installingVersionId = null;
		}
	}

	function startInstallStream(jobId: string) {
		installEventSource?.close();
		installEventSource = new EventSource(
			`/api/servers/${encodeURIComponent(serverName)}/mods/stream?jobId=${encodeURIComponent(jobId)}`
		);

		installEventSource.onmessage = (event) => {
			const progress = JSON.parse(event.data);
			installProgress = {
				status: progress.status,
				percentage: progress.percentage ?? 0,
				message: progress.message ?? ''
			};

			if (outputEl) {
				tick().then(() => {
					outputEl!.scrollTop = outputEl!.scrollHeight;
				});
			}

			if (progress.status === 'completed' || progress.status === 'failed') {
				installEventSource?.close();
				installEventSource = null;
				if (progress.status === 'completed') {
					setTimeout(() => {
						installProgress = null;
						detailOpen = false;
						onInstallComplete?.();
					}, 2000);
				}
			}
		};

		installEventSource.onerror = () => {
			installEventSource?.close();
			installEventSource = null;
			installProgress = {
				status: 'failed',
				percentage: 0,
				message: 'Connection lost'
			};
		};
	}

	function closeDetail() {
		installEventSource?.close();
		installEventSource = null;
		installProgress = null;
		detailOpen = false;
		detailProject = null;
		detailVersions = [];
		detailError = null;
	}
</script>

<div class="search-container">
	<div class="search-controls">
		<input
			type="text"
			bind:value={searchQuery}
			oninput={scheduleSearch}
			placeholder="Search Modrinth modpacks..."
			class="search-input"
		/>

		<div class="filters">
			<select bind:value={selectedLoader} onchange={scheduleSearch}>
				{#each loaderOptions as option}
					<option value={option.value}>{option.label}</option>
				{/each}
			</select>

			<select bind:value={selectedVersion} onchange={scheduleSearch}>
				{#each commonMinecraftVersions as version}
					<option value={version}>{version === 'auto' ? 'Auto (server)' : version}</option>
				{/each}
			</select>
		</div>
	</div>

	{#if searchLoading}
		<div class="loading">Searching...</div>
	{:else if searchError}
		<div class="error">{searchError}</div>
	{:else if searchResults.length > 0}
		<div class="results">
			{#each searchResults as item (item.projectId)}
				<button class="result-card" onclick={() => showDetail(item.projectId)}>
					<div class="result-media">
						{#if item.iconUrl}
							<img src={item.iconUrl} alt={item.title} class="result-logo" />
						{:else}
							<div class="result-placeholder">No image</div>
						{/if}
					</div>
					<div class="result-body">
						<h3>{item.title}</h3>
						<p class="summary">{item.description}</p>
						<div class="result-meta">
							<span>{item.downloads.toLocaleString()} downloads</span>
							<span class="muted">View details</span>
						</div>
					</div>
				</button>
			{/each}
		</div>

		{#if loadingMore}
			<div class="loading">Loading more...</div>
		{:else if hasMore}
			<button class="load-more-btn" onclick={() => searchModpacks(false)}>
				Load More
			</button>
		{/if}
	{:else if searchQuery.trim().length >= 2}
		<div class="no-results">No results found</div>
	{/if}
</div>

{#if detailOpen}
	<div class="modal-overlay" onclick={closeDetail}>
		<div class="modal" onclick={(e) => e.stopPropagation()}>
			{#if detailLoading}
				<div class="loading">Loading...</div>
			{:else if detailError}
				<div class="error">{detailError}</div>
			{:else if detailProject}
				{#if installProgress}
					<div class="install-progress">
						<h2>Installing {detailProject.title}</h2>
						<ProgressBar value={installProgress.percentage} color="green" size="md" />
						<p class="status">{installProgress.message}</p>

						<div class="output-log" bind:this={outputEl}>
							<div class="log-line">{installProgress.message}</div>
						</div>

						{#if installProgress.status === 'completed'}
							<div class="success-box">Installation complete!</div>
						{:else if installProgress.status === 'failed'}
							<div class="error-box">{installProgress.message}</div>
						{/if}
					</div>
				{:else}
					<div class="modal-header">
						<div class="title-block">
							{#if detailProject.iconUrl}
								<img src={detailProject.iconUrl} alt={detailProject.title} class="detail-logo" />
							{/if}
							<div>
								<h2>{detailProject.title}</h2>
								<p>{detailProject.description}</p>
							</div>
						</div>
						<button class="close-btn" onclick={closeDetail}>Close</button>
					</div>

					<div class="versions-section">
						<h3>Available Versions</h3>
						{#if detailVersions.length === 0}
							<p class="muted">No versions match your filters.</p>
						{:else}
							{#each detailVersions as version}
								<div class="version-card">
									<div class="version-info">
										<div class="version-name">{version.name || version.versionNumber}</div>
										<div class="version-meta">
											<span>{version.gameVersions.slice(0, 4).join(', ')}</span>
											<span class="muted">
												{new Date(version.datePublished).toLocaleDateString()}
											</span>
										</div>
									</div>
									<button
										class="install-btn"
										onclick={() => installVersion(version.id)}
										disabled={installingVersionId === version.id}
									>
										{installingVersionId === version.id ? 'Installing...' : 'Install'}
									</button>
								</div>
							{/each}
						{/if}
					</div>
				{/if}
			{/if}
		</div>
	</div>
{/if}

<style>
	.search-container {
		background: #1a1e2f;
		border-radius: 16px;
		padding: 20px;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.35);
		display: flex;
		flex-direction: column;
		gap: 16px;
	}

	.search-controls {
		display: flex;
		gap: 12px;
		align-items: center;
		flex-wrap: wrap;
	}

	.search-input {
		background: #141827;
		border: 1px solid #2a2f47;
		border-radius: 8px;
		padding: 10px 14px;
		color: #eef0f8;
		min-width: 240px;
		flex: 1;
	}

	.search-input::placeholder {
		color: #6b7190;
	}

	.filters {
		display: flex;
		gap: 12px;
		flex-wrap: wrap;
	}

	.filters select {
		background: #141827;
		border: 1px solid #2a2f47;
		border-radius: 8px;
		padding: 10px 12px;
		color: #eef0f8;
	}

	.results {
		display: grid;
		grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
		gap: 16px;
	}

	.result-card {
		background: #141827;
		border-radius: 12px;
		padding: 16px;
		display: grid;
		grid-template-columns: 72px 1fr;
		gap: 16px;
		border: 1px solid #242a40;
		cursor: pointer;
		text-align: left;
		transition: background 0.2s;
	}

	.result-card:hover {
		background: #1a1f33;
	}

	.result-logo {
		width: 72px;
		height: 72px;
		border-radius: 12px;
		object-fit: cover;
		background: #1a1f33;
	}

	.result-placeholder {
		width: 72px;
		height: 72px;
		border-radius: 12px;
		background: #1a1f33;
		display: flex;
		align-items: center;
		justify-content: center;
		font-size: 11px;
		color: #8890b1;
		text-align: center;
		padding: 6px;
	}

	.result-body {
		display: flex;
		flex-direction: column;
		gap: 8px;
	}

	.result-card h3 {
		margin: 0 0 6px;
		font-size: 16px;
		font-weight: 600;
		color: #eef0f8;
	}

	.summary {
		margin: 0;
		color: #9aa2c5;
		font-size: 13px;
		overflow: hidden;
		text-overflow: ellipsis;
		display: -webkit-box;
		-webkit-line-clamp: 2;
		-webkit-box-orient: vertical;
	}

	.result-meta {
		display: flex;
		justify-content: space-between;
		align-items: center;
		color: #9aa2c5;
		font-size: 12px;
		gap: 12px;
	}

	.muted {
		color: #8890b1;
		margin: 0;
	}

	.load-more-btn {
		background: var(--mc-grass);
		color: white;
		border: none;
		border-radius: 8px;
		padding: 10px 20px;
		font-weight: 600;
		cursor: pointer;
		width: 100%;
		margin-top: 8px;
		transition: opacity 0.2s;
	}

	.load-more-btn:hover {
		opacity: 0.9;
	}

	.modal-overlay {
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
		max-width: 720px;
		width: 100%;
		max-height: 85vh;
		overflow-y: auto;
		padding: 20px;
		border: 1px solid #2a2f47;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.4);
	}

	.modal-header {
		display: flex;
		justify-content: space-between;
		align-items: start;
		margin-bottom: 16px;
		gap: 12px;
	}

	.title-block {
		display: flex;
		gap: 12px;
		align-items: center;
	}

	.detail-logo {
		width: 48px;
		height: 48px;
		border-radius: 10px;
		object-fit: cover;
		background: #1a1f33;
	}

	.modal-header h2 {
		margin: 0 0 6px 0;
		font-size: 22px;
		font-weight: 600;
		color: #eef0f8;
	}

	.modal-header p {
		margin: 0;
		color: #9aa2c5;
		font-size: 13px;
	}

	.close-btn {
		background: rgba(43, 47, 69, 0.9);
		color: #d4d9f1;
		border: 1px solid rgba(106, 176, 76, 0.25);
		border-radius: 8px;
		padding: 8px 16px;
		font-size: 14px;
		font-weight: 600;
		cursor: pointer;
		transition: background 0.2s;
	}

	.close-btn:hover {
		background: rgba(53, 57, 79, 0.9);
	}

	.versions-section {
		display: flex;
		flex-direction: column;
		gap: 12px;
	}

	.version-card {
		display: flex;
		justify-content: space-between;
		align-items: center;
		gap: 12px;
		padding: 12px;
		border-radius: 12px;
		background: #1a1f33;
		border: 1px solid #2a2f47;
	}

	.version-name {
		font-size: 14px;
		font-weight: 600;
		color: #eef0f8;
		margin-bottom: 4px;
	}

	.version-meta {
		display: flex;
		gap: 12px;
		font-size: 12px;
		color: #8890b1;
		flex-wrap: wrap;
	}

	.install-btn {
		background: var(--mc-grass);
		color: white;
		border: none;
		border-radius: 8px;
		padding: 8px 16px;
		font-weight: 600;
		cursor: pointer;
		transition: opacity 0.2s;
	}

	.install-btn:disabled {
		opacity: 0.6;
		cursor: not-allowed;
	}

	.install-progress {
		display: flex;
		flex-direction: column;
		gap: 16px;
	}

	.status {
		margin: 0;
		color: #9aa2c5;
		font-size: 13px;
	}

	.output-log {
		background: #0d0f16;
		border-radius: 8px;
		padding: 12px;
		max-height: 200px;
		overflow-y: auto;
		font-family: 'Courier New', monospace;
		font-size: 12px;
		line-height: 1.5;
	}

	.log-line {
		color: #9aa2c5;
		white-space: pre-wrap;
		word-break: break-word;
	}

	.success-box {
		background: rgba(106, 176, 76, 0.1);
		border: 1px solid rgba(106, 176, 76, 0.3);
		border-radius: 12px;
		padding: 16px 20px;
		color: #b7f5a2;
	}

	.error-box {
		background: rgba(255, 92, 92, 0.1);
		border: 1px solid rgba(255, 92, 92, 0.3);
		border-radius: 12px;
		padding: 16px 20px;
		color: #ff9f9f;
	}

	.loading,
	.error,
	.no-results {
		padding: 16px;
		text-align: center;
		color: #8890b1;
	}

	.error {
		color: #ff9f9f;
	}
</style>
