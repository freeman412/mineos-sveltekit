<script lang="ts">
	import { page } from '$app/stores';
	import { goto, invalidateAll } from '$app/navigation';
	import type { LayoutData } from './$types';

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
		{ href: '/import', label: 'Import', icon: '[I]' },
		{ href: '/curseforge', label: 'CurseForge', icon: '[C]' }
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
			<p class="user-info">{data.user?.username || 'Unknown'}</p>
		</div>

		<ul class="nav-list">
			{#each navItems as item}
				<li>
					<a href={item.href} class:active={isActive(item.href)}>
						<span class="icon">{item.icon}</span>
						<span>{item.label}</span>
					</a>
				</li>
			{/each}
		</ul>

		<div class="sidebar-footer">
			<button class="logout-btn" onclick={handleLogout}>
				<span class="icon">[X]</span>
				<span>Logout</span>
			</button>
		</div>
	</nav>

	<main class="main-content">
		{@render children()}
	</main>
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
		margin-bottom: 12px;
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
	.user-info {
		margin: 0;
		font-size: 13px;
		color: #8890b1;
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

	.main-content {
		flex: 1;
		margin-left: 260px;
		padding: 48px 32px;
		max-width: 1400px;
		width: 100%;
	}

	@media (max-width: 768px) {
		.sidebar {
			width: 200px;
		}

		.main-content {
			margin-left: 200px;
			padding: 24px 16px;
		}
	}
</style>
