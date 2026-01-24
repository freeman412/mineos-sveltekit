<script lang="ts">
	import { onMount, tick } from 'svelte';
	import { modal } from '$lib/stores/modal';
	import type { CurseForgeModSummary, CurseForgeMod, ModpackInstallProgress } from '$lib/api/types';
	import ProgressBar from './ProgressBar.svelte';

	interface Props {
		serverName: string;
		onInstallComplete?: () => void;
	}

	let { serverName, onInstallComplete }: Props = $props();

	let searchQuery = $state('');
	let searchResults = $state<CurseForgeModSummary[]>([]);
	let searchLoading = $state(false);
	let searchError = $state<string | null>(null);
	let searchType = $state<'mods' | 'modpacks'>('mods');
	let sortOption = $state<'downloads-desc' | 'downloads-asc' | 'updated-desc' | 'name-asc'>(
		'downloads-desc'
	);
	let minDownloads = $state('0');
	let searchDebounce: ReturnType<typeof setTimeout> | null = null;
	let pageIndex = 0;
	let pageSize = 20;
	let hasMore = $state(true);
	let loadingMore = $state(false);
	let loadMoreTrigger = $state<HTMLDivElement | undefined>(undefined);

	let detailOpen = $state(false);
	let detailLoading = $state(false);
	let detailError = $state<string | null>(null);
	let detailItem = $state<CurseForgeMod | null>(null);
	let selectedMinecraftVersion = $state<string>('');
	let availableFiles = $state<any[]>([]);
	let filesLoading = $state(false);

	// In-modal install progress
	let modpackInstallProgress = $state<ModpackInstallProgress | null>(null);
	let modpackEventSource: EventSource | null = null;
	let modInstallProgress = $state<any | null>(null);
	let modEventSource: EventSource | null = null;
	let outputEl = $state<HTMLDivElement | null>(null);

	const classIdMap: Record<'mods' | 'modpacks', number> = {
		mods: 6,
		modpacks: 4471
	};

	const commonMinecraftVersions = [
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

	async function searchCurseForge(reset = false) {
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
			const classId = classIdMap[searchType];
			const { sort, order} = getSortParams();
			const params = new URLSearchParams({
				query: trimmedQuery,
				classId: String(classId),
				index: String(pageIndex),
				pageSize: String(pageSize)
			});

			if (sort) params.set('sort', sort);
			if (order) params.set('order', order);

			const minValue = Number(minDownloads);
			if (!Number.isNaN(minValue) && minValue > 0) {
				params.set('minDownloads', String(minValue));
			}

			const res = await fetch(`/api/curseforge/search?${params.toString()}`);
			if (res.ok) {
				const payload = await res.json();
				const results = payload.results || [];
				searchResults = reset ? results : [...searchResults, ...results];
				const nextIndex = (payload.index ?? pageIndex) + (payload.pageSize ?? pageSize);
				pageIndex = nextIndex;
				const totalCount = payload.totalCount ?? nextIndex;
				hasMore = results.length > 0 && nextIndex < totalCount;
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

	function getSortParams() {
		switch (sortOption) {
			case 'downloads-asc':
				return { sort: 'downloads', order: 'asc' };
			case 'updated-desc':
				return { sort: 'updated', order: 'desc' };
			case 'name-asc':
				return { sort: 'name', order: 'asc' };
			default:
				return { sort: 'downloads', order: 'desc' };
		}
	}

	function scheduleSearch() {
		if (searchDebounce) {
			clearTimeout(searchDebounce);
		}

		searchDebounce = setTimeout(() => {
			searchCurseForge(true);
		}, 350);
	}

	async function showDetail(modId: number) {
		detailOpen = true;
		detailLoading = true;
		detailError = null;
		detailItem = null;
		selectedMinecraftVersion = '';
		availableFiles = [];

		try {
			const res = await fetch(`/api/curseforge/mod/${modId}`);
			if (res.ok) {
				detailItem = await res.json();
				// Load default files
				await loadFilesForVersion('');
			} else {
				const payload = await res.json().catch(() => ({}));
				detailError = payload.error || 'Failed to load details';
			}
		} catch (err) {
			detailError = err instanceof Error ? err.message : 'Failed to load details';
		} finally {
			detailLoading = false;
		}
	}

	async function loadFilesForVersion(version: string) {
		if (!detailItem) return;
		filesLoading = true;
		try {
			const params = new URLSearchParams();
			if (version) params.set('gameVersion', version);
			const res = await fetch(`/api/curseforge/mod/${detailItem.id}/files?${params}`);
			if (res.ok) {
				availableFiles = await res.json();
			}
		} catch (err) {
			console.error('Failed to load files:', err);
		} finally {
			filesLoading = false;
		}
	}

	$effect(() => {
		if (selectedMinecraftVersion !== undefined) {
			loadFilesForVersion(selectedMinecraftVersion);
		}
	});

	async function installMod(modId: number, fileId?: number) {
		if (!detailItem) return;

		// Show progress immediately
		modInstallProgress = {
			status: 'queued',
			percentage: 0,
			message: 'Starting installation...'
		};

		try {
			const res = await fetch(`/api/servers/${serverName}/mods/install-from-curseforge`, {
				method: 'POST',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify({ modId, fileId: fileId || null })
			});

			if (!res.ok) {
				const payload = await res.json().catch(() => ({}));
				throw new Error(payload.error || 'Installation failed');
			}

			const { jobId } = await res.json();
			startModStream(jobId);
		} catch (err) {
			modInstallProgress = {
				status: 'failed',
				percentage: 0,
				message: err instanceof Error ? err.message : 'Installation failed'
			};
		}
	}

	async function installModpack(modId: number, fileId?: number) {
		if (!detailItem) return;

		// Show progress immediately
		modpackInstallProgress = {
			jobId: '',
			serverName,
			status: 'queued',
			percentage: 0,
			currentStep: 'Starting installation...',
			currentModIndex: 0,
			totalMods: 0,
			currentModName: null,
			outputLines: [],
			error: null
		};

		try {
			const res = await fetch(`/api/servers/${serverName}/modpacks`, {
				method: 'POST',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify({
					modpackId: modId,
					fileId,
					modpackName: detailItem.name,
					modpackVersion: fileId
						? detailItem.latestFiles.find((f: any) => f.id === fileId)?.fileName
						: detailItem.latestFiles[0]?.fileName,
					logoUrl: detailItem.logoUrl
				})
			});

			if (!res.ok) {
				const payload = await res.json().catch(() => ({}));
				throw new Error(payload.error || 'Installation failed');
			}

			const { jobId } = await res.json();
			startModpackStream(jobId);
		} catch (err) {
			modpackInstallProgress = {
				...modpackInstallProgress!,
				status: 'failed',
				error: err instanceof Error ? err.message : 'Installation failed'
			};
		}
	}

	function startModStream(jobId: string) {
		modEventSource?.close();
		modEventSource = new EventSource(
			`/api/servers/${serverName}/mods/stream?jobId=${encodeURIComponent(jobId)}`
		);

		modEventSource.onmessage = (event) => {
			const progress = JSON.parse(event.data);
			modInstallProgress = progress;

			if (progress.status === 'completed' || progress.status === 'failed') {
				modEventSource?.close();
				modEventSource = null;
				if (progress.status === 'completed') {
					setTimeout(() => {
						modInstallProgress = null;
						detailOpen = false;
						onInstallComplete?.();
					}, 2000);
				}
			}
		};

		modEventSource.onerror = () => {
			modEventSource?.close();
			modEventSource = null;
			modInstallProgress = {
				status: 'failed',
				percentage: 0,
				message: 'Connection lost'
			};
		};
	}

	function startModpackStream(jobId: string) {
		modpackEventSource?.close();
		modpackEventSource = new EventSource(
			`/api/servers/${serverName}/modpacks/install/${encodeURIComponent(jobId)}/stream`
		);

		modpackEventSource.onmessage = (event) => {
			const progress = JSON.parse(event.data);
			modpackInstallProgress = progress;

			if (outputEl) {
				tick().then(() => {
					outputEl!.scrollTop = outputEl!.scrollHeight;
				});
			}

			if (progress.status === 'completed' || progress.status === 'failed') {
				modpackEventSource?.close();
				modpackEventSource = null;

				if (progress.status === 'completed') {
					setTimeout(() => {
						modpackInstallProgress = null;
						detailOpen = false;
						onInstallComplete?.();
					}, 3000);
				}
			}
		};

		modpackEventSource.onerror = () => {
			modpackEventSource?.close();
			modpackEventSource = null;
			if (modpackInstallProgress) {
				modpackInstallProgress = {
					...modpackInstallProgress,
					status: 'failed',
					error: 'Connection lost'
				};
			}
		};
	}

	function closeDetail() {
		modpackEventSource?.close();
		modpackEventSource = null;
		modEventSource?.close();
		modEventSource = null;
		modpackInstallProgress = null;
		modInstallProgress = null;
		detailOpen = false;
	}

	onMount(() => {
		let observer: IntersectionObserver;

		const setupObserver = () => {
			if (observer) observer.disconnect();

			observer = new IntersectionObserver(
				(entries) => {
					entries.forEach((entry) => {
						if (entry.isIntersecting && !searchLoading && !loadingMore && hasMore) {
							searchCurseForge(false);
						}
					});
				},
				{
					threshold: 0.1,
					rootMargin: '200px'
				}
			);

			if (loadMoreTrigger) {
				observer.observe(loadMoreTrigger);
			}
		};

		// Set up observer when loadMoreTrigger becomes available
		$effect(() => {
			if (loadMoreTrigger) {
				setupObserver();
			}
		});

		return () => {
			if (observer) observer.disconnect();
			modpackEventSource?.close();
			modEventSource?.close();
		};
	});
</script>

<div class="search-container">
	<div class="search-controls">
		<div class="type-tabs">
			<button class:active={searchType === 'mods'} onclick={() => { searchType = 'mods'; scheduleSearch(); }}>
				Mods
			</button>
			<button class:active={searchType === 'modpacks'} onclick={() => { searchType = 'modpacks'; scheduleSearch(); }}>
				Modpacks
			</button>
		</div>

		<input
			type="text"
			bind:value={searchQuery}
			oninput={scheduleSearch}
			placeholder="Search CurseForge..."
			class="search-input"
		/>

		<div class="filters">
			<select bind:value={sortOption} onchange={scheduleSearch}>
				<option value="downloads-desc">Most Downloads</option>
				<option value="downloads-asc">Least Downloads</option>
				<option value="updated-desc">Recently Updated</option>
				<option value="name-asc">Name (A-Z)</option>
			</select>

			<select bind:value={minDownloads} onchange={scheduleSearch}>
				<option value="0">All downloads</option>
				<option value="10000">10k+ downloads</option>
				<option value="100000">100k+ downloads</option>
				<option value="1000000">1M+ downloads</option>
			</select>
		</div>
	</div>

	{#if searchLoading}
		<div class="loading">Searching...</div>
	{:else if searchError}
		<div class="error">{searchError}</div>
	{:else if searchResults.length > 0}
		<div class="results">
			{#each searchResults as item (item.id)}
				<button class="result-card" onclick={() => showDetail(item.id)}>
					<div class="result-media">
						{#if item.logoUrl}
							<img src={item.logoUrl} alt={item.name} class="result-logo" />
						{:else}
							<div class="result-placeholder">No image</div>
						{/if}
					</div>
					<div class="result-body">
						<h3>{item.name}</h3>
						<p class="summary">{item.summary}</p>
						<div class="result-meta">
							<span>{item.downloadCount.toLocaleString()} downloads</span>
							<span class="muted">View details</span>
						</div>
					</div>
				</button>
			{/each}
		</div>

		<div bind:this={loadMoreTrigger} class="load-trigger"></div>

		{#if loadingMore}
			<div class="loading">Loading more...</div>
		{:else if hasMore}
			<button class="load-more-btn" onclick={() => searchCurseForge(false)}>
				Load More
			</button>
		{/if}
	{:else if searchQuery.trim().length >= 2}
		<div class="no-results">No results found</div>
	{/if}
</div>

{#if detailOpen && detailItem}
	<!-- svelte-ignore a11y_no_static_element_interactions, a11y_click_events_have_key_events -->
	<div class="modal-overlay" onclick={closeDetail} role="presentation">
		<!-- svelte-ignore a11y_no_static_element_interactions, a11y_click_events_have_key_events, a11y_interactive_supports_focus -->
		<div class="modal" role="dialog" aria-modal="true" tabindex="-1" onclick={(e) => e.stopPropagation()}>
			{#if modpackInstallProgress || modInstallProgress}
				<div class="install-progress">
					{#if modpackInstallProgress}
						<h2>Installing {detailItem.name}</h2>
						<ProgressBar value={modpackInstallProgress.percentage} color="green" size="md" />
						<p class="status">{modpackInstallProgress.currentStep}</p>
						{#if modpackInstallProgress.totalMods > 0}
							<p class="mod-progress">
								Mod {modpackInstallProgress.currentModIndex} / {modpackInstallProgress.totalMods}
								{#if modpackInstallProgress.currentModName}
									<span class="mod-name">({modpackInstallProgress.currentModName})</span>
								{/if}
							</p>
						{/if}

						<div class="output-log" bind:this={outputEl}>
							{#each modpackInstallProgress.outputLines || [] as line}
								<div class="log-line">{line}</div>
							{/each}
						</div>

						{#if modpackInstallProgress.status === 'completed'}
							<div class="success-box">
								Installation complete! {modpackInstallProgress.totalMods} mods installed.
							</div>
						{:else if modpackInstallProgress.status === 'failed'}
							<div class="error-box">
								{modpackInstallProgress.error || 'Installation failed'}
							</div>
						{/if}
					{:else if modInstallProgress}
						<h2>Installing Mod</h2>
						<ProgressBar value={modInstallProgress.percentage} color="green" size="md" />
						<p class="status">{modInstallProgress.message}</p>

						{#if modInstallProgress.status === 'completed'}
							<div class="success-box">Installation complete!</div>
						{:else if modInstallProgress.status === 'failed'}
							<div class="error-box">{modInstallProgress.message}</div>
						{/if}
					{/if}
				</div>
			{:else}
				<div class="modal-header">
					<div>
						<h2>{detailItem.name}</h2>
						<p>{detailItem.summary}</p>
					</div>
					<button class="close-btn" onclick={closeDetail}>Close</button>
				</div>

				<div class="version-selector">
					<label>
						Minecraft Version:
						<select bind:value={selectedMinecraftVersion}>
							<option value="">All Versions (Latest Files)</option>
							{#each commonMinecraftVersions as version}
								<option value={version}>{version}</option>
							{/each}
						</select>
					</label>
				</div>

				{#if filesLoading}
					<div class="loading">Loading files...</div>
				{:else if availableFiles.length > 0}
					<div class="files-list">
						<h3>Available Files</h3>
						{#each availableFiles as file}
							<div class="file-item">
								<div class="file-info">
									<div class="file-name">{file.fileName}</div>
									<div class="file-meta">
										<span>{file.gameVersions.slice(0, 4).join(', ')}</span>
									</div>
								</div>
								<button
									class="install-btn"
									onclick={() => {
										if (detailItem) {
											searchType === 'modpacks' ? installModpack(detailItem.id, file.id) : installMod(detailItem.id, file.id);
										}
									}}
								>
									Install
								</button>
							</div>
						{/each}
					</div>
				{:else}
					<div class="no-files">No files available</div>
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

	.type-tabs {
		display: flex;
		background: #141827;
		border-radius: 10px;
		padding: 4px;
	}

	.type-tabs button {
		background: transparent;
		border: none;
		color: #8890b1;
		padding: 8px 14px;
		border-radius: 8px;
		cursor: pointer;
		transition: all 0.2s;
	}

	.type-tabs button.active {
		background: #1f2a4a;
		color: #eef0f8;
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

	.result-media {
		display: flex;
		align-items: flex-start;
		justify-content: center;
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
		line-clamp: 2;
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
	}

	.modal-header h2 {
		margin: 0 0 8px 0;
		font-size: 24px;
		font-weight: 600;
		color: #eef0f8;
	}

	.modal-header p {
		margin: 0;
		color: #9aa2c5;
		font-size: 14px;
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

	.version-selector {
		margin-bottom: 16px;
	}

	.version-selector label {
		display: flex;
		flex-direction: column;
		gap: 8px;
		color: #9aa2c5;
		font-size: 14px;
	}

	.version-selector select {
		background: #141827;
		border: 1px solid #2a2f47;
		border-radius: 8px;
		padding: 10px 12px;
		color: #eef0f8;
	}

	.files-list {
		display: flex;
		flex-direction: column;
		gap: 12px;
	}

	.files-list h3 {
		margin: 0 0 8px 0;
		font-size: 18px;
		font-weight: 600;
		color: #eef0f8;
	}

	.file-item {
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 12px;
		padding: 12px;
		border-radius: 12px;
		background: #1a1f33;
		border: 1px solid #2a2f47;
	}

	.file-info {
		flex: 1;
	}

	.file-name {
		font-size: 14px;
		font-weight: 600;
		color: #eef0f8;
		margin-bottom: 4px;
	}

	.file-meta {
		font-size: 12px;
		color: #8890b1;
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

	.install-btn:hover:not(:disabled) {
		opacity: 0.9;
	}

	.install-btn:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.install-progress {
		display: flex;
		flex-direction: column;
		gap: 16px;
	}

	.install-progress h2 {
		margin: 0;
		font-size: 24px;
		font-weight: 600;
		color: #eef0f8;
	}

	.status {
		margin: 0;
		color: #9aa2c5;
		font-size: 13px;
	}

	.mod-progress {
		color: #9aa2c5;
		font-size: 14px;
		margin: 0;
	}

	.mod-name {
		font-style: italic;
		color: #8890b1;
	}

	.output-log {
		background: #0d0f16;
		border-radius: 8px;
		padding: 12px;
		max-height: 300px;
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
	.no-results,
	.no-files {
		padding: 16px;
		text-align: center;
		color: #8890b1;
	}

	.error {
		color: #ff9f9f;
	}

	.load-trigger {
		min-height: 1px;
		grid-column: 1 / -1;
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
		margin-top: 16px;
		transition: opacity 0.2s;
	}

	.load-more-btn:hover {
		opacity: 0.9;
	}
</style>
