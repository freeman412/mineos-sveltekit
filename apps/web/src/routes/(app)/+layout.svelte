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
		{ href: '/servers', label: 'Servers', icon: '🖥️' },
		{ href: '/profiles', label: 'Profiles', icon: '📦' },
		{ href: '/import', label: 'Import', icon: '📥' }
	];

	function isActive(href: string) {
		return $page.url.pathname === href || $page.url.pathname.startsWith(href + '/');
	}
</script>

<svelte:head>
	<link rel="preconnect" href="https://fonts.googleapis.com" />
	<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin />
	<link
		href="https://fonts.googleapis.com/css2?family=Space+Grotesk:wght@400;500;600&display=swap"
		rel="stylesheet"
	/>
</svelte:head>

<div class="app-container">
	<nav class="sidebar">
		<div class="sidebar-header">
			<h1 class="logo">MineOS</h1>
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
				<span class="icon">🚪</span>
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
		background: radial-gradient(circle at top, #1f2538 0%, #0f1118 55%, #0b0c12 100%);
		color: #eef0f8;
		min-height: 100vh;
	}

	.app-container {
		display: flex;
		min-height: 100vh;
	}

	.sidebar {
		width: 260px;
		background: #141827;
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

	.logo {
		margin: 0 0 8px;
		font-size: 24px;
		font-weight: 600;
		color: #eef0f8;
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
		background: rgba(88, 101, 242, 0.1);
		color: #eef0f8;
	}

	.nav-list a.active {
		background: rgba(88, 101, 242, 0.15);
		color: #eef0f8;
		font-weight: 500;
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
		color: #ff9f9f;
		text-decoration: none;
		transition: all 0.2s;
		font-size: 15px;
		font-family: inherit;
		cursor: pointer;
	}

	.logout-btn:hover {
		background: rgba(255, 92, 92, 0.1);
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
