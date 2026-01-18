<script lang="ts">
	import { page } from '$app/stores';
	import { goto, invalidateAll } from '$app/navigation';
	import type { LayoutData } from './$types';
	import TopBar from '$lib/components/TopBar.svelte';
	import FeedbackButton from '$lib/components/FeedbackButton.svelte';

	let { data, children }: { data: LayoutData; children: any } = $props();

	async function handleLogout() {
		// Call logout endpoint to clear cookies
		await fetch('/api/auth/logout', { method: 'POST' });
		await invalidateAll();
		goto('/login');
	}

	const navItems = [
		{ href: '/dashboard', label: 'Dashboard', icon: '[D]' },
		{ href: '/servers', label: 'Servers', icon: '[S]' },
		{ href: '/profiles', label: 'Profiles', icon: '[P]' },
		{ href: '/profiles/buildtools', label: 'BuildTools', icon: '[B]' },
		{ href: '/curseforge', label: 'CurseForge', icon: '[C]' },
		{ href: '/admin/access', label: 'Users', icon: '[U]', requiresAdmin: true },
		{ href: '/admin/settings', label: 'Settings', icon: '[G]', requiresAdmin: true },
		{ href: '/admin/shell', label: 'Admin Shell', icon: '[T]', requiresAdmin: true }
	];

	function isActive(href: string) {
		return $page.url.pathname === href || $page.url.pathname.startsWith(href + '/');
	}
</script>

<svelte:head>
	<link rel="preconnect" href="https://fonts.googleapis.com" />
	<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin="anonymous" />
	<link
		href="https://fonts.googleapis.com/css2?family=Space+Grotesk:wght@400;500;600&display=swap"
		rel="stylesheet"
	/>
	<link
		href="https://fonts.googleapis.com/css2?family=Press+Start+2P&display=swap"
		rel="stylesheet"
	/>
</svelte:head>

<div class="app-container">
	<nav class="sidebar">
		<div class="sidebar-header">
			<div class="logo-wrap">
				<img src="/mineos-logo.svg" alt="MineOS logo" class="logo-icon" />
				<div>
					<h1 class="logo">MineOS</h1>
					<p class="logo-tagline">Minecraft Control</p>
				</div>
			</div>
		</div>

		<ul class="nav-list">
			{#each navItems.filter((item) => !item.requiresAdmin || data.user?.role === 'admin') as item}
				<li>
					<a href={item.href} class:active={isActive(item.href)}>
						<span class="icon">{item.icon}</span>
						<span>{item.label}</span>
					</a>
				</li>
			{/each}
		</ul>

	<div class="sidebar-footer">
		<a href="/about" class="footer-link">
			<span class="icon">[?]</span>
			<span>About</span>
		</a>
		<a
			class="coffee-btn"
			href="https://buymeacoffee.com/freemancraft"
			target="_blank"
			rel="noreferrer"
		>
			<span class="icon">[☕]</span>
			<span>Buy Me a Coffee</span>
		</a>
		<button class="logout-btn" onclick={handleLogout}>
			<span class="icon">[X]</span>
			<span>Logout</span>
		</button>
	</div>
	</nav>

	<div class="main-wrapper">
		<TopBar user={data.user} servers={data.servers} profiles={data.profiles} />
		<main class="main-content">
			{@render children()}
		</main>
	</div>

	<FeedbackButton />
</div>

<style>
	:global(body) {
		margin: 0;
		font-family: 'Space Grotesk', system-ui, sans-serif;
		background: radial-gradient(circle at top, rgba(106, 176, 76, 0.15), transparent 55%),
				radial-gradient(circle at 20% 20%, rgba(111, 181, 255, 0.12), transparent 40%),
				linear-gradient(180deg, #151923 0%, #0d0f16 60%, #0a0c12 100%);
		color: #eef0f8;
		min-height: 100vh;
	}

	:global(:root) {
		--mc-grass: #6ab04c;
		--mc-grass-dark: #4a8b34;
		--mc-dirt: #8b5a2b;
		--mc-dirt-dark: #5d3b1f;
		--mc-sky: #6fb5ff;
		--mc-stone: #2b2f45;
		--mc-panel: #1a1e2f;
		--mc-panel-dark: #141827;
		--mc-text-muted: #9aa2c5;
	}

	:global(select) {
		background: #141827;
		border: 1px solid #2a2f47;
		border-radius: 8px;
		padding: 10px 12px;
		color: #eef0f8;
		font-family: inherit;
		font-size: 14px;
		cursor: pointer;
		transition: all 0.2s;
	}

	:global(select:focus) {
		outline: none;
		border-color: rgba(106, 176, 76, 0.5);
		box-shadow: 0 0 0 2px rgba(106, 176, 76, 0.1);
	}

	:global(select:disabled) {
		opacity: 0.5;
		cursor: not-allowed;
	}

	:global(input[type='text']),
	:global(input[type='number']),
	:global(input[type='email']),
	:global(input[type='password']),
	:global(textarea) {
		background: #141827;
		border: 1px solid #2a2f47;
		border-radius: 8px;
		padding: 10px 12px;
		color: #eef0f8;
		font-family: inherit;
		font-size: 14px;
		transition: all 0.2s;
	}

	:global(input:focus),
	:global(textarea:focus) {
		outline: none;
		border-color: rgba(106, 176, 76, 0.5);
		box-shadow: 0 0 0 2px rgba(106, 176, 76, 0.1);
	}

	:global(input:disabled),
	:global(textarea:disabled) {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.app-container {
		display: flex;
		min-height: 100vh;
	}

	.sidebar {
		width: 260px;
		background: linear-gradient(180deg, #171c28 0%, #141827 100%);
		border-right: 1px solid #2a2f47;
		display: flex;
		flex-direction: column;
		position: fixed;
		height: 100vh;
		left: 0;
		top: 0;
	}

	.sidebar-header {
		padding: 24px 20px;
		border-bottom: 1px solid #2a2f47;
	}

	.logo-wrap {
		display: flex;
		align-items: center;
		gap: 12px;
	}

	.logo-icon {
		width: 44px;
		height: 44px;
	}

	.logo {
		margin: 0 0 8px;
		font-size: 16px;
		font-weight: 400;
		font-family: 'Press Start 2P', 'Space Grotesk', sans-serif;
		color: #eef0f8;
	}

	.logo-tagline {
		margin: 0;
		font-size: 11px;
		color: #7c87b2;
		letter-spacing: 0.04em;
		text-transform: uppercase;
	}

	.nav-list {
		list-style: none;
		padding: 12px 8px;
		margin: 0;
		flex: 1;
	}

	.nav-list li {
		margin-bottom: 4px;
	}

	.nav-list a {
		display: flex;
		align-items: center;
		gap: 12px;
		padding: 12px 16px;
		border-radius: 8px;
		color: #aab2d3;
		text-decoration: none;
		transition: all 0.2s;
		font-size: 15px;
	}

	.nav-list a:hover {
		background: rgba(106, 176, 76, 0.12);
		color: #eef0f8;
	}

	.nav-list a.active {
		background: rgba(106, 176, 76, 0.2);
		color: #eef0f8;
		font-weight: 500;
		border: 1px solid rgba(106, 176, 76, 0.4);
	}

	.icon {
		font-size: 18px;
		display: flex;
		align-items: center;
	}

	.sidebar-footer {
		padding: 12px 8px;
		border-top: 1px solid #2a2f47;
		display: flex;
		flex-direction: column;
		gap: 8px;
	}

	.coffee-btn {
		width: 100%;
		display: flex;
		align-items: center;
		gap: 12px;
		padding: 12px 16px;
		border-radius: 8px;
		background: rgba(255, 200, 87, 0.12);
		border: 1px solid rgba(255, 200, 87, 0.35);
		color: #f4c08e;
		text-decoration: none;
		transition: all 0.2s;
		font-size: 14px;
		box-sizing: border-box;
		white-space: normal;
	}

	.coffee-btn:hover {
		background: rgba(255, 200, 87, 0.2);
	}

	.logout-btn {
		width: 100%;
		display: flex;
		align-items: center;
		gap: 12px;
		padding: 12px 16px;
		border-radius: 8px;
		background: transparent;
		border: none;
		color: #f6b26b;
		text-decoration: none;
		transition: all 0.2s;
		font-size: 15px;
		font-family: inherit;
		cursor: pointer;
	}

	.logout-btn:hover {
		background: rgba(246, 178, 107, 0.12);
	}

	.footer-link {
		width: 100%;
		display: flex;
		align-items: center;
		gap: 12px;
		padding: 12px 16px;
		border-radius: 8px;
		background: transparent;
		color: #aab2d3;
		text-decoration: none;
		transition: all 0.2s;
		font-size: 15px;
		box-sizing: border-box;
	}

	.footer-link:hover {
		background: rgba(106, 176, 76, 0.12);
		color: #eef0f8;
	}

	.main-wrapper {
		flex: 1;
		margin-left: 260px;
		display: flex;
		flex-direction: column;
		min-height: 100vh;
		min-width: 0; /* Allow flexbox shrinking */
		width: calc(100vw - 260px);
		max-width: calc(100vw - 260px);
		overflow-x: hidden;
	}

	.main-content {
		flex: 1;
		padding: 32px;
		max-width: 1400px;
		width: 100%;
		min-width: 0; /* Allow flexbox shrinking */
		box-sizing: border-box;
	}

	@media (max-width: 768px) {
		.sidebar {
			width: 200px;
		}

		.main-wrapper {
			margin-left: 200px;
			width: calc(100vw - 200px);
			max-width: calc(100vw - 200px);
		}

		.main-content {
			padding: 20px;
		}
	}

	@media (max-width: 480px) {
		.main-content {
			padding: 16px;
		}
	}
</style>
