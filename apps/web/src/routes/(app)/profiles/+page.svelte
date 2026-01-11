<script lang="ts">
	import { invalidateAll } from '$app/navigation';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	let actionLoading = $state<Record<string, boolean>>({});
	let copyTargets = $state<Record<string, string>>({});
	let searchQuery = $state('');
	let groupFilter = $state('all');
	let statusFilter = $state<'all' | 'downloaded' | 'missing'>('all');
	let sortOption = $state<'name' | 'group' | 'version'>('name');

	const profiles = $derived(data.profiles.data ?? []);
	const servers = $derived(data.servers.data ?? []);

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

	async function handleDownload(profileId: string) {
		actionLoading[profileId] = true;
		try {
			const res = await fetch(`/api/host/profiles/${profileId}/download`, { method: 'POST' });
			if (!res.ok) {
				const error = await res.json().catch(() => ({ error: 'Failed to download profile' }));
				alert(error.error || 'Failed to download profile');
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
			alert('Select a server to copy this profile to.');
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
				alert(error.error || 'Failed to copy profile');
			} else {
				await invalidateAll();
			}
		} finally {
			delete actionLoading[profileId];
			actionLoading = { ...actionLoading };
		}
	}

	async function handleDelete(profileId: string) {
		if (!confirm(`Delete BuildTools profile "${profileId}"?`)) {
			return;
		}

		actionLoading[profileId] = true;
		try {
			const res = await fetch(`/api/host/profiles/buildtools/${profileId}`, {
				method: 'DELETE'
			});
			if (!res.ok) {
				const error = await res.json().catch(() => ({ error: 'Failed to delete profile' }));
				alert(error.error || 'Failed to delete profile');
			} else {
				await invalidateAll();
			}
		} finally {
			delete actionLoading[profileId];
			actionLoading = { ...actionLoading };
		}
	}
</script>

<div class="page-header">
	<div>
		<h1>Profiles</h1>
		<p class="subtitle">Manage Minecraft server jars and BuildTools builds</p>
	</div>
</div>

<div class="profiles-layout">
	<aside class="side-panel">
		<div class="filter-card">
			<h2>Filters</h2>
			<label class="filter-field">
				<span>Search</span>
				<input type="text" bind:value={searchQuery} placeholder="Search profiles..." />
			</label>
			<div class="filter-field">
				<span>Group</span>
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
			<div class="filter-field">
				<span>Status</span>
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
			</div>
			<label class="filter-field">
				<span>Sort by</span>
				<select bind:value={sortOption}>
					<option value="name">Name</option>
					<option value="group">Group</option>
					<option value="version">Version</option>
				</select>
			</label>
		</div>

		<div class="buildtools-card">
			<h2>BuildTools</h2>
			<p>Compile Spigot or CraftBukkit jars with live terminal output.</p>
			<a class="btn-primary" href="/profiles/buildtools">Open BuildTools Console</a>
		</div>
	</aside>

	<section class="profiles-section">
		{#if data.profiles.error}
			<div class="error-box">
				<p>Failed to load profiles: {data.profiles.error}</p>
			</div>
		{:else if filteredProfiles.length > 0}
			<div class="profiles-grid">
				{#each filteredProfiles as profile}
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
</div>

<style>
	.page-header {
		display: flex;
		justify-content: space-between;
		align-items: center;
		margin-bottom: 24px;
	}

	h1 {
		margin: 0 0 8px;
		font-size: 32px;
		font-weight: 600;
	}

	.subtitle {
		margin: 0;
		color: #aab2d3;
		font-size: 15px;
	}

	.profiles-layout {
		display: grid;
		grid-template-columns: minmax(240px, 320px) 1fr;
		gap: 24px;
		align-items: start;
	}

	.side-panel {
		display: flex;
		flex-direction: column;
		gap: 20px;
		position: sticky;
		top: 24px;
		align-self: start;
	}

	.filter-card,
	.buildtools-card {
		background: #1a1e2f;
		border-radius: 16px;
		padding: 20px;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.35);
		border: 1px solid rgba(106, 176, 76, 0.12);
	}

	.filter-card h2,
	.buildtools-card h2 {
		margin: 0 0 12px;
		font-size: 18px;
	}

	.buildtools-card p {
		margin: 0 0 16px;
		color: #9aa2c5;
	}

	.filter-field {
		display: flex;
		flex-direction: column;
		gap: 8px;
		margin-bottom: 16px;
		font-size: 13px;
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
		border-radius: 8px;
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


	.btn-primary {
		background: var(--mc-grass);
		color: #fff;
		border: none;
		border-radius: 8px;
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

	.profiles-section {
		min-width: 0;
	}

	.profiles-grid {
		display: grid;
		grid-template-columns: repeat(auto-fill, minmax(300px, 1fr));
		gap: 20px;
	}

	.profile-card {
		background: #1a1e2f;
		border-radius: 16px;
		padding: 18px;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.35);
		display: flex;
		flex-direction: column;
		gap: 12px;
		border: 1px solid rgba(42, 47, 71, 0.9);
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
		background: #1a1e2f;
		border-radius: 16px;
		border: 1px dashed rgba(106, 176, 76, 0.2);
	}

	@media (max-width: 960px) {
		.profiles-layout {
			grid-template-columns: 1fr;
		}

		.side-panel {
			position: static;
		}
	}
</style>
