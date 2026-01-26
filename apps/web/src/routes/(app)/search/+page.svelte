<script lang="ts">
	import type { PageData } from './$types';
	import type { Profile, ServerSummary } from '$lib/api/types';
	import { withBase } from '$lib/utils/paths';

	let { data }: { data: PageData } = $props();

	let query = $state(data.query ?? '');
	let updateTimer: ReturnType<typeof setTimeout> | null = null;

	const minQueryLength = 2;

	const servers = $derived((data.servers.data ?? []) as ServerSummary[]);
	const profiles = $derived((data.profiles.data ?? []) as Profile[]);
	const normalizedQuery = $derived(query.trim().toLowerCase());

	const filteredServers = $derived.by(() => {
		if (normalizedQuery.length < minQueryLength) return [];
		return servers.filter((server) => server.name.toLowerCase().includes(normalizedQuery));
	});

	const filteredProfiles = $derived.by(() => {
		if (normalizedQuery.length < minQueryLength) return [];
		return profiles.filter((profile) => {
			const haystack = `${profile.id} ${profile.group} ${profile.version}`.toLowerCase();
			return haystack.includes(normalizedQuery);
		});
	});

	function scheduleUrlUpdate() {
		if (updateTimer) clearTimeout(updateTimer);
		updateTimer = setTimeout(() => {
			if (typeof window === 'undefined') return;
			const params = new URLSearchParams(window.location.search);
			if (query.trim().length >= minQueryLength) {
				params.set('q', query.trim());
			} else {
				params.delete('q');
			}
			const nextUrl = `${window.location.pathname}${params.toString() ? `?${params.toString()}` : ''}`;
			window.history.replaceState({}, '', nextUrl);
		}, 300);
	}
</script>

<div class="search-page">
	<div class="page-header">
		<div>
			<h1>Global Search</h1>
			<p class="subtitle">Find servers and profiles from one place</p>
		</div>
	</div>

	<div class="search-input">
		<input
			type="text"
			placeholder="Search servers, profiles..."
			bind:value={query}
			oninput={scheduleUrlUpdate}
		/>
		<span class="hint">Type at least {minQueryLength} characters</span>
	</div>

	{#if data.servers.error || data.profiles.error}
		<div class="error-box">
			<p>{data.servers.error ?? data.profiles.error}</p>
		</div>
	{/if}

	{#if normalizedQuery.length < minQueryLength}
		<div class="empty-state">
			<p>Start typing to see matching servers and profiles.</p>
		</div>
	{:else}
		<div class="results-grid">
			<div class="result-card">
				<h2>Servers</h2>
				{#if filteredServers.length === 0}
					<p class="muted">No matching servers.</p>
				{:else}
					<ul class="result-list">
						{#each filteredServers as server}
							<li>
								<a href={withBase(`/servers/${server.name}`)} class="result-link">
									<span>{server.name}</span>
									<span class="meta">{server.up ? 'Running' : 'Stopped'}</span>
								</a>
							</li>
						{/each}
					</ul>
				{/if}
			</div>

			<div class="result-card">
				<h2>Profiles</h2>
				{#if filteredProfiles.length === 0}
					<p class="muted">No matching profiles.</p>
				{:else}
					<ul class="result-list">
						{#each filteredProfiles as profile}
							<li class="profile-item">
								<span>
									<strong>{profile.id}</strong>
									<span class="meta">{profile.group} {profile.version}</span>
								</span>
								<span class="status" class:ready={profile.downloaded}>
									{profile.downloaded ? 'Ready' : 'Not downloaded'}
								</span>
							</li>
						{/each}
					</ul>
				{/if}
			</div>
		</div>
	{/if}
</div>

<style>
	.search-page {
		display: flex;
		flex-direction: column;
		gap: 24px;
		max-width: 1100px;
	}

	.page-header h1 {
		margin: 0 0 8px;
		font-size: 32px;
		font-weight: 600;
	}

	.subtitle {
		margin: 0;
		color: #aab2d3;
		font-size: 15px;
	}

	.search-input {
		display: flex;
		flex-direction: column;
		gap: 8px;
	}

	.search-input input {
		background: #141827;
		border: 1px solid #2a2f47;
		border-radius: 12px;
		padding: 14px 16px;
		color: #eef0f8;
		font-size: 15px;
	}

	.search-input input:focus {
		outline: none;
		border-color: rgba(106, 176, 76, 0.6);
		box-shadow: 0 0 0 2px rgba(106, 176, 76, 0.15);
	}

	.hint {
		font-size: 12px;
		color: #8e96bb;
	}

	.results-grid {
		display: grid;
		grid-template-columns: repeat(auto-fit, minmax(320px, 1fr));
		gap: 20px;
	}

	.result-card {
		background: #1a1e2f;
		border-radius: 16px;
		padding: 20px;
		border: 1px solid rgba(106, 176, 76, 0.12);
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.35);
	}

	.result-card h2 {
		margin: 0 0 16px;
		font-size: 18px;
		color: #eef0f8;
	}

	.result-list {
		list-style: none;
		padding: 0;
		margin: 0;
		display: flex;
		flex-direction: column;
		gap: 12px;
	}

	.result-link {
		display: flex;
		justify-content: space-between;
		align-items: center;
		text-decoration: none;
		color: #eef0f8;
		padding: 10px 12px;
		border-radius: 10px;
		background: rgba(20, 24, 39, 0.8);
		border: 1px solid rgba(42, 47, 71, 0.8);
		transition: background 0.2s;
	}

	.result-link:hover {
		background: rgba(106, 176, 76, 0.08);
	}

	.profile-item {
		display: flex;
		justify-content: space-between;
		align-items: center;
		padding: 10px 12px;
		border-radius: 10px;
		background: rgba(20, 24, 39, 0.8);
		border: 1px solid rgba(42, 47, 71, 0.8);
		color: #eef0f8;
	}

	.meta {
		font-size: 12px;
		color: #9aa2c5;
		margin-left: 8px;
	}

	.status {
		font-size: 12px;
		color: #ffb6b6;
		background: rgba(255, 159, 159, 0.12);
		padding: 4px 10px;
		border-radius: 999px;
	}

	.status.ready {
		color: #b7f5a2;
		background: rgba(106, 176, 76, 0.2);
	}

	.muted {
		color: #8890b1;
		margin: 0;
	}

	.empty-state {
		background: #1a1e2f;
		border-radius: 16px;
		padding: 32px;
		color: #8e96bb;
		border: 1px dashed rgba(106, 176, 76, 0.25);
	}

	.error-box {
		background: rgba(255, 92, 92, 0.1);
		border: 1px solid rgba(255, 92, 92, 0.3);
		border-radius: 12px;
		padding: 16px 20px;
		color: #ff9f9f;
	}
</style>
