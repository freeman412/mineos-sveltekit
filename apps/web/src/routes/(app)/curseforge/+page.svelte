<script lang="ts">
	import { onMount, tick } from 'svelte';
	import { modal } from '$lib/stores/modal';
	import type { PageData } from './$types';
	import type { CurseForgeModSummary, CurseForgeMod, ServerSummary } from '$lib/api/types';

	let { data }: { data: PageData } = $props();

	const servers = (data.servers.data ?? []) as ServerSummary[];
	let selectedServer = $state(servers[0]?.name ?? '');

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
	let hasMore = true;
	let loadingMore = $state(false);
	let loadMoreTrigger: HTMLDivElement;
	let installLoading = $state<Record<string, boolean>>({});

	let detailOpen = $state(false);
	let detailLoading = $state(false);
	let detailError = $state<string | null>(null);
	let detailItem = $state<CurseForgeMod | null>(null);

	let jobStatus = $state<JobProgress | null>(null);
	let jobEventSource: EventSource | null = null;

	type JobProgress = {
		jobId: string;
		type: string;
		serverName: string;
		status: string;
		percentage: number;
		message: string | null;
		timestamp: string;
	};

	const classIdMap: Record<'mods' | 'modpacks', number> = {
		mods: 6,
		modpacks: 4471
	};

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
			const { sort, order } = getSortParams();
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

			await maybeLoadMore();
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

	function getInstallKey(modId: number, fileId?: number) {
		return fileId ? `${modId}-${fileId}` : `${modId}`;
	}

	async function installFromCurseForge(modId: number) {
		if (!selectedServer) {
			await modal.alert('Select a server to install to', 'Select Server');
			return;
		}

		const key = getInstallKey(modId);
		installLoading[key] = true;
		installLoading = { ...installLoading };
		try {
			const endpoint =
				searchType === 'modpacks'
					? `/api/servers/${selectedServer}/modpacks/install`
					: `/api/servers/${selectedServer}/mods/install-from-curseforge`;
			const body =
				searchType === 'modpacks' ? { modpackId: modId, fileId: null } : { modId, fileId: null };
			const res = await fetch(endpoint, {
				method: 'POST',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify(body)
			});
			if (res.ok) {
				const payload = await res.json().catch(() => null);
				if (payload?.jobId) {
					startJobStream(payload.jobId);
				}
			} else {
				const payload = await res.json().catch(() => ({}));
				await modal.error(payload.error || 'Install failed');
			}
		} catch (err) {
			await modal.error(err instanceof Error ? err.message : 'Install failed');
		} finally {
			delete installLoading[key];
			installLoading = { ...installLoading };
		}
	}

	async function installFromCurseForgeFile(modId: number, fileId: number) {
		if (!selectedServer) {
			await modal.alert('Select a server to install to', 'Select Server');
			return;
		}

		const key = getInstallKey(modId, fileId);
		installLoading[key] = true;
		installLoading = { ...installLoading };
		try {
			const endpoint =
				searchType === 'modpacks'
					? `/api/servers/${selectedServer}/modpacks/install`
					: `/api/servers/${selectedServer}/mods/install-from-curseforge`;
			const body =
				searchType === 'modpacks'
					? { modpackId: modId, fileId }
					: { modId, fileId };
			const res = await fetch(endpoint, {
				method: 'POST',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify(body)
			});

			if (res.ok) {
				const payload = await res.json().catch(() => null);
				if (payload?.jobId) {
					startJobStream(payload.jobId);
				}
			} else {
				const payload = await res.json().catch(() => ({}));
				await modal.error(payload.error || 'Install failed');
			}
		} catch (err) {
			await modal.error(err instanceof Error ? err.message : 'Install failed');
		} finally {
			delete installLoading[key];
			installLoading = { ...installLoading };
		}
	}

	async function openDetails(modId: number) {
		detailOpen = true;
		detailLoading = true;
		detailError = null;
		detailItem = null;
		try {
			const res = await fetch(`/api/curseforge/mod/${modId}`);
			if (res.ok) {
				detailItem = await res.json();
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

	function closeDetails() {
		detailOpen = false;
	}

	function startJobStream(jobId: string) {
		jobStatus = {
			jobId,
			type: 'mod-install',
			serverName: selectedServer,
			status: 'queued',
			percentage: 0,
			message: 'Queued',
			timestamp: new Date().toISOString()
		};

		jobEventSource?.close();
		jobEventSource = new EventSource(`/api/jobs/${jobId}/stream`);
		jobEventSource.onmessage = (event) => {
			try {
				jobStatus = JSON.parse(event.data) as JobProgress;
				if (jobStatus.status === 'completed' || jobStatus.status === 'failed') {
					jobEventSource?.close();
					jobEventSource = null;
				}
			} catch {
				// ignore parse errors
			}
		};
		jobEventSource.onerror = () => {
			jobEventSource?.close();
			jobEventSource = null;
		};
	}

	onMount(() => {
		const observer = new IntersectionObserver(
			(entries) => {
				if (entries[0].isIntersecting) {
					searchCurseForge();
				}
			},
			{ rootMargin: '200px' }
		);

		if (loadMoreTrigger) {
			observer.observe(loadMoreTrigger);
		}

		return () => observer.disconnect();
	});

	async function maybeLoadMore() {
		if (!loadMoreTrigger || !hasMore || searchLoading || loadingMore) return;
		await tick();
		const rect = loadMoreTrigger.getBoundingClientRect();
		if (rect.top <= window.innerHeight + 200) {
			searchCurseForge();
		}
	}

	$effect(() => {
		return () => {
			jobEventSource?.close();
		};
	});
</script>

<div class="page">
	<div class="page-header">
		<div>
			<h2>CurseForge</h2>
			<p class="subtitle">Search mods and modpacks, then install directly to a server</p>
		</div>
	</div>

	{#if data.servers.error}
		<div class="error-box">
			<p>Failed to load servers: {data.servers.error}</p>
		</div>
	{/if}

	{#if jobStatus}
		<div class="job-card">
			<div class="job-header">
				<h3>Install Job</h3>
				<span class="job-status">{jobStatus.status}</span>
			</div>
			<div class="job-details">
				<div class="job-progress">
					<div class="job-progress-bar" style={`width: ${jobStatus.percentage}%`}></div>
				</div>
				<p class="job-message">{jobStatus.message ?? 'Working...'}</p>
				<p class="job-meta">Job ID: {jobStatus.jobId}</p>
			</div>
		</div>
	{/if}

	<div class="search-panel">
		<div class="search-bar">
			<div class="toggle-group">
				<button
					class:active={searchType === 'mods'}
					onclick={() => {
						searchType = 'mods';
						searchCurseForge(true);
					}}
				>
					Mods
				</button>
				<button
					class:active={searchType === 'modpacks'}
					onclick={() => {
						searchType = 'modpacks';
						searchCurseForge(true);
					}}
				>
					Modpacks
				</button>
			</div>
			<input
				type="text"
				bind:value={searchQuery}
				placeholder="Search CurseForge..."
				oninput={scheduleSearch}
				onkeydown={(e) => {
					if (e.key === 'Enter') searchCurseForge(true);
				}}
			/>
			<select bind:value={sortOption} onchange={() => searchCurseForge(true)}>
				<option value="downloads-desc">Most downloaded</option>
				<option value="downloads-asc">Least downloaded</option>
				<option value="updated-desc">Recently updated</option>
				<option value="name-asc">Name A-Z</option>
			</select>
			<select bind:value={minDownloads} onchange={() => searchCurseForge(true)}>
				<option value="0">All downloads</option>
				<option value="10000">10k+ downloads</option>
				<option value="100000">100k+ downloads</option>
				<option value="1000000">1M+ downloads</option>
			</select>
			<button onclick={() => searchCurseForge(true)} disabled={searchLoading}>
				{searchLoading ? 'Searching...' : 'Search'}
			</button>
		</div>

		{#if searchError}
			<p class="error-text">{searchError}</p>
		{/if}

		{#if searchResults.length > 0}
			<div class="search-results">
				{#each searchResults as result}
					<div class="result-card" onclick={() => openDetails(result.id)}>
						<div class="result-media">
							{#if result.logoUrl}
								<img
									class="result-logo"
									src={result.logoUrl}
									alt={result.name}
									loading="lazy"
								/>
							{:else}
								<div class="result-placeholder">No image</div>
							{/if}
						</div>
						<div class="result-body">
							<h4>{result.name}</h4>
							<p class="summary">{result.summary}</p>
							<div class="result-meta">
								<span>{result.downloadCount.toLocaleString()} downloads</span>
								<span class="muted">View details</span>
							</div>
						</div>
					</div>
				{/each}
			</div>
			{#if loadingMore}
				<p class="muted">Loading more...</p>
			{/if}
		{/if}
		<div bind:this={loadMoreTrigger} class="load-more"></div>
		{#if searchResults.length > 0 && hasMore && !searchLoading}
			<button class="btn-secondary" onclick={() => searchCurseForge()}>
				Load more
			</button>
		{/if}
	</div>
</div>

	{#if detailOpen}
	<div class="modal-backdrop" onclick={closeDetails}>
		<div class="modal" onclick={(event) => event.stopPropagation()}>
			<div class="modal-header">
				<div class="modal-title">
					{#if detailItem?.logoUrl}
						<img class="detail-logo" src={detailItem.logoUrl} alt={detailItem.name} />
					{/if}
					<div>
						<h3>{detailItem?.name || 'Mod details'}</h3>
						{#if detailItem}
							<p class="muted">{detailItem.downloadCount.toLocaleString()} downloads</p>
						{/if}
					</div>
				</div>
				<button class="btn-secondary" onclick={closeDetails}>Close</button>
			</div>

			{#if detailLoading}
				<p class="muted">Loading details...</p>
			{:else if detailError}
				<p class="error-text">{detailError}</p>
			{:else if detailItem}
				<p class="summary">{detailItem.summary}</p>
				{#if data.servers.error}
					<p class="error-text">Failed to load servers: {data.servers.error}</p>
				{:else if servers.length === 0}
					<p class="muted">Create a server before installing mods.</p>
				{:else}
					<div class="modal-actions">
						<label class="modal-field">
							<span>Target server</span>
							<select bind:value={selectedServer}>
								{#each servers as server}
									<option value={server.name}>{server.name}</option>
								{/each}
							</select>
						</label>
						<button
							class="btn-primary"
							onclick={() => installFromCurseForge(detailItem.id)}
							disabled={!selectedServer || installLoading[getInstallKey(detailItem.id)]}
						>
							{installLoading[getInstallKey(detailItem.id)] ? 'Installing...' : 'Install latest'}
						</button>
					</div>
				{/if}

				{#if detailItem.categories.length > 0}
					<div class="chip-row">
						{#each detailItem.categories as category}
							<span class="chip">{category.name}</span>
						{/each}
					</div>
				{/if}

				{#if detailItem.latestFiles.length > 0}
					<div class="file-list">
						<h4>Latest files</h4>
						{#each detailItem.latestFiles.slice(0, 5) as file}
							<div class="file-row">
								<div>
									<div class="file-name">{file.fileName}</div>
									<div class="file-meta">
										{file.gameVersions.slice(0, 4).join(', ')}
									</div>
								</div>
								<button
									class="btn-primary"
									onclick={() => installFromCurseForgeFile(detailItem.id, file.id)}
									disabled={!selectedServer || installLoading[getInstallKey(detailItem.id, file.id)]}
								>
									{installLoading[getInstallKey(detailItem.id, file.id)] ? 'Installing...' : 'Install'}
								</button>
							</div>
						{/each}
					</div>
				{/if}
			{/if}
		</div>
	</div>
{/if}

<style>
	.page {
		display: flex;
		flex-direction: column;
		gap: 24px;
	}

	h2 {
		margin: 0 0 8px;
		font-size: 24px;
		font-weight: 600;
	}

	.subtitle {
		margin: 0;
		color: #aab2d3;
		font-size: 14px;
	}

	.search-panel {
		background: #1a1e2f;
		border-radius: 16px;
		padding: 20px;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.35);
		display: flex;
		flex-direction: column;
		gap: 16px;
	}

	.search-bar {
		display: flex;
		gap: 12px;
		align-items: center;
		flex-wrap: wrap;
	}

	.search-bar input {
		background: #141827;
		border: 1px solid #2a2f47;
		border-radius: 8px;
		padding: 10px 14px;
		color: #eef0f8;
		min-width: 240px;
		flex: 1;
	}

	.search-bar button {
		background: var(--mc-grass);
		color: white;
		border: none;
		border-radius: 8px;
		padding: 10px 18px;
		font-weight: 600;
		cursor: pointer;
	}

	.search-bar select {
		background: #141827;
		border: 1px solid #2a2f47;
		border-radius: 8px;
		padding: 10px 12px;
		color: #eef0f8;
	}

	.toggle-group {
		display: flex;
		background: #141827;
		border-radius: 10px;
		padding: 4px;
	}

	.toggle-group button {
		background: transparent;
		border: none;
		color: #8890b1;
		padding: 8px 14px;
		border-radius: 8px;
		cursor: pointer;
	}

	.toggle-group button.active {
		background: #1f2a4a;
		color: #eef0f8;
	}

	.search-results {
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
	}

	.result-card h4 {
		margin: 0 0 6px;
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

	.summary {
		margin: 0;
		color: #9aa2c5;
		font-size: 13px;
	}

	.result-meta {
		display: flex;
		justify-content: space-between;
		align-items: center;
		color: #9aa2c5;
		font-size: 12px;
		gap: 12px;
	}

	.btn-primary {
		background: var(--mc-grass);
		color: white;
		border: none;
		border-radius: 8px;
		padding: 8px 16px;
		font-weight: 600;
		cursor: pointer;
	}

	.btn-secondary {
		background: rgba(43, 47, 69, 0.9);
		color: #d4d9f1;
		border: 1px solid rgba(106, 176, 76, 0.25);
		border-radius: 8px;
		padding: 8px 16px;
		font-weight: 600;
		cursor: pointer;
	}

	.job-card {
		background: #161b2c;
		border-radius: 16px;
		padding: 18px 20px;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.35);
		border: 1px solid #2a2f47;
	}

	.job-header {
		display: flex;
		justify-content: space-between;
		align-items: center;
		margin-bottom: 12px;
	}

	.job-status {
		font-size: 12px;
		text-transform: uppercase;
		letter-spacing: 0.08em;
		color: #98a2c9;
	}

	.job-progress {
		height: 8px;
		background: #252a3d;
		border-radius: 999px;
		overflow: hidden;
	}

	.job-progress-bar {
		height: 100%;
		background: linear-gradient(90deg, var(--mc-grass), var(--mc-sky));
		transition: width 0.2s ease;
	}

	.job-details {
		display: flex;
		flex-direction: column;
		gap: 8px;
	}

	.job-message {
		margin: 0;
		color: #d4d9f1;
		font-size: 14px;
	}

	.job-meta {
		margin: 0;
		color: #8e96bb;
		font-size: 12px;
	}

	.error-text {
		color: #ff9f9f;
		margin: 0;
	}

	.muted {
		color: #8890b1;
		margin: 0;
	}

	.load-more {
		height: 1px;
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
		max-width: 720px;
		width: 100%;
		padding: 20px;
		border: 1px solid #2a2f47;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.4);
		display: flex;
		flex-direction: column;
		gap: 16px;
	}

	.modal-header {
		display: flex;
		justify-content: space-between;
		align-items: center;
	}

	.modal-title {
		display: flex;
		align-items: center;
		gap: 12px;
	}

	.detail-logo {
		width: 56px;
		height: 56px;
		border-radius: 12px;
		object-fit: cover;
		background: #1a1f33;
	}

	.modal-actions {
		display: flex;
		flex-wrap: wrap;
		gap: 12px;
		align-items: center;
	}

	.modal-field {
		display: flex;
		flex-direction: column;
		gap: 6px;
		color: #9aa2c5;
		font-size: 12px;
	}

	.modal-field select {
		background: #141827;
		border: 1px solid #2a2f47;
		border-radius: 8px;
		padding: 10px 12px;
		color: #eef0f8;
	}

	.chip-row {
		display: flex;
		flex-wrap: wrap;
		gap: 8px;
	}

	.chip {
		background: rgba(88, 101, 242, 0.2);
		color: #cdd3f3;
		padding: 4px 10px;
		border-radius: 999px;
		font-size: 12px;
	}

	.file-list {
		display: flex;
		flex-direction: column;
		gap: 12px;
	}

	.file-row {
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 12px;
		padding: 12px;
		border-radius: 12px;
		background: #1a1f33;
		border: 1px solid #2a2f47;
	}

	.file-name {
		font-size: 14px;
		font-weight: 600;
		color: #eef0f8;
	}

	.file-meta {
		font-size: 12px;
		color: #8890b1;
	}

	.error-box {
		background: rgba(255, 92, 92, 0.1);
		border: 1px solid rgba(255, 92, 92, 0.3);
		border-radius: 12px;
		padding: 16px 20px;
		color: #ff9f9f;
	}
</style>
