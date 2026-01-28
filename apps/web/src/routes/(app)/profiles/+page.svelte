<script lang="ts">
	import { onDestroy, onMount } from 'svelte';
	import { page } from '$app/stores';
	import { invalidateAll } from '$app/navigation';
	import { modal } from '$lib/stores/modal';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	let actionLoading = $state<Record<string, boolean>>({});
	let copyTargets = $state<Record<string, string>>({});
	let searchQuery = $state('');
	let groupFilter = $state('all');
	let statusFilter = $state<'all' | 'downloaded' | 'missing'>('all');
	let sortOption = $state<'name' | 'group' | 'version'>('name');
	let currentPage = $state(1);
	let pageSize = $state(8);

	let buildGroup = $state('spigot');
	let buildVersion = $state('');
	let buildError = $state('');
	let buildStatus = $state<'idle' | 'running' | 'completed' | 'failed'>('idle');
	let runId = $state<string | null>(null);
	let profileId = $state<string | null>(null);
	let runs = $state<any[]>([]);
	let runsLoading = $state(false);
	let logs = $state<string[]>([]);
	let logContainer: HTMLDivElement | null = null;
	let eventSource: EventSource | null = null;

	// Check for group query param on mount
	onMount(() => {
		const groupParam = $page.url.searchParams.get('group');
		if (groupParam) {
			groupFilter = groupParam;
		}
		loadRuns();
		const lastRun = localStorage.getItem('mineos_buildtools_run');
		if (lastRun) {
			attachRun(lastRun);
		}
	});

	const profiles = $derived(data.profiles.data ?? []);
	const servers = $derived(data.servers.data ?? []);
	const downloadedCount = $derived.by(() => profiles.filter((profile) => profile.downloaded).length);
	const missingCount = $derived.by(() => profiles.length - downloadedCount);

	const profileGroups = $derived.by(() => {
		const groups = new Set<string>();
		for (const profile of profiles) {
			if (profile.group) {
				groups.add(profile.group);
			}
		}
		return ['all', ...Array.from(groups).sort()];
	});

	const filteredProfiles = $derived.by(() => {
		const query = searchQuery.trim().toLowerCase();
		const list = profiles.filter((profile) => {
			if (query) {
				const haystack = `${profile.id} ${profile.group} ${profile.version} ${profile.filename ?? ''}`.toLowerCase();
				if (!haystack.includes(query)) return false;
			}

			if (groupFilter !== 'all' && profile.group !== groupFilter) {
				return false;
			}

			if (statusFilter === 'downloaded' && !profile.downloaded) return false;
			if (statusFilter === 'missing' && profile.downloaded) return false;

			return true;
		});

		return list.sort((a, b) => {
			switch (sortOption) {
				case 'group':
					return a.group.localeCompare(b.group);
				case 'version':
					return a.version.localeCompare(b.version);
				default:
					return a.id.localeCompare(b.id);
			}
		});
	});

	const totalPages = $derived.by(() =>
		Math.max(1, Math.ceil(filteredProfiles.length / pageSize))
	);

	const pagedProfiles = $derived.by(() => {
		const start = (currentPage - 1) * pageSize;
		return filteredProfiles.slice(start, start + pageSize);
	});

	const paginationPages = $derived.by(() => {
		const pages: (number | string)[] = [];
		const max = totalPages;
		const current = currentPage;
		const window = 2;
		const start = Math.max(1, current - window);
		const end = Math.min(max, current + window);

		if (start > 1) pages.push(1);
		if (start > 2) pages.push('...');

		for (let i = start; i <= end; i += 1) {
			pages.push(i);
		}

		if (end < max - 1) pages.push('...');
		if (end < max) pages.push(max);

		return pages;
	});

	const rangeStart = $derived.by(() =>
		filteredProfiles.length === 0 ? 0 : (currentPage - 1) * pageSize + 1
	);
	const rangeEnd = $derived.by(() =>
		Math.min(currentPage * pageSize, filteredProfiles.length)
	);

	const commonVersions = [
		'latest',
		'1.21.4',
		'1.21.3',
		'1.21.1',
		'1.21',
		'1.20.6',
		'1.20.4',
		'1.20.2',
		'1.20.1',
		'1.20',
		'1.19.4',
		'1.19.3',
		'1.19.2',
		'1.19.1',
		'1.19',
		'1.18.2',
		'1.18.1',
		'1.17.1',
		'1.16.5'
	];

	async function handleDownload(profileId: string) {
		actionLoading[profileId] = true;
		try {
			const res = await fetch(`/api/host/profiles/${profileId}/download`, { method: 'POST' });
			if (!res.ok) {
				const error = await res.json().catch(() => ({ error: 'Failed to download profile' }));
				await modal.error(error.error || 'Failed to download profile');
			} else {
				await invalidateAll();
			}
		} finally {
			delete actionLoading[profileId];
			actionLoading = { ...actionLoading };
		}
	}

	async function handleCopy(profileId: string) {
		const target = copyTargets[profileId];
		if (!target) {
			await modal.alert('Select a server to copy this profile to.', 'Select Server');
			return;
		}

		actionLoading[profileId] = true;
		try {
			const res = await fetch(`/api/host/profiles/${profileId}/copy-to-server`, {
				method: 'POST',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify({ serverName: target })
			});
			if (!res.ok) {
				const error = await res.json().catch(() => ({ error: 'Failed to copy profile' }));
				await modal.error(error.error || 'Failed to copy profile');
			} else {
				await invalidateAll();
			}
		} finally {
			delete actionLoading[profileId];
			actionLoading = { ...actionLoading };
		}
	}

	async function handleDelete(profileId: string) {
		const confirmed = await modal.confirm(`Delete BuildTools profile "${profileId}"?`, 'Delete Profile');
		if (!confirmed) {
			return;
		}

		actionLoading[profileId] = true;
		try {
			const res = await fetch(`/api/host/profiles/buildtools/${profileId}`, {
				method: 'DELETE'
			});
			if (!res.ok) {
				const error = await res.json().catch(() => ({ error: 'Failed to delete profile' }));
				await modal.error(error.error || 'Failed to delete profile');
			} else {
				await invalidateAll();
			}
		} finally {
			delete actionLoading[profileId];
			actionLoading = { ...actionLoading };
		}
	}

	$effect(() => {
		if (logContainer) {
			logContainer.scrollTop = logContainer.scrollHeight;
		}
	});

	async function startBuild() {
		if (!buildVersion.trim()) {
			buildError = 'Version is required';
			return;
		}

		buildError = '';
		buildStatus = 'running';
		logs = [];
		runId = null;
		profileId = null;

		try {
			const res = await fetch('/api/host/profiles/buildtools', {
				method: 'POST',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify({ group: buildGroup, version: buildVersion.trim() })
			});

			const payload = await res.json().catch(() => null);
			if (!res.ok) {
				buildStatus = 'failed';
				buildError = payload?.error || 'Failed to start BuildTools';
				return;
			}

			runId = payload.runId;
			profileId = payload.profileId;
			buildStatus = payload.status ?? 'running';

			if (runId) {
				localStorage.setItem('mineos_buildtools_run', runId);
				openStream(runId);
				await loadRuns();
			}
		} catch (err) {
			buildStatus = 'failed';
			buildError = err instanceof Error ? err.message : 'Failed to start BuildTools';
		}
	}

	function openStream(id: string) {
		eventSource?.close();
		eventSource = new EventSource(`/api/host/profiles/buildtools/runs/${id}/stream`);

		eventSource.onmessage = (event) => {
			try {
				const entry = JSON.parse(event.data);
				if (entry?.message) {
					logs = [...logs, entry.message];
				}
				if (entry?.status) {
					buildStatus = entry.status;
					if (entry.status !== 'running') {
						eventSource?.close();
						eventSource = null;
					}
				}
			} catch {
				logs = [...logs, event.data];
			}
		};

		eventSource.onerror = () => {
			if (buildStatus === 'running') {
				buildError = 'Log stream disconnected. Check the server logs.';
				buildStatus = 'failed';
			}
			eventSource?.close();
			eventSource = null;
		};
	}

	function attachRun(id: string) {
		const run = runs.find((item) => item.runId === id);
		runId = id;
		logs = [];
		buildError = '';
		buildStatus = run?.status ?? 'running';
		buildGroup = run?.group ?? buildGroup;
		buildVersion = run?.version ?? buildVersion;
		profileId = run?.profileId ?? profileId;
		openStream(id);
		localStorage.setItem('mineos_buildtools_run', id);
	}

	async function loadRuns() {
		if (runsLoading) return;
		runsLoading = true;
		try {
			const res = await fetch('/api/host/profiles/buildtools/runs');
			if (res.ok) {
				runs = await res.json();
			}
		} finally {
			runsLoading = false;
		}
	}

	$effect(() => {
		searchQuery;
		groupFilter;
		statusFilter;
		sortOption;
		pageSize;
		currentPage = 1;
	});

	$effect(() => {
		if (currentPage > totalPages) currentPage = totalPages;
	});

	onDestroy(() => {
		eventSource?.close();
	});
</script>

<div class="page-header">
	<div class="header-copy">
		<h1>Profiles</h1>
		<p class="subtitle">Manage Minecraft server jars and BuildTools builds.</p>
		<div class="stat-row">
			<div class="stat-chip">
				<span class="stat-label">Total</span>
				<span class="stat-value">{profiles.length}</span>
			</div>
			<div class="stat-chip success">
				<span class="stat-label">Ready</span>
				<span class="stat-value">{downloadedCount}</span>
			</div>
			<div class="stat-chip warning">
				<span class="stat-label">Missing</span>
				<span class="stat-value">{missingCount}</span>
			</div>
		</div>
	</div>
	<div class="header-actions">
		<a class="btn-ghost" href="#buildtools">BuildTools</a>
	</div>
</div>

<div class="profiles-shell">
	<section class="library-panel">
		<div class="library-toolbar">
			<div class="search-field">
				<label for="profile-search">Search profiles</label>
				<input
					id="profile-search"
					type="text"
					bind:value={searchQuery}
					placeholder="Search profiles..."
				/>
			</div>
			<div class="toolbar-row">
				<div class="chip-row">
					{#each profileGroups as group}
						<button
							class="chip"
							class:active={groupFilter === group}
							onclick={() => (groupFilter = group)}
						>
							{group}
						</button>
					{/each}
				</div>
			</div>
			<div class="toolbar-row split">
				<div class="toggle-group">
					<button class:active={statusFilter === 'all'} onclick={() => (statusFilter = 'all')}>
						All
					</button>
					<button
						class:active={statusFilter === 'downloaded'}
						onclick={() => (statusFilter = 'downloaded')}
					>
						Ready
					</button>
					<button class:active={statusFilter === 'missing'} onclick={() => (statusFilter = 'missing')}>
						Missing
					</button>
				</div>
				<div class="select-row">
					<label>
						<span>Sort</span>
						<select bind:value={sortOption}>
							<option value="name">Name</option>
							<option value="group">Group</option>
							<option value="version">Version</option>
						</select>
					</label>
					<label>
						<span>Page size</span>
						<select
							value={pageSize}
							onchange={(event) => {
								pageSize = Number((event.currentTarget as HTMLSelectElement).value);
							}}
						>
							<option value="8">8</option>
							<option value="12">12</option>
							<option value="20">20</option>
							<option value="32">32</option>
						</select>
					</label>
				</div>
			</div>
		</div>

		<div class="library-meta">
			<span class="muted">
				Showing {rangeStart}-{rangeEnd} of {filteredProfiles.length} profiles
			</span>
			<div class="pagination">
				<button
					class="page-btn"
					onclick={() => (currentPage = Math.max(1, currentPage - 1))}
					disabled={currentPage === 1}
				>
					Prev
				</button>
				{#each paginationPages as page}
					{#if page === '...'}
						<span class="page-ellipsis">...</span>
					{:else}
						<button
							class="page-btn"
							class:active={page === currentPage}
							onclick={() => (currentPage = page as number)}
						>
							{page}
						</button>
					{/if}
				{/each}
				<button
					class="page-btn"
					onclick={() => (currentPage = Math.min(totalPages, currentPage + 1))}
					disabled={currentPage === totalPages}
				>
					Next
				</button>
			</div>
		</div>

		{#if data.profiles.error}
			<div class="error-box">
				<p>Failed to load profiles: {data.profiles.error}</p>
			</div>
		{:else if pagedProfiles.length > 0}
			<div class="profiles-grid">
				{#each pagedProfiles as profile}
					<div class="profile-card">
						<div class="card-header">
							<div>
								<h3>{profile.id}</h3>
								<p class="meta">{profile.group} {profile.version}</p>
							</div>
							<span class="badge" class:badge-ready={profile.downloaded}>
								{profile.downloaded ? 'Ready' : 'Not downloaded'}
							</span>
						</div>
						{#if profile.filename}
							<p class="file">{profile.filename}</p>
						{/if}
						<div class="card-actions">
							{#if !profile.downloaded}
								<button
									class="btn-action btn-primary"
									onclick={() => handleDownload(profile.id)}
									disabled={actionLoading[profile.id]}
								>
									Download
								</button>
							{:else}
								<div class="copy-group">
									<select
										value={copyTargets[profile.id] ?? ''}
										onchange={(event) => {
											const value = (event.currentTarget as HTMLSelectElement).value;
											copyTargets[profile.id] = value;
											copyTargets = { ...copyTargets };
										}}
									>
										<option value="">Select server</option>
										{#each servers as server}
											<option value={server.name}>{server.name}</option>
										{/each}
									</select>
									<button
										class="btn-action"
										onclick={() => handleCopy(profile.id)}
										disabled={actionLoading[profile.id] || !copyTargets[profile.id]}
									>
										Copy to server
									</button>
								</div>
							{/if}
							{#if profile.type === 'buildtools'}
								<button
									class="btn-action btn-danger"
									onclick={() => handleDelete(profile.id)}
									disabled={actionLoading[profile.id]}
								>
									Delete
								</button>
							{/if}
						</div>
					</div>
				{/each}
			</div>
		{:else}
			<div class="empty-state">
				<h2>No profiles match your filters</h2>
				<p>Try adjusting the filters or download a new profile.</p>
			</div>
		{/if}
	</section>

	<aside class="buildtools-panel" id="buildtools">
		<div class="buildtools-header">
			<div>
				<h2>BuildTools Station</h2>
				<p>Compile Spigot or CraftBukkit builds and queue them into your library.</p>
			</div>
			<a class="btn-ghost" href="#buildtools-console">Console</a>
		</div>

		<form
			class="buildtools-form"
			onsubmit={(event) => {
				event.preventDefault();
				if (buildStatus !== 'running') {
					startBuild();
				}
			}}
		>
			<label>
				Group
				<select bind:value={buildGroup} disabled={buildStatus === 'running'}>
					<option value="spigot">Spigot</option>
					<option value="craftbukkit">CraftBukkit</option>
				</select>
			</label>
			<label>
				Version
				<select bind:value={buildVersion} disabled={buildStatus === 'running'}>
					<option value="">Select a version...</option>
					{#each commonVersions as version}
						<option value={version}>{version}</option>
					{/each}
				</select>
			</label>
			<button class="btn-primary" type="submit" disabled={buildStatus === 'running'}>
				{buildStatus === 'running' ? 'Building...' : 'Run BuildTools'}
			</button>
		</form>

		{#if buildError}
			<p class="error">{buildError}</p>
		{/if}

		{#if runId}
			<div class="buildtools-status">
				<div>
					<p>Run ID</p>
					<span>{runId}</span>
				</div>
				<div>
					<p>Status</p>
					<span class:status-running={buildStatus === 'running'} class:status-success={buildStatus === 'completed'} class:status-failed={buildStatus === 'failed'}>
						{buildStatus}
					</span>
				</div>
				{#if profileId}
					<div>
						<p>Profile</p>
						<span>{profileId}</span>
					</div>
				{/if}
				<button class="btn-secondary" onclick={() => runId && attachRun(runId)}>
					Open console
				</button>
			</div>
		{/if}

		<div class="run-list">
			<div class="run-list-header">
				<h3>Recent Builds</h3>
				<button class="btn-secondary" onclick={loadRuns} disabled={runsLoading}>
					{runsLoading ? 'Refreshing...' : 'Refresh'}
				</button>
			</div>
			{#if runs.length === 0}
				<p class="run-empty">No BuildTools runs yet.</p>
			{:else}
				<ul>
					{#each runs.slice(0, 5) as run}
						<li>
							<div>
								<div class="run-title">{run.profileId ?? run.runId}</div>
								<div class="run-meta">{run.group} {run.version}</div>
							</div>
							<div class="run-actions">
								<span
									class="run-status"
									class:status-success={run.status === 'completed'}
									class:status-failed={run.status === 'failed'}
								>
									{run.status}
								</span>
								<button class="btn-secondary" onclick={() => attachRun(run.runId)}>
									Open
								</button>
							</div>
						</li>
					{/each}
				</ul>
			{/if}
		</div>

		<div class="console-panel" id="buildtools-console">
			<div class="console-header">
				<h3>Build Console</h3>
				{#if runId}
					<button class="btn-secondary" onclick={() => runId && openStream(runId)}>Reconnect</button>
				{/if}
			</div>
			<div class="log-output" bind:this={logContainer}>
				{#if logs.length === 0}
					<p class="log-placeholder">Select a build run to see output.</p>
				{:else}
					{#each logs as line}
						<div class="log-line">{line}</div>
					{/each}
				{/if}
			</div>
		</div>
	</aside>
</div>

<style>
	.page-header {
		display: flex;
		justify-content: space-between;
		align-items: flex-start;
		margin-bottom: 28px;
		gap: 20px;
	}

	h1 {
		margin: 0 0 8px;
		font-size: 32px;
		font-weight: 600;
	}

	.subtitle {
		margin: 0 0 16px;
		color: #aab2d3;
		font-size: 15px;
	}

	.header-actions {
		display: flex;
		gap: 12px;
	}

	.stat-row {
		display: flex;
		flex-wrap: wrap;
		gap: 10px;
	}

	.stat-chip {
		background: rgba(20, 24, 39, 0.8);
		border: 1px solid rgba(42, 47, 71, 0.8);
		border-radius: 999px;
		padding: 6px 12px;
		display: inline-flex;
		align-items: center;
		gap: 8px;
		font-size: 12px;
		color: #c7cbe0;
	}

	.stat-chip.success {
		background: rgba(106, 176, 76, 0.18);
		border-color: rgba(106, 176, 76, 0.35);
		color: #b7f5a2;
	}

	.stat-chip.warning {
		background: rgba(255, 200, 87, 0.14);
		border-color: rgba(255, 200, 87, 0.35);
		color: #f4c08e;
	}

	.stat-label {
		text-transform: uppercase;
		letter-spacing: 0.06em;
		font-size: 10px;
		color: #9aa2c5;
	}

	.stat-value {
		font-weight: 600;
		font-size: 14px;
		color: #eef0f8;
	}

	.btn-ghost {
		background: rgba(88, 101, 242, 0.12);
		border: 1px solid rgba(88, 101, 242, 0.3);
		color: #c7cbe0;
		border-radius: 10px;
		padding: 10px 16px;
		font-size: 13px;
		text-decoration: none;
		display: inline-flex;
		align-items: center;
		gap: 8px;
	}

	.profiles-shell {
		display: grid;
		grid-template-columns: minmax(0, 1fr) 360px;
		gap: 24px;
		align-items: start;
	}

	.library-panel,
	.buildtools-panel {
		background: #1a1e2f;
		border-radius: 18px;
		padding: 20px;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.35);
		border: 1px solid rgba(42, 47, 71, 0.9);
		min-width: 0;
	}

	.library-toolbar {
		display: flex;
		flex-direction: column;
		gap: 16px;
		padding-bottom: 20px;
		border-bottom: 1px solid rgba(42, 47, 71, 0.6);
		min-width: 0;
	}

	.search-field {
		min-width: 0;
		width: 100%;
		display: flex;
		flex-direction: column;
	}

	.search-field label {
		font-size: 12px;
		color: #aab2d3;
		margin-bottom: 6px;
		display: block;
	}

	.search-field input {
		width: 100%;
		max-width: 100%;
		min-width: 0;
		box-sizing: border-box;
	}

	.toolbar-row {
		display: flex;
		flex-wrap: wrap;
		gap: 12px;
	}

	.toolbar-row.split {
		justify-content: space-between;
		align-items: center;
	}

	.select-row {
		display: flex;
		gap: 12px;
		flex-wrap: wrap;
	}

	.select-row label {
		display: flex;
		flex-direction: column;
		gap: 6px;
		font-size: 12px;
		color: #aab2d3;
	}

	label {
		display: flex;
		flex-direction: column;
		gap: 6px;
		font-size: 13px;
		color: #aab2d3;
	}

	input,
	select {
		background: #141827;
		border: 1px solid #2a2f47;
		border-radius: 10px;
		padding: 10px 12px;
		color: #eef0f8;
		font-family: inherit;
		font-size: 14px;
	}

	.chip-row {
		display: flex;
		flex-wrap: wrap;
		gap: 8px;
	}

	.chip {
		background: #141827;
		border: 1px solid #2a2f47;
		color: #9aa2c5;
		padding: 6px 12px;
		border-radius: 999px;
		font-size: 12px;
		cursor: pointer;
		transition: all 0.2s;
	}

	.chip.active {
		background: rgba(106, 176, 76, 0.2);
		border-color: rgba(106, 176, 76, 0.45);
		color: #b7f5a2;
	}

	.toggle-group {
		display: flex;
		gap: 6px;
		background: #141827;
		border-radius: 10px;
		padding: 4px;
	}

	.toggle-group button {
		background: transparent;
		border: none;
		color: #8890b1;
		padding: 6px 12px;
		border-radius: 8px;
		cursor: pointer;
		font-size: 12px;
	}

	.toggle-group button.active {
		background: rgba(106, 176, 76, 0.2);
		color: #eef0f8;
	}

	.library-meta {
		display: flex;
		justify-content: space-between;
		align-items: center;
		gap: 12px;
		padding: 16px 0;
		flex-wrap: wrap;
	}

	.pagination {
		display: flex;
		align-items: center;
		gap: 6px;
		flex-wrap: wrap;
	}

	.page-btn {
		background: #2b2f45;
		color: #d4d9f1;
		border: none;
		border-radius: 8px;
		padding: 6px 10px;
		font-size: 12px;
		cursor: pointer;
	}

	.page-btn.active {
		background: rgba(106, 176, 76, 0.25);
		color: #eef0f8;
	}

	.page-btn:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.page-ellipsis {
		color: #6f789b;
		font-size: 12px;
		padding: 0 6px;
	}

	.muted {
		color: #8e96bb;
		font-size: 12px;
	}

	.btn-primary {
		background: var(--mc-grass);
		color: #fff;
		border: none;
		border-radius: 10px;
		padding: 12px 20px;
		font-size: 14px;
		font-weight: 600;
		cursor: pointer;
		display: inline-flex;
		align-items: center;
		justify-content: center;
		text-decoration: none;
	}

	.btn-primary:disabled {
		opacity: 0.6;
		cursor: not-allowed;
	}

	.btn-secondary {
		background: #2b2f45;
		color: #d4d9f1;
		border: none;
		border-radius: 8px;
		padding: 8px 12px;
		font-size: 12px;
		cursor: pointer;
		text-decoration: none;
		display: inline-flex;
		align-items: center;
		justify-content: center;
	}

	.error-box {
		background: rgba(255, 92, 92, 0.1);
		border: 1px solid rgba(255, 92, 92, 0.3);
		border-radius: 12px;
		padding: 16px 20px;
		color: #ff9f9f;
	}

	.error {
		color: #ff9f9f;
		margin-top: 8px;
		font-size: 13px;
	}

	.profiles-grid {
		display: grid;
		grid-template-columns: repeat(auto-fill, minmax(300px, 1fr));
		gap: 20px;
	}

	.profile-card {
		background: #141827;
		border-radius: 16px;
		padding: 18px;
		display: flex;
		flex-direction: column;
		gap: 12px;
		border: 1px solid rgba(42, 47, 71, 0.8);
		box-shadow: inset 0 0 0 1px rgba(106, 176, 76, 0.05);
	}

	.card-header {
		display: flex;
		justify-content: space-between;
		align-items: center;
		gap: 12px;
	}

	.card-header h3 {
		margin: 0;
		font-size: 18px;
	}

	.meta {
		margin: 4px 0 0;
		font-size: 12px;
		color: #9aa2c5;
	}

	.file {
		margin: 0;
		font-size: 12px;
		color: #c9d1d9;
		background: rgba(20, 24, 39, 0.8);
		border-radius: 8px;
		padding: 8px 10px;
		border: 1px solid rgba(42, 47, 71, 0.8);
	}

	.badge {
		padding: 4px 10px;
		border-radius: 999px;
		font-size: 12px;
		background: rgba(255, 159, 159, 0.15);
		color: #ff9f9f;
	}

	.badge-ready {
		background: rgba(106, 176, 76, 0.2);
		color: #b7f5a2;
	}

	.card-actions {
		display: flex;
		flex-wrap: wrap;
		gap: 8px;
		align-items: center;
	}

	.copy-group {
		display: flex;
		gap: 8px;
		flex: 1;
		flex-wrap: wrap;
	}

	.copy-group select {
		flex: 1;
		min-width: 140px;
	}

	.btn-action {
		background: #2b2f45;
		color: #d4d9f1;
		border: none;
		border-radius: 6px;
		padding: 8px 12px;
		font-size: 13px;
		cursor: pointer;
	}

	.btn-action:disabled {
		opacity: 0.6;
		cursor: not-allowed;
	}

	.btn-danger {
		background: rgba(255, 92, 92, 0.15);
		color: #ff9f9f;
	}

	.empty-state {
		text-align: center;
		padding: 60px 20px;
		color: #8e96bb;
		background: #141827;
		border-radius: 16px;
		border: 1px dashed rgba(106, 176, 76, 0.2);
	}

	.buildtools-panel {
		display: flex;
		flex-direction: column;
		gap: 16px;
		background: linear-gradient(160deg, rgba(20, 24, 39, 0.95), rgba(18, 21, 33, 0.95));
		position: sticky;
		top: 24px;
	}

	.buildtools-header {
		display: flex;
		justify-content: space-between;
		align-items: flex-start;
		gap: 12px;
	}

	.buildtools-header h2 {
		margin: 0 0 4px;
		font-size: 18px;
	}

	.buildtools-header p {
		margin: 0;
		color: #9aa2c5;
		font-size: 13px;
	}

	.buildtools-form {
		display: flex;
		flex-direction: column;
		gap: 12px;
		padding: 12px;
		background: rgba(20, 24, 39, 0.6);
		border-radius: 12px;
		border: 1px solid rgba(42, 47, 71, 0.6);
	}

	.buildtools-status {
		display: grid;
		grid-template-columns: repeat(auto-fit, minmax(120px, 1fr));
		gap: 10px;
		background: rgba(20, 24, 39, 0.7);
		border-radius: 12px;
		padding: 12px;
		border: 1px solid rgba(42, 47, 71, 0.6);
	}

	.buildtools-status p {
		margin: 0;
		font-size: 11px;
		color: #8e96bb;
		text-transform: uppercase;
		letter-spacing: 0.06em;
	}

	.buildtools-status span {
		font-size: 13px;
		color: #eef0f8;
		font-weight: 600;
	}

	.status-running {
		color: #f0c674;
	}

	.status-success {
		color: #b7f5a2;
	}

	.status-failed {
		color: #ff9f9f;
	}

	.run-list {
		display: flex;
		flex-direction: column;
		gap: 12px;
	}

	.run-list-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 8px;
	}

	.run-list ul {
		list-style: none;
		padding: 0;
		margin: 0;
		display: grid;
		gap: 10px;
	}

	.run-list li {
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 12px;
		background: #141827;
		border-radius: 12px;
		padding: 12px;
		border: 1px solid rgba(42, 47, 71, 0.8);
	}

	.run-title {
		font-weight: 600;
		color: #eef0f8;
		font-size: 13px;
	}

	.run-meta {
		font-size: 12px;
		color: #9aa2c5;
	}

	.run-actions {
		display: flex;
		align-items: center;
		gap: 8px;
	}

	.run-status {
		font-size: 12px;
		color: #f0c674;
	}

	.run-empty {
		margin: 0;
		color: #9aa2c5;
		font-size: 13px;
	}

	.console-panel {
		display: flex;
		flex-direction: column;
		gap: 10px;
		background: #0f121e;
		border-radius: 14px;
		padding: 14px;
		border: 1px solid rgba(42, 47, 71, 0.8);
	}

	.console-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 8px;
	}

	.console-header h3 {
		margin: 0;
		font-size: 14px;
		color: #c9d1d9;
	}

	.log-output {
		background: #0b0e18;
		border: 1px solid rgba(42, 47, 71, 0.6);
		border-radius: 12px;
		padding: 12px;
		height: 260px;
		overflow-y: auto;
		font-family: "Cascadia Code", "Fira Code", "Consolas", monospace;
		font-size: 12px;
		color: #d4d9f1;
	}

	.log-line {
		white-space: pre-wrap;
		word-break: break-word;
	}

	.log-placeholder {
		margin: 0;
		color: #6f789b;
	}

	.link-action {
		color: #a5b4fc;
		font-size: 12px;
		text-decoration: none;
		align-self: center;
	}

	@media (max-width: 1080px) {
		.profiles-shell {
			grid-template-columns: 1fr;
		}

		.buildtools-panel {
			position: static;
		}
	}

	@media (max-width: 720px) {
		.page-header {
			flex-direction: column;
			align-items: flex-start;
		}

		.toolbar-row.split {
			flex-direction: column;
			align-items: flex-start;
		}
	}
</style>
