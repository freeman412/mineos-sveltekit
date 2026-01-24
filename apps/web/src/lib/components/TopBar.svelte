<script lang="ts">
	import { goto } from '$app/navigation';
	import { browser } from '$app/environment';
	import { onMount } from 'svelte';
	import NotificationMenu from './NotificationMenu.svelte';
	import type { Profile, ServerSummary, ServerDetail } from '$lib/api/types';
	import * as api from '$lib/api/client';

	let {
		user,
		servers = [],
		profiles = []
	}: {
		user: { username: string; role: string } | null;
		servers: ServerSummary[] | ServerDetail[];
		profiles: Profile[];
	} = $props();

	let query = $state('');
	let showResults = $state(false);
	let searchInput: HTMLInputElement | null = $state(null);
	let activeFilter = $state<'all' | 'servers' | 'profiles'>('all');
	let focusedIndex = $state(-1);
	let recentQueries = $state<string[]>([]);
	let recentServerNames = $state<string[]>([]);
	let recentProfileIds = $state<string[]>([]);
	let actionBusy = $state<string | null>(null);

	const minQueryLength = 2;
	const maxResults = 8;
	const maxRecent = 5;
	const normalizedQuery = $derived(query.trim().toLowerCase());
	const showRecent = $derived.by(() => normalizedQuery.length < minQueryLength);

	type SearchResult = {
		key: string;
		type: 'server' | 'profile';
		label: string;
		meta: string;
		href: string;
		server?: ServerSummary | ServerDetail;
		profile?: Profile;
	};

	type HighlightPart = {
		text: string;
		match: boolean;
	};

	const recentQueryKey = 'mineos_recent_queries';
	const recentServerKey = 'mineos_recent_servers';
	const recentProfileKey = 'mineos_recent_profiles';

	onMount(() => {
		if (!browser) return;
		recentQueries = loadRecent(recentQueryKey);
		recentServerNames = loadRecent(recentServerKey);
		recentProfileIds = loadRecent(recentProfileKey);
	});

	function loadRecent(key: string): string[] {
		if (!browser) return [];
		try {
			const raw = localStorage.getItem(key);
			if (!raw) return [];
			const parsed = JSON.parse(raw);
			if (Array.isArray(parsed)) {
				return parsed.filter((value) => typeof value === 'string');
			}
		} catch {
			// Ignore localStorage errors.
		}
		return [];
	}

	function saveRecent(key: string, values: string[]) {
		if (!browser) return;
		localStorage.setItem(key, JSON.stringify(values));
	}

	function recordRecentQuery(value: string) {
		const trimmed = value.trim();
		if (trimmed.length < minQueryLength) return;
		const next = [
			trimmed,
			...recentQueries.filter((entry) => entry.toLowerCase() !== trimmed.toLowerCase())
		].slice(0, maxRecent);
		recentQueries = next;
		saveRecent(recentQueryKey, next);
	}

	function recordRecentServer(name: string) {
		const trimmed = name.trim();
		if (!trimmed) return;
		const next = [
			trimmed,
			...recentServerNames.filter((entry) => entry.toLowerCase() !== trimmed.toLowerCase())
		].slice(0, maxRecent);
		recentServerNames = next;
		saveRecent(recentServerKey, next);
	}

	function recordRecentProfile(id: string) {
		const trimmed = id.trim();
		if (!trimmed) return;
		const next = [
			trimmed,
			...recentProfileIds.filter((entry) => entry.toLowerCase() !== trimmed.toLowerCase())
		].slice(0, maxRecent);
		recentProfileIds = next;
		saveRecent(recentProfileKey, next);
	}

	function buildServerResult(server: ServerSummary | ServerDetail): SearchResult {
		const isRunning = 'up' in server ? server.up : server.status === 'running';
		return {
			key: `server:${server.name}`,
			type: 'server',
			label: server.name,
			meta: isRunning ? 'RUNNING' : 'STOPPED',
			href: `/servers/${encodeURIComponent(server.name)}`,
			server
		};
	}

	function buildProfileResult(profile: Profile): SearchResult {
		return {
			key: `profile:${profile.id}`,
			type: 'profile',
			label: profile.id,
			meta: `${profile.group} ${profile.version}`,
			href: '/profiles',
			profile
		};
	}

	const recentServerResults = $derived.by(() => {
		if (!showRecent || activeFilter === 'profiles') return [];
		const map = new Map(servers.map((server) => [server.name.toLowerCase(), server]));
		return recentServerNames
			.map((name) => map.get(name.toLowerCase()))
			.filter((server): server is ServerSummary | ServerDetail => !!server)
			.map(buildServerResult);
	});

	const recentProfileResults = $derived.by(() => {
		if (!showRecent || activeFilter === 'servers') return [];
		const map = new Map(profiles.map((profile) => [profile.id.toLowerCase(), profile]));
		return recentProfileIds
			.map((id) => map.get(id.toLowerCase()))
			.filter((profile): profile is Profile => !!profile)
			.map(buildProfileResult);
	});

	const serverResults = $derived.by(() => {
		if (showRecent || activeFilter === 'profiles') return [];
		if (normalizedQuery.length < minQueryLength) return [];
		return servers
			.filter((server) => server.name.toLowerCase().includes(normalizedQuery))
			.slice(0, maxResults)
			.map(buildServerResult);
	});

	const profileResults = $derived.by(() => {
		if (showRecent || activeFilter === 'servers') return [];
		if (normalizedQuery.length < minQueryLength) return [];
		return profiles
			.filter((profile) => {
				const haystack = `${profile.id} ${profile.group} ${profile.version}`.toLowerCase();
				return haystack.includes(normalizedQuery);
			})
			.slice(0, maxResults)
			.map(buildProfileResult);
	});

	const visibleServers = $derived.by(() => (showRecent ? recentServerResults : serverResults));
	const visibleProfiles = $derived.by(() => (showRecent ? recentProfileResults : profileResults));
	const visibleResults = $derived.by(() => [...visibleServers, ...visibleProfiles]);
	const focusedKey = $derived.by(() =>
		focusedIndex >= 0 && focusedIndex < visibleResults.length
			? visibleResults[focusedIndex].key
			: null
	);
	const hasRecentResults = $derived.by(
		() => recentQueries.length > 0 || recentServerResults.length > 0 || recentProfileResults.length > 0
	);
	const hasSearchResults = $derived.by(() => serverResults.length > 0 || profileResults.length > 0);

	$effect(() => {
		normalizedQuery;
		activeFilter;
		showRecent;
		focusedIndex = -1;
	});

	function getHighlightParts(text: string): HighlightPart[] {
		const q = normalizedQuery;
		if (q.length < minQueryLength) {
			return [{ text, match: false }];
		}
		const lower = text.toLowerCase();
		const index = lower.indexOf(q);
		if (index < 0) {
			return [{ text, match: false }];
		}
		return [
			{ text: text.slice(0, index), match: false },
			{ text: text.slice(index, index + q.length), match: true },
			{ text: text.slice(index + q.length), match: false }
		];
	}

	function handleSearchFocus() {
		showResults = true;
	}

	function handleSearchInput() {
		showResults = true;
	}

	function handleSearchBlur() {
		// Delay to allow clicks on results
		setTimeout(() => {
			showResults = false;
			focusedIndex = -1;
		}, 200);
	}

	function handleResultSelect(result: SearchResult) {
		recordRecentQuery(query);
		if (result.type === 'server') {
			recordRecentServer(result.label);
		} else if (result.type === 'profile') {
			recordRecentProfile(result.label);
		}
		showResults = false;
		focusedIndex = -1;
		query = '';
		goto(result.href);
	}

	function handleRecentQueryClick(value: string) {
		query = value;
		showResults = true;
		searchInput?.focus();
	}

	function moveFocus(delta: number) {
		if (visibleResults.length === 0) return;
		let next = focusedIndex + delta;
		if (next < 0) next = visibleResults.length - 1;
		if (next >= visibleResults.length) next = 0;
		focusedIndex = next;
	}

	function handleKeydown(event: KeyboardEvent) {
		if (!showResults) return;

		switch (event.key) {
			case 'ArrowDown':
				event.preventDefault();
				moveFocus(1);
				break;
			case 'ArrowUp':
				event.preventDefault();
				moveFocus(-1);
				break;
			case 'Enter':
				if (focusedIndex >= 0 && focusedIndex < visibleResults.length) {
					event.preventDefault();
					handleResultSelect(visibleResults[focusedIndex]);
				} else {
					recordRecentQuery(query);
				}
				break;
			case 'Escape':
				showResults = false;
				focusedIndex = -1;
				break;
		}
	}

	async function handleServerAction(
		event: MouseEvent,
		server: ServerSummary | ServerDetail,
		action: 'start' | 'stop' | 'restart'
	) {
		event.preventDefault();
		event.stopPropagation();

		const key = `${server.name}:${action}`;
		if (actionBusy === key) return;

		actionBusy = key;
		try {
			let result;
			if (action === 'start') {
				result = await api.startServer(fetch, server.name);
			} else if (action === 'stop') {
				result = await api.stopServer(fetch, server.name);
			} else {
				result = await api.restartServer(fetch, server.name);
			}
			if (result?.error) {
				console.error(result.error);
			}
		} catch (err) {
			console.error('Failed to perform server action:', err);
		} finally {
			actionBusy = null;
		}
	}

	function isActionBusy(
		server: ServerSummary | ServerDetail,
		action: 'start' | 'stop' | 'restart'
	) {
		return actionBusy === `${server.name}:${action}`;
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
			oninput={handleSearchInput}
			onkeydown={handleKeydown}
		/>
		<span class="search-icon" aria-hidden="true">
			<svg viewBox="0 0 24 24" focusable="false" aria-hidden="true">
				<circle cx="11" cy="11" r="7" fill="none" stroke="currentColor" stroke-width="2" />
				<line x1="16.5" y1="16.5" x2="21" y2="21" stroke="currentColor" stroke-width="2" />
			</svg>
		</span>

		{#if showResults}
			<div class="search-results" role="listbox" aria-label="Search results">
				<div class="search-filters" role="tablist" aria-label="Search filters">
					<button
						class="filter-btn"
						class:active={activeFilter === 'all'}
						onclick={() => {
							activeFilter = 'all';
						}}
					>
						All
					</button>
					<button
						class="filter-btn"
						class:active={activeFilter === 'servers'}
						onclick={() => {
							activeFilter = 'servers';
						}}
					>
						Servers
					</button>
					<button
						class="filter-btn"
						class:active={activeFilter === 'profiles'}
						onclick={() => {
							activeFilter = 'profiles';
						}}
					>
						Profiles
					</button>
				</div>

				{#if showRecent}
					{#if recentQueries.length > 0}
						<div class="results-section">
							<div class="section-header">Recent searches</div>
							<div class="recent-queries">
								{#each recentQueries as term}
									<button class="recent-chip" onclick={() => handleRecentQueryClick(term)}>
										{term}
									</button>
								{/each}
							</div>
						</div>
					{/if}
					{#if !hasRecentResults}
						<div class="no-results">Start typing to search</div>
					{/if}
				{:else}
					{#if !hasSearchResults}
						<div class="no-results">No results found</div>
					{/if}
				{/if}

				{#if visibleServers.length > 0}
					<div class="results-section">
						<div class="section-header">{showRecent ? 'Recent servers' : 'Servers'}</div>
						{#each visibleServers as result}
							<div
								class="result-item"
								class:focused={focusedKey === result.key}
								role="option"
								aria-selected={focusedKey === result.key}
								onclick={() => handleResultSelect(result)}
							>
								<div class="result-main">
									<div class="result-title">
										{#each getHighlightParts(result.label) as part}
											<span class:highlight={part.match}>{part.text}</span>
										{/each}
									</div>
									<span
										class="result-meta"
										class:running={result.server && ('up' in result.server ? result.server.up : result.server.status === 'running')}
									>
										{result.meta}
									</span>
								</div>
								{#if result.server}
									<div class="result-actions">
										{#if 'up' in result.server ? result.server.up : result.server.status === 'running'}
											<button
												class="result-action"
												onclick={(e) => handleServerAction(e, result.server!, 'restart')}
												disabled={isActionBusy(result.server!, 'restart')}
											>
												Restart
											</button>
											<button
												class="result-action danger"
												onclick={(e) => handleServerAction(e, result.server!, 'stop')}
												disabled={isActionBusy(result.server!, 'stop')}
											>
												Stop
											</button>
										{:else}
											<button
												class="result-action"
												onclick={(e) => handleServerAction(e, result.server!, 'start')}
												disabled={isActionBusy(result.server!, 'start')}
											>
												Start
											</button>
										{/if}
									</div>
								{/if}
							</div>
						{/each}
					</div>
				{/if}

				{#if visibleProfiles.length > 0}
					<div class="results-section">
						<div class="section-header">{showRecent ? 'Recent profiles' : 'Profiles'}</div>
						{#each visibleProfiles as result}
							<div
								class="result-item"
								class:focused={focusedKey === result.key}
								role="option"
								aria-selected={focusedKey === result.key}
								onclick={() => handleResultSelect(result)}
							>
								<div class="result-main">
									<div class="result-title">
										{#each getHighlightParts(result.label) as part}
											<span class:highlight={part.match}>{part.text}</span>
										{/each}
									</div>
									<span class="result-meta">{result.meta}</span>
								</div>
							</div>
						{/each}
					</div>
				{/if}
			</div>
		{/if}
	</div>

	<div class="topbar-actions">
		<NotificationMenu />
		<a class="user-info" href="/profile">
			<span class="user-icon" aria-hidden="true">
				<svg viewBox="0 0 24 24" focusable="false" aria-hidden="true">
					<circle cx="12" cy="8" r="4" fill="none" stroke="currentColor" stroke-width="2" />
					<path
						d="M4 20c1.8-3.6 5-5.5 8-5.5s6.2 1.9 8 5.5"
						fill="none"
						stroke="currentColor"
						stroke-width="2"
						stroke-linecap="round"
					/>
				</svg>
			</span>
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
		color: #7c87b2;
		opacity: 0.8;
	}

	.search-icon svg {
		width: 16px;
		height: 16px;
		display: block;
	}

	.search-results {
		position: absolute;
		top: calc(100% + 8px);
		left: 0;
		right: auto;
		width: min(680px, calc(100vw - 64px));
		background: #141827;
		border: 1px solid #2a2f47;
		border-radius: 12px;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.4);
		box-sizing: border-box;
		overflow: hidden;
		padding-bottom: 8px;
		z-index: 2000;
	}

	.search-filters {
		display: flex;
		gap: 8px;
		padding: 12px 16px;
		border-bottom: 1px solid #2a2f47;
	}

	.filter-btn {
		background: none;
		border: 1px solid transparent;
		color: #9aa2c5;
		font-size: 12px;
		font-weight: 600;
		cursor: pointer;
		padding: 6px 10px;
		border-radius: 8px;
		text-transform: uppercase;
		letter-spacing: 0.04em;
		transition: all 0.2s;
	}

	.filter-btn.active {
		color: #eef0f8;
		background: rgba(106, 176, 76, 0.15);
		border-color: rgba(106, 176, 76, 0.4);
	}

	.filter-btn:hover {
		background: rgba(255, 255, 255, 0.05);
		color: #eef0f8;
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

	.recent-queries {
		display: flex;
		flex-wrap: wrap;
		gap: 8px;
		padding: 0 16px 8px;
	}

	.recent-chip {
		background: rgba(255, 255, 255, 0.04);
		border: 1px solid #2a2f47;
		color: #c4cff5;
		padding: 6px 10px;
		border-radius: 999px;
		font-size: 12px;
		cursor: pointer;
		transition: all 0.2s;
	}

	.recent-chip:hover {
		background: rgba(106, 176, 76, 0.15);
		color: #eef0f8;
		border-color: rgba(106, 176, 76, 0.4);
	}

	.result-item {
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 12px;
		padding: 10px 16px;
		width: 100%;
		text-align: left;
		cursor: pointer;
		transition: background 0.2s;
		border: 1px solid transparent;
		background: none;
		box-sizing: border-box;
	}

	.result-item:hover {
		background: rgba(106, 176, 76, 0.08);
	}

	.result-item.focused {
		background: rgba(106, 176, 76, 0.14);
		border-color: rgba(106, 176, 76, 0.35);
	}

	.result-main {
		flex: 1;
		min-width: 0;
	}

	.result-title {
		color: #eef0f8;
		font-size: 14px;
		font-weight: 500;
	}

	.highlight {
		color: #6ab04c;
	}

	.result-name {
		color: inherit;
	}

	.result-meta {
		color: #9aa2c5;
		font-size: 12px;
		margin-top: 2px;
	}

	.result-meta.running {
		color: #b7f5a2;
	}

	.result-actions {
		display: flex;
		gap: 6px;
		flex-shrink: 0;
	}

	.result-action {
		background: rgba(255, 255, 255, 0.04);
		border: 1px solid #2a2f47;
		color: #eef0f8;
		font-size: 11px;
		font-weight: 600;
		padding: 4px 8px;
		border-radius: 6px;
		cursor: pointer;
		transition: all 0.2s;
	}

	.result-action:hover:not(:disabled) {
		background: rgba(106, 176, 76, 0.18);
		border-color: rgba(106, 176, 76, 0.45);
	}

	.result-action.danger:hover:not(:disabled) {
		background: rgba(255, 92, 92, 0.15);
		border-color: rgba(255, 92, 92, 0.4);
	}

	.result-action:disabled {
		opacity: 0.5;
		cursor: not-allowed;
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
		display: inline-flex;
		align-items: center;
		justify-content: center;
		width: 18px;
		height: 18px;
	}

	.user-icon svg {
		width: 18px;
		height: 18px;
		display: block;
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
