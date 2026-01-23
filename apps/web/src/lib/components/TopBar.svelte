<script lang="ts">
	import { goto } from '$app/navigation';
	import NotificationMenu from './NotificationMenu.svelte';
	import type { Profile, ServerSummary } from '$lib/api/types';

	let {
		user,
		servers = [],
		profiles = []
	}: {
		user: { username: string; role: string } | null;
		servers: ServerSummary[];
		profiles: Profile[];
	} = $props();

	let query = $state('');
	let showResults = $state(false);
	let searchInput: HTMLInputElement | null = $state(null);

	const minQueryLength = 2;
	const normalizedQuery = $derived(query.trim().toLowerCase());

	const filteredServers = $derived.by(() => {
		if (normalizedQuery.length < minQueryLength) return [];
		return servers.filter((server) => server.name.toLowerCase().includes(normalizedQuery)).slice(0, 5);
	});

	const filteredProfiles = $derived.by(() => {
		if (normalizedQuery.length < minQueryLength) return [];
		return profiles
			.filter((profile) => {
				const haystack = `${profile.id} ${profile.group} ${profile.version}`.toLowerCase();
				return haystack.includes(normalizedQuery);
			})
			.slice(0, 5);
	});

	function handleSearchFocus() {
		showResults = true;
	}

	function handleSearchBlur() {
		// Delay to allow clicks on results
		setTimeout(() => {
			showResults = false;
		}, 200);
	}

	function handleResultClick(href: string) {
		showResults = false;
		query = '';
		goto(href);
	}

	function handleClickOutside(event: MouseEvent) {
		const target = event.target as HTMLElement;
		if (!target.closest('.topbar-search')) {
			showResults = false;
		}
	}
</script>

<svelte:window onclick={handleClickOutside} />

<div class="topbar">
	<div class="topbar-search">
		<input
			bind:this={searchInput}
			type="text"
			placeholder="Search servers, profiles..."
			bind:value={query}
			onfocus={handleSearchFocus}
			onblur={handleSearchBlur}
		/>
		<span class="search-icon">🔍</span>

		{#if showResults && normalizedQuery.length >= minQueryLength}
			<div class="search-results">
				{#if filteredServers.length === 0 && filteredProfiles.length === 0}
					<div class="no-results">No results found</div>
				{:else}
					{#if filteredServers.length > 0}
						<div class="results-section">
							<div class="section-header">Servers</div>
							{#each filteredServers as server}
								<button
									class="result-item"
									onclick={() => handleResultClick(`/servers/${server.name}`)}
								>
									<span class="result-name">{server.name}</span>
									<span class="result-meta" class:running={server.up}>
										{server.up ? '🟢 Running' : '⚫ Stopped'}
									</span>
								</button>
							{/each}
						</div>
					{/if}

					{#if filteredProfiles.length > 0}
						<div class="results-section">
							<div class="section-header">Profiles</div>
							{#each filteredProfiles as profile}
								<div class="result-item profile">
									<span class="result-name">{profile.id}</span>
									<span class="result-meta">{profile.group} {profile.version}</span>
								</div>
							{/each}
						</div>
					{/if}
				{/if}
			</div>
		{/if}
	</div>

	<div class="topbar-actions">
		<NotificationMenu />
		<a class="user-info" href="/profile">
			<span class="user-icon">👤</span>
			<span class="username">{user?.username || 'Unknown'}</span>
		</a>
	</div>
</div>

<style>
	.topbar {
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 24px;
		padding: 12px 24px;
		background: linear-gradient(180deg, #171c28 0%, #141827 100%);
		border-bottom: 1px solid #2a2f47;
		position: sticky;
		top: 0;
		z-index: 100;
	}

	.topbar-search {
		position: relative;
		flex: 1;
		max-width: 500px;
	}

	.topbar-search input {
		width: 100%;
		background: #1a1f33;
		border: 1px solid #2a2f47;
		border-radius: 8px;
		padding: 10px 40px 10px 16px;
		color: #eef0f8;
		font-size: 14px;
		font-family: inherit;
		transition: all 0.2s;
	}

	.topbar-search input:focus {
		outline: none;
		border-color: rgba(106, 176, 76, 0.5);
		box-shadow: 0 0 0 2px rgba(106, 176, 76, 0.1);
	}

	.topbar-search input::placeholder {
		color: #7c87b2;
	}

	.search-icon {
		position: absolute;
		right: 12px;
		top: 50%;
		transform: translateY(-50%);
		font-size: 16px;
		opacity: 0.5;
	}

	.search-results {
		position: absolute;
		top: calc(100% + 8px);
		left: 0;
		right: 0;
		background: #141827;
		border: 1px solid #2a2f47;
		border-radius: 12px;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.4);
		max-height: 400px;
		overflow-y: auto;
		z-index: 1000;
	}

	.no-results {
		padding: 20px;
		text-align: center;
		color: #8890b1;
		font-size: 14px;
	}

	.results-section {
		padding: 8px 0;
		border-bottom: 1px solid #2a2f47;
	}

	.results-section:last-child {
		border-bottom: none;
	}

	.section-header {
		padding: 8px 16px;
		font-size: 12px;
		font-weight: 600;
		color: #7c87b2;
		text-transform: uppercase;
		letter-spacing: 0.05em;
	}

	.result-item {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 10px 16px;
		cursor: pointer;
		transition: background 0.2s;
		border: none;
		background: none;
		width: 100%;
		text-align: left;
		font-family: inherit;
	}

	.result-item:hover {
		background: rgba(106, 176, 76, 0.08);
	}

	.result-item.profile {
		cursor: default;
	}

	.result-item.profile:hover {
		background: transparent;
	}

	.result-name {
		color: #eef0f8;
		font-size: 14px;
		font-weight: 500;
	}

	.result-meta {
		color: #9aa2c5;
		font-size: 12px;
	}

	.result-meta.running {
		color: #b7f5a2;
	}

	.topbar-actions {
		display: flex;
		align-items: center;
		gap: 16px;
	}

	.user-info {
		display: flex;
		align-items: center;
		gap: 8px;
		padding: 8px 12px;
		background: rgba(255, 255, 255, 0.03);
		border-radius: 8px;
		border: 1px solid #2a2f47;
		text-decoration: none;
	}

	.user-info:hover {
		background: rgba(255, 255, 255, 0.08);
		border-color: rgba(106, 176, 76, 0.4);
	}

	.user-icon {
		font-size: 16px;
	}

	.username {
		color: #eef0f8;
		font-size: 14px;
		font-weight: 500;
	}

	@media (max-width: 768px) {
		.topbar {
			flex-direction: column;
			gap: 12px;
		}

		.topbar-search {
			max-width: 100%;
		}

		.topbar-actions {
			width: 100%;
			justify-content: space-between;
		}
	}
</style>
