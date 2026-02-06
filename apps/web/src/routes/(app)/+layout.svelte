<script lang="ts">
	import { page } from '$app/stores';
	import { goto } from '$app/navigation';
	import type { LayoutData } from './$types';
	import TopBar from '$lib/components/TopBar.svelte';
	import FeedbackButton from '$lib/components/FeedbackButton.svelte';
	import MinecraftSheepPet from '$lib/components/MinecraftSheepPet.svelte';
	import { sheepEnabled, theme } from '$lib/stores/uiPreferences';

	let { data, children }: { data: LayoutData; children: any } = $props();
	let sidebarOpen = $state(false);

	function toggleSidebar() {
		sidebarOpen = !sidebarOpen;
	}

	function closeSidebar() {
		sidebarOpen = false;
	}

	// Close sidebar on navigation
	$effect(() => {
		$page.url.pathname;
		sidebarOpen = false;
	});

	const logoSrc = $derived(
		$theme === 'nether' ? '/mineos-logo-nether.svg' :
		$theme === 'end' ? '/mineos-logo-end.svg' :
		'/mineos-logo.svg'
	);
	const faviconSrc = $derived(
		$theme === 'nether' ? '/favicon-nether.svg' :
		$theme === 'end' ? '/favicon-end.svg' :
		'/favicon.svg'
	);

	const navItems = [
		{ href: '/dashboard', label: 'Dashboard', icon: '[D]' },
		{ href: '/servers', label: 'Servers', icon: '[S]' },
		{ href: '/profiles', label: 'Profiles', icon: '[P]' },
		{ href: '/mods', label: 'Mods', icon: '[M]' },
		{ href: '/admin/access', label: 'Users', icon: '[U]', requiresAdmin: true },
		{ href: '/admin/settings', label: 'Settings', icon: '[G]', requiresAdmin: true },
		{ href: '/admin/shell', label: 'Admin Shell', icon: '[T]', requiresAdmin: true }
	];

	function isActive(href: string) {
		return $page.url.pathname === href || $page.url.pathname.startsWith(href + '/');
	}

	function handleKeyboardShortcut(event: KeyboardEvent) {
		// Only handle shortcuts with Shift key pressed
		if (!event.shiftKey) return;

		// Don't trigger shortcuts if user is typing in an input field
		const target = event.target as HTMLElement;
		if (
			target.tagName === 'INPUT' ||
			target.tagName === 'TEXTAREA' ||
			target.isContentEditable
		) {
			return;
		}

		const key = event.key.toUpperCase();
		const shortcuts: Record<string, string> = {
			'D': '/dashboard',
			'S': '/servers',
			'P': '/profiles',
			'M': '/mods',
			'N': '/servers#new',
			'U': '/admin/access',
			'G': '/admin/settings',
			'T': '/admin/shell'
		};

		const path = shortcuts[key];
		if (path) {
			event.preventDefault();
			goto(path);
		}
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
	<link rel="icon" type="image/svg+xml" href={faviconSrc} />
</svelte:head>

<svelte:window onkeydown={handleKeyboardShortcut} />

<div class="app-container" data-theme={$theme}>
	{#if sidebarOpen}
		<!-- svelte-ignore a11y_no_static_element_interactions -->
		<div class="sidebar-overlay" onclick={closeSidebar} onkeydown={(e) => e.key === 'Escape' && closeSidebar()}></div>
	{/if}
	<nav class="sidebar" class:open={sidebarOpen}>
		<div class="sidebar-header">
			<a href="/dashboard" class="logo-wrap logo-link" aria-label="Go to dashboard">
				<img src={logoSrc} alt="MineOS logo" class="logo-icon" />
				<div>
					<div class="logo-title">
						<h1 class="logo">MineOS</h1>
						<span class="beta-tag">Beta</span>
					</div>
					<p class="logo-tagline">Minecraft Control</p>
				</div>
			</a>
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
		<form action="/logout" method="POST" style="display: contents;">
			<button type="submit" class="logout-btn">
				<span class="icon">[X]</span>
				<span>Logout</span>
			</button>
		</form>
	</div>
	</nav>

	<div class="main-wrapper">
		<TopBar user={data.user} servers={data.servers} profiles={data.profiles} onToggleSidebar={toggleSidebar} />
		<main class="main-content">
			{@render children()}
		</main>
	</div>

	<FeedbackButton />
	{#if $sheepEnabled}
		<MinecraftSheepPet />
	{/if}
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
		/* Minecraft-inspired theme colors */
		--mc-grass: #6ab04c;
		--mc-grass-dark: #4a8b34;
		--mc-dirt: #8b5a2b;
		--mc-dirt-dark: #5d3b1f;
		--mc-sky: #6fb5ff;
		--mc-stone: #2b2f45;

		/* Panel backgrounds (darkest to lightest) */
		--mc-panel-darkest: #0d1117;
		--mc-panel-dark: #141827;
		--mc-panel: #1a1e2f;
		--mc-panel-light: #2a2f47;
		--mc-panel-lighter: #3a3f5a;

		/* Text colors */
		--mc-text: #eef0f8;
		--mc-text-secondary: #c4cff5;
		--mc-text-muted: #9aa2c5;
		--mc-text-dim: #7c87b2;

		/* Status colors */
		--color-success: #6ab04c;
		--color-success-light: #7ae68d;
		--color-success-bg: rgba(106, 176, 76, 0.15);
		--color-success-border: rgba(106, 176, 76, 0.3);

		--color-error: #ff6b6b;
		--color-error-light: #ff9f9f;
		--color-error-bg: rgba(255, 92, 92, 0.15);
		--color-error-border: rgba(255, 92, 92, 0.3);

		--color-warning: #ffb74d;
		--color-warning-light: #ffcc80;
		--color-warning-bg: rgba(255, 183, 77, 0.15);
		--color-warning-border: rgba(255, 183, 77, 0.3);

		--color-info: #5b9eff;
		--color-info-light: #a5b4fc;
		--color-info-bg: rgba(91, 158, 255, 0.15);
		--color-info-border: rgba(91, 158, 255, 0.3);

		/* Border colors */
		--border-color: #2a2f47;
		--border-color-light: rgba(42, 47, 71, 0.5);

		/* Focus states */
		--focus-color: rgba(106, 176, 76, 0.5);
		--focus-shadow: 0 0 0 2px rgba(106, 176, 76, 0.1);

		/* Scrollbars */
		--scrollbar-size: 6px;
		--scrollbar-track: rgba(15, 17, 24, 0.6);
		--scrollbar-thumb: rgba(88, 101, 242, 0.35);
		--scrollbar-thumb-hover: rgba(88, 101, 242, 0.55);
		--scrollbar-thumb-border: rgba(88, 101, 242, 0.5);
	}

	/* ═══════════════════════════════════════════════════════════════
	   NETHER THEME — Crimson, lava, netherrack, soul fire
	   ═══════════════════════════════════════════════════════════════ */

	:global([data-theme='nether']) {
		/* Minecraft Nether palette */
		--mc-grass: #dc2626;
		--mc-grass-dark: #991b1b;
		--mc-dirt: #7f1d1d;
		--mc-dirt-dark: #450a0a;
		--mc-sky: #fb923c;
		--mc-stone: #3b1515;

		/* Panel backgrounds — deep crimson/blackstone */
		--mc-panel-darkest: #0a0505;
		--mc-panel-dark: #1a0808;
		--mc-panel: #241010;
		--mc-panel-light: #3b1a1a;
		--mc-panel-lighter: #4d2525;

		/* Text — warm, ashy tones */
		--mc-text: #fde8d8;
		--mc-text-secondary: #e8b4a0;
		--mc-text-muted: #b87a6a;
		--mc-text-dim: #8a5a4d;

		/* Status colors — Nether variants */
		--color-success: #fb923c;
		--color-success-light: #fdba74;
		--color-success-bg: rgba(251, 146, 60, 0.18);
		--color-success-border: rgba(251, 146, 60, 0.4);

		--color-error: #ef4444;
		--color-error-light: #fca5a5;
		--color-error-bg: rgba(239, 68, 68, 0.2);
		--color-error-border: rgba(239, 68, 68, 0.45);

		--color-warning: #f59e0b;
		--color-warning-light: #fcd34d;
		--color-warning-bg: rgba(245, 158, 11, 0.18);
		--color-warning-border: rgba(245, 158, 11, 0.4);

		--color-info: #a78bfa;
		--color-info-light: #c4b5fd;
		--color-info-bg: rgba(167, 139, 250, 0.15);
		--color-info-border: rgba(167, 139, 250, 0.35);

		/* Borders — smoldering red */
		--border-color: #4d2525;
		--border-color-light: rgba(77, 37, 37, 0.6);

		/* Focus — lava glow */
		--focus-color: rgba(239, 68, 68, 0.6);
		--focus-shadow: 0 0 0 2px rgba(239, 68, 68, 0.15);

		/* Scrollbars — crimson */
		--scrollbar-track: rgba(26, 8, 8, 0.8);
		--scrollbar-thumb: rgba(220, 38, 38, 0.4);
		--scrollbar-thumb-hover: rgba(239, 68, 68, 0.6);
		--scrollbar-thumb-border: rgba(220, 38, 38, 0.5);
	}

	/* Nether body — fiery void with lava undertones */
	:global([data-theme='nether'] body),
	:global(body:has([data-theme='nether'])) {
		background:
			radial-gradient(ellipse at top, rgba(220, 38, 38, 0.2), transparent 55%),
			radial-gradient(circle at 80% 80%, rgba(251, 146, 60, 0.12), transparent 40%),
			radial-gradient(circle at 20% 60%, rgba(139, 92, 246, 0.08), transparent 35%),
			linear-gradient(180deg, #1a0808 0%, #0f0404 60%, #0a0202 100%) !important;
		color: #fde8d8 !important;
	}

	/* Nether select inputs */
	:global([data-theme='nether'] select) {
		background: #1a0808;
		border-color: #4d2525;
		color: #fde8d8;
	}

	:global([data-theme='nether'] select:focus) {
		border-color: rgba(239, 68, 68, 0.6);
		box-shadow: 0 0 0 2px rgba(239, 68, 68, 0.15);
	}

	/* Nether text inputs */
	:global([data-theme='nether'] input[type='text']),
	:global([data-theme='nether'] input[type='number']),
	:global([data-theme='nether'] input[type='email']),
	:global([data-theme='nether'] input[type='password']),
	:global([data-theme='nether'] textarea) {
		background: #1a0808;
		border-color: #4d2525;
		color: #fde8d8;
	}

	:global([data-theme='nether'] input:focus),
	:global([data-theme='nether'] textarea:focus) {
		border-color: rgba(239, 68, 68, 0.6);
		box-shadow: 0 0 0 2px rgba(239, 68, 68, 0.15);
	}

	/* Nether placeholder text */
	:global([data-theme='nether'] input::placeholder),
	:global([data-theme='nether'] textarea::placeholder) {
		color: #8a5a4d;
	}

	/* Nether buttons — generic overrides for common button patterns */
	:global([data-theme='nether'] button:not([class*='portal'])) {
		transition: all 0.2s;
	}

	/* Nether links */
	:global([data-theme='nether'] a) {
		transition: color 0.3s;
	}

	/* Nether code/pre blocks */
	:global([data-theme='nether'] pre),
	:global([data-theme='nether'] code) {
		background: #1a0808;
		border-color: #4d2525;
	}

	/* Nether tables */
	:global([data-theme='nether'] table) {
		border-color: #4d2525;
	}

	:global([data-theme='nether'] th) {
		background: #241010;
		border-color: #4d2525;
		color: #fde8d8;
	}

	:global([data-theme='nether'] td) {
		border-color: #3b1a1a;
	}

	:global([data-theme='nether'] tr:hover td) {
		background: rgba(220, 38, 38, 0.08);
	}

	/* Nether cards/panels — common class patterns */
	:global([data-theme='nether'] .card),
	:global([data-theme='nether'] .panel),
	:global([data-theme='nether'] .box) {
		background: #1a0808;
		border-color: #4d2525;
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

	:global(input[type='number']) {
		appearance: textfield;
		-moz-appearance: textfield;
	}

	:global(input[type='number']::-webkit-outer-spin-button),
	:global(input[type='number']::-webkit-inner-spin-button) {
		-webkit-appearance: none;
		margin: 0;
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

	:global(*) {
		scrollbar-width: thin;
		scrollbar-color: var(--scrollbar-thumb) var(--scrollbar-track);
	}

	:global(*::-webkit-scrollbar) {
		width: var(--scrollbar-size);
		height: var(--scrollbar-size);
	}

	:global(*::-webkit-scrollbar-track) {
		background: var(--scrollbar-track);
		border-radius: 999px;
	}

	:global(*::-webkit-scrollbar-thumb) {
		background: var(--scrollbar-thumb);
		border-radius: 999px;
		border: 1px solid var(--scrollbar-thumb-border);
	}

	:global(*::-webkit-scrollbar-thumb:hover) {
		background: var(--scrollbar-thumb-hover);
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

	.logo-link {
		text-decoration: none;
		color: inherit;
		border-radius: 12px;
		padding: 6px 4px;
		transition: background 0.2s, transform 0.2s;
	}

	.logo-link:hover {
		background: rgba(88, 101, 242, 0.08);
		transform: translateY(-1px);
	}

	.logo-link:focus-visible {
		outline: 2px solid rgba(88, 101, 242, 0.45);
		outline-offset: 2px;
	}

	.logo-title {
		display: flex;
		align-items: center;
		gap: 8px;
	}

	.logo-icon {
		width: 44px;
		height: 44px;
	}

	.logo {
		margin: 0;
		font-size: 16px;
		font-weight: 400;
		font-family: 'Press Start 2P', 'Space Grotesk', sans-serif;
		color: #eef0f8;
	}

	.logo-tagline {
		margin: 4px 0 0;
		font-size: 11px;
		color: #7c87b2;
		letter-spacing: 0.04em;
		text-transform: uppercase;
	}

	.beta-tag {
		display: inline-flex;
		align-items: center;
		padding: 2px 8px;
		border-radius: 999px;
		background: rgba(255, 183, 77, 0.2);
		border: 1px solid rgba(255, 183, 77, 0.4);
		color: #ffcc80;
		font-size: 10px;
		font-weight: 700;
		text-transform: uppercase;
		letter-spacing: 0.08em;
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

	/* ═══ NETHER THEME — Sidebar ═══ */
	:global([data-theme='nether']) .sidebar {
		background: linear-gradient(180deg, #1a0808 0%, #150606 100%);
		border-right-color: #4d2525;
		box-shadow: 2px 0 20px rgba(220, 38, 38, 0.1);
	}

	:global([data-theme='nether']) .sidebar-header {
		border-bottom-color: #4d2525;
	}

	:global([data-theme='nether']) .logo {
		color: #fde8d8;
	}

	:global([data-theme='nether']) .logo-tagline {
		color: #8a5a4d;
	}

	:global([data-theme='nether']) .logo-link:hover {
		background: rgba(220, 38, 38, 0.1);
	}

	:global([data-theme='nether']) .logo-link:focus-visible {
		outline-color: rgba(220, 38, 38, 0.5);
	}

	:global([data-theme='nether']) .beta-tag {
		background: rgba(239, 68, 68, 0.2);
		border-color: rgba(239, 68, 68, 0.4);
		color: #fca5a5;
	}

	/* ═══ NETHER THEME — Navigation ═══ */
	:global([data-theme='nether']) .nav-list a {
		color: #b87a6a;
	}

	:global([data-theme='nether']) .nav-list a:hover {
		background: rgba(220, 38, 38, 0.15);
		color: #fde8d8;
	}

	:global([data-theme='nether']) .nav-list a.active {
		background: rgba(220, 38, 38, 0.25);
		color: #fde8d8;
		border-color: rgba(239, 68, 68, 0.5);
		box-shadow: inset 0 0 12px rgba(251, 146, 60, 0.1);
	}

	/* ═══ NETHER THEME — Sidebar Footer ═══ */
	:global([data-theme='nether']) .sidebar-footer {
		border-top-color: #4d2525;
	}

	:global([data-theme='nether']) .coffee-btn {
		background: rgba(251, 146, 60, 0.15);
		border-color: rgba(251, 146, 60, 0.35);
		color: #fdba74;
	}

	:global([data-theme='nether']) .coffee-btn:hover {
		background: rgba(251, 146, 60, 0.25);
		box-shadow: 0 0 12px rgba(251, 146, 60, 0.2);
	}

	:global([data-theme='nether']) .logout-btn {
		color: #ef4444;
	}

	:global([data-theme='nether']) .logout-btn:hover {
		background: rgba(239, 68, 68, 0.15);
	}

	:global([data-theme='nether']) .footer-link {
		color: #b87a6a;
	}

	:global([data-theme='nether']) .footer-link:hover {
		background: rgba(220, 38, 38, 0.12);
		color: #fde8d8;
	}

	/* ═══ NETHER THEME — TopBar ═══ */
	:global([data-theme='nether'] .topbar) {
		background: linear-gradient(180deg, #1a0808 0%, #150606 100%) !important;
		border-bottom-color: #4d2525 !important;
		box-shadow: 0 2px 15px rgba(220, 38, 38, 0.08);
	}

	:global([data-theme='nether'] .topbar-search input) {
		background: #241010 !important;
		border-color: #4d2525 !important;
		color: #fde8d8 !important;
	}

	:global([data-theme='nether'] .topbar-search input:focus) {
		border-color: rgba(239, 68, 68, 0.6) !important;
		box-shadow: 0 0 0 2px rgba(239, 68, 68, 0.15) !important;
	}

	:global([data-theme='nether'] .topbar-search input::placeholder) {
		color: #8a5a4d !important;
	}

	:global([data-theme='nether'] .search-results) {
		background: #1a0808 !important;
		border-color: #4d2525 !important;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.6) !important;
	}

	:global([data-theme='nether'] .search-filters) {
		border-bottom-color: #4d2525 !important;
	}

	:global([data-theme='nether'] .filter-btn) {
		color: #b87a6a !important;
	}

	:global([data-theme='nether'] .filter-btn.active) {
		color: #fde8d8 !important;
		background: rgba(220, 38, 38, 0.2) !important;
		border-color: rgba(239, 68, 68, 0.45) !important;
	}

	:global([data-theme='nether'] .filter-btn:hover) {
		color: #fde8d8 !important;
		background: rgba(220, 38, 38, 0.1) !important;
	}

	:global([data-theme='nether'] .result-item:hover) {
		background: rgba(220, 38, 38, 0.1) !important;
	}

	:global([data-theme='nether'] .result-item.focused) {
		background: rgba(220, 38, 38, 0.18) !important;
		border-color: rgba(239, 68, 68, 0.4) !important;
	}

	:global([data-theme='nether'] .highlight) {
		color: #fb923c !important;
	}

	:global([data-theme='nether'] .result-meta.running) {
		color: #fdba74 !important;
	}

	:global([data-theme='nether'] .recent-chip) {
		background: rgba(220, 38, 38, 0.1) !important;
		border-color: #4d2525 !important;
		color: #e8b4a0 !important;
	}

	:global([data-theme='nether'] .recent-chip:hover) {
		background: rgba(220, 38, 38, 0.2) !important;
		border-color: rgba(239, 68, 68, 0.45) !important;
		color: #fde8d8 !important;
	}

	:global([data-theme='nether'] .section-header) {
		color: #8a5a4d !important;
	}

	:global([data-theme='nether'] .results-section) {
		border-bottom-color: #3b1a1a !important;
	}

	:global([data-theme='nether'] .result-action) {
		background: rgba(220, 38, 38, 0.1) !important;
		border-color: #4d2525 !important;
		color: #fde8d8 !important;
	}

	:global([data-theme='nether'] .result-action:hover:not(:disabled)) {
		background: rgba(251, 146, 60, 0.2) !important;
		border-color: rgba(251, 146, 60, 0.5) !important;
	}

	:global([data-theme='nether'] .result-action.danger:hover:not(:disabled)) {
		background: rgba(239, 68, 68, 0.2) !important;
		border-color: rgba(239, 68, 68, 0.5) !important;
	}

	:global([data-theme='nether'] .user-info) {
		background: rgba(220, 38, 38, 0.06) !important;
		border-color: #4d2525 !important;
	}

	:global([data-theme='nether'] .user-info:hover) {
		background: rgba(220, 38, 38, 0.12) !important;
		border-color: rgba(239, 68, 68, 0.45) !important;
	}

	:global([data-theme='nether'] .search-icon) {
		color: #8a5a4d !important;
	}

	/* ═══ NETHER THEME — Cards, Panels, Stat Cards ═══ */
	:global([data-theme='nether'] .stat-card),
	:global([data-theme='nether'] .metric-card),
	:global([data-theme='nether'] .profile-card),
	:global([data-theme='nether'] .quick-link-card),
	:global([data-theme='nether'] .setting-card),
	:global([data-theme='nether'] .info-card),
	:global([data-theme='nether'] .result-card) {
		background: linear-gradient(160deg, rgba(36, 16, 16, 0.95), rgba(26, 8, 8, 0.95)) !important;
		border-color: #4d2525 !important;
		box-shadow: 0 10px 30px rgba(0, 0, 0, 0.4) !important;
	}

	:global([data-theme='nether'] .stat-card:hover),
	:global([data-theme='nether'] .metric-card:hover),
	:global([data-theme='nether'] .quick-link-card:hover) {
		border-color: rgba(239, 68, 68, 0.5) !important;
		box-shadow: 0 10px 35px rgba(220, 38, 38, 0.15) !important;
	}

	:global([data-theme='nether'] .stat-icon) {
		color: #fb923c !important;
	}

	:global([data-theme='nether'] .stat-value) {
		color: #fde8d8 !important;
	}

	:global([data-theme='nether'] .stat-label),
	:global([data-theme='nether'] .metric-label) {
		color: #b87a6a !important;
	}

	/* ═══ NETHER THEME — Status Badges ═══ */
	:global([data-theme='nether'] .status-badge.success),
	:global([data-theme='nether'] .badge-success) {
		background: rgba(251, 146, 60, 0.18) !important;
		border-color: rgba(251, 146, 60, 0.4) !important;
		color: #fdba74 !important;
	}

	:global([data-theme='nether'] .status-dot.success) {
		background: #fb923c !important;
		box-shadow: 0 0 6px rgba(251, 146, 60, 0.6) !important;
	}

	:global([data-theme='nether'] .status-badge.error),
	:global([data-theme='nether'] .badge-error) {
		background: rgba(239, 68, 68, 0.2) !important;
		border-color: rgba(239, 68, 68, 0.45) !important;
		color: #fca5a5 !important;
	}

	:global([data-theme='nether'] .status-badge.warning),
	:global([data-theme='nether'] .badge-warning) {
		background: rgba(245, 158, 11, 0.18) !important;
		border-color: rgba(245, 158, 11, 0.4) !important;
		color: #fcd34d !important;
	}

	:global([data-theme='nether'] .status-badge.info),
	:global([data-theme='nether'] .badge-info) {
		background: rgba(167, 139, 250, 0.15) !important;
		border-color: rgba(167, 139, 250, 0.35) !important;
		color: #c4b5fd !important;
	}

	/* ═══ NETHER THEME — Buttons ═══ */
	:global([data-theme='nether'] .btn-primary) {
		background: linear-gradient(135deg, #dc2626, #b91c1c) !important;
		border-color: #ef4444 !important;
		color: #fff !important;
		box-shadow: 0 2px 10px rgba(220, 38, 38, 0.3) !important;
	}

	:global([data-theme='nether'] .btn-primary:hover) {
		background: linear-gradient(135deg, #ef4444, #dc2626) !important;
		box-shadow: 0 4px 15px rgba(239, 68, 68, 0.4) !important;
	}

	:global([data-theme='nether'] .btn-secondary) {
		background: rgba(77, 37, 37, 0.5) !important;
		border-color: #4d2525 !important;
		color: #e8b4a0 !important;
	}

	:global([data-theme='nether'] .btn-secondary:hover) {
		background: rgba(77, 37, 37, 0.8) !important;
		border-color: rgba(239, 68, 68, 0.4) !important;
		color: #fde8d8 !important;
	}

	:global([data-theme='nether'] .btn-success) {
		background: linear-gradient(135deg, #ea580c, #c2410c) !important;
		border-color: #f97316 !important;
		color: #fff !important;
	}

	:global([data-theme='nether'] .btn-danger) {
		background: rgba(239, 68, 68, 0.2) !important;
		border-color: rgba(239, 68, 68, 0.5) !important;
		color: #fca5a5 !important;
	}

	:global([data-theme='nether'] .btn-danger:hover) {
		background: rgba(239, 68, 68, 0.35) !important;
		box-shadow: 0 0 15px rgba(239, 68, 68, 0.3) !important;
	}

	:global([data-theme='nether'] .btn-warning) {
		background: rgba(245, 158, 11, 0.2) !important;
		border-color: rgba(245, 158, 11, 0.5) !important;
		color: #fcd34d !important;
	}

	:global([data-theme='nether'] .btn-ghost),
	:global([data-theme='nether'] .btn-link) {
		color: #fb923c !important;
	}

	:global([data-theme='nether'] .btn-ghost:hover),
	:global([data-theme='nether'] .btn-link:hover) {
		background: rgba(251, 146, 60, 0.12) !important;
		color: #fdba74 !important;
	}

	:global([data-theme='nether'] .btn-icon) {
		color: #b87a6a !important;
	}

	:global([data-theme='nether'] .btn-icon:hover) {
		color: #fb923c !important;
		background: rgba(251, 146, 60, 0.12) !important;
	}

	:global([data-theme='nether'] .btn-create),
	:global([data-theme='nether'] .btn-setup) {
		background: linear-gradient(135deg, rgba(251, 146, 60, 0.25), rgba(220, 38, 38, 0.2)) !important;
		border-color: rgba(251, 146, 60, 0.5) !important;
		color: #fdba74 !important;
	}

	:global([data-theme='nether'] .btn-create:hover),
	:global([data-theme='nether'] .btn-setup:hover) {
		background: linear-gradient(135deg, rgba(251, 146, 60, 0.35), rgba(220, 38, 38, 0.3)) !important;
		box-shadow: 0 4px 15px rgba(251, 146, 60, 0.2) !important;
	}

	/* ═══ NETHER THEME — Modals/Dialogs ═══ */
	:global([data-theme='nether'] .modal-backdrop),
	:global([data-theme='nether'] .modal-overlay) {
		background: rgba(10, 2, 2, 0.85) !important;
	}

	:global([data-theme='nether'] .modal-container) {
		background: linear-gradient(160deg, #241010, #1a0808) !important;
		border-color: #4d2525 !important;
		box-shadow: 0 25px 60px rgba(0, 0, 0, 0.5), 0 0 30px rgba(220, 38, 38, 0.1) !important;
	}

	:global([data-theme='nether'] .modal-title) {
		color: #fde8d8 !important;
	}

	:global([data-theme='nether'] .modal-message) {
		color: #e8b4a0 !important;
	}

	:global([data-theme='nether'] .modal-icon.alert),
	:global([data-theme='nether'] .modal-icon.error) {
		color: #ef4444 !important;
	}

	:global([data-theme='nether'] .modal-icon.success) {
		color: #fb923c !important;
	}

	:global([data-theme='nether'] .modal-icon.confirm) {
		color: #f59e0b !important;
	}

	/* ═══ NETHER THEME — Console/Logs/Shell ═══ */
	:global([data-theme='nether'] .console-container),
	:global([data-theme='nether'] .log-panel),
	:global([data-theme='nether'] .console-panel),
	:global([data-theme='nether'] .shell-panel) {
		background: #0a0505 !important;
		border-color: #3b1a1a !important;
	}

	:global([data-theme='nether'] .console-header) {
		background: #1a0808 !important;
		border-color: #4d2525 !important;
	}

	/* ═══ NETHER THEME — Library/Build Panels ═══ */
	:global([data-theme='nether'] .library-panel),
	:global([data-theme='nether'] .buildtools-panel) {
		background: #1a0808 !important;
		border-color: #4d2525 !important;
	}

	/* ═══ NETHER THEME — Forms ═══ */
	:global([data-theme='nether'] .field .label-text) {
		color: #e8b4a0 !important;
	}

	:global([data-theme='nether'] .error-box) {
		background: rgba(239, 68, 68, 0.15) !important;
		border-color: rgba(239, 68, 68, 0.4) !important;
		color: #fca5a5 !important;
	}

	:global([data-theme='nether'] .success-box) {
		background: rgba(251, 146, 60, 0.15) !important;
		border-color: rgba(251, 146, 60, 0.4) !important;
		color: #fdba74 !important;
	}

	:global([data-theme='nether'] .info-box) {
		background: rgba(167, 139, 250, 0.1) !important;
		border-color: rgba(167, 139, 250, 0.3) !important;
		color: #c4b5fd !important;
	}

	/* ═══ NETHER THEME — Settings ═══ */
	:global([data-theme='nether'] .setting-header) {
		color: #fde8d8 !important;
	}

	:global([data-theme='nether'] .setting-description) {
		color: #b87a6a !important;
	}

	:global([data-theme='nether'] .setting-value) {
		color: #e8b4a0 !important;
	}

	/* ═══ NETHER THEME — Info Sections/Grids ═══ */
	:global([data-theme='nether'] .info-section) {
		border-color: #3b1a1a !important;
	}

	:global([data-theme='nether'] .info-row) {
		border-color: #3b1a1a !important;
	}

	/* ═══ NETHER THEME — Misc text overrides for hardcoded colors ═══ */
	:global([data-theme='nether'] .muted),
	:global([data-theme='nether'] .tagline) {
		color: #8a5a4d !important;
	}

	/* ═══ NETHER THEME — Notification menu ═══ */
	:global([data-theme='nether'] .notification-panel) {
		background: #1a0808 !important;
		border-color: #4d2525 !important;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.5) !important;
	}

	:global([data-theme='nether'] .notification-item) {
		border-color: #3b1a1a !important;
	}

	:global([data-theme='nether'] .notification-item:hover) {
		background: rgba(220, 38, 38, 0.08) !important;
	}

	/* ═══ NETHER THEME — Feedback button ═══ */
	:global([data-theme='nether'] .feedback-btn) {
		background: linear-gradient(135deg, #dc2626, #991b1b) !important;
		border-color: rgba(239, 68, 68, 0.5) !important;
		box-shadow: 0 4px 15px rgba(220, 38, 38, 0.3) !important;
	}

	:global([data-theme='nether'] .feedback-btn:hover) {
		background: linear-gradient(135deg, #ef4444, #dc2626) !important;
		box-shadow: 0 6px 20px rgba(239, 68, 68, 0.4) !important;
	}

	/* ═══ NETHER THEME — Ambient lava glow animation ═══ */
	@keyframes netherAmbientGlow {
		0%, 100% {
			box-shadow: 2px 0 20px rgba(220, 38, 38, 0.1);
		}
		50% {
			box-shadow: 2px 0 30px rgba(251, 146, 60, 0.15);
		}
	}

	:global([data-theme='nether']) .sidebar {
		animation: netherAmbientGlow 4s ease-in-out infinite;
	}

	/* ═══════════════════════════════════════════════════════════════
	   END THEME — Void purple, endstone, chorus, dragon breath
	   ═══════════════════════════════════════════════════════════════ */

	:global([data-theme='end']) {
		/* Minecraft End palette */
		--mc-grass: #a78bfa;
		--mc-grass-dark: #7c3aed;
		--mc-dirt: #3b1f6e;
		--mc-dirt-dark: #1e0a3e;
		--mc-sky: #c4b5fd;
		--mc-stone: #1a0f2e;

		/* Panel backgrounds — deep void purple */
		--mc-panel-darkest: #06020f;
		--mc-panel-dark: #0d0620;
		--mc-panel: #150b2e;
		--mc-panel-light: #231546;
		--mc-panel-lighter: #321f5e;

		/* Text — pale endstone tones */
		--mc-text: #ede9fe;
		--mc-text-secondary: #c4b5fd;
		--mc-text-muted: #8b7ab8;
		--mc-text-dim: #6b5b96;

		/* Status colors — End variants */
		--color-success: #a78bfa;
		--color-success-light: #c4b5fd;
		--color-success-bg: rgba(167, 139, 250, 0.18);
		--color-success-border: rgba(167, 139, 250, 0.4);

		--color-error: #f472b6;
		--color-error-light: #f9a8d4;
		--color-error-bg: rgba(244, 114, 182, 0.18);
		--color-error-border: rgba(244, 114, 182, 0.4);

		--color-warning: #d4c999;
		--color-warning-light: #e8deb5;
		--color-warning-bg: rgba(212, 201, 153, 0.15);
		--color-warning-border: rgba(212, 201, 153, 0.35);

		--color-info: #818cf8;
		--color-info-light: #a5b4fc;
		--color-info-bg: rgba(129, 140, 248, 0.15);
		--color-info-border: rgba(129, 140, 248, 0.35);

		/* Borders — obsidian purple */
		--border-color: #321f5e;
		--border-color-light: rgba(50, 31, 94, 0.6);

		/* Focus — ender purple glow */
		--focus-color: rgba(167, 139, 250, 0.6);
		--focus-shadow: 0 0 0 2px rgba(167, 139, 250, 0.15);

		/* Scrollbars — void purple */
		--scrollbar-track: rgba(13, 6, 32, 0.8);
		--scrollbar-thumb: rgba(167, 139, 250, 0.35);
		--scrollbar-thumb-hover: rgba(167, 139, 250, 0.55);
		--scrollbar-thumb-border: rgba(167, 139, 250, 0.45);
	}

	/* End body — void with purple nebula */
	:global([data-theme='end'] body),
	:global(body:has([data-theme='end'])) {
		background:
			radial-gradient(ellipse at top, rgba(139, 92, 246, 0.15), transparent 55%),
			radial-gradient(circle at 80% 30%, rgba(167, 139, 250, 0.1), transparent 40%),
			radial-gradient(circle at 15% 70%, rgba(244, 114, 182, 0.06), transparent 35%),
			linear-gradient(180deg, #0b0014 0%, #06020f 60%, #030108 100%) !important;
		color: #ede9fe !important;
	}

	/* End select inputs */
	:global([data-theme='end'] select) {
		background: #0d0620;
		border-color: #321f5e;
		color: #ede9fe;
	}

	:global([data-theme='end'] select:focus) {
		border-color: rgba(167, 139, 250, 0.6);
		box-shadow: 0 0 0 2px rgba(167, 139, 250, 0.15);
	}

	/* End text inputs */
	:global([data-theme='end'] input[type='text']),
	:global([data-theme='end'] input[type='number']),
	:global([data-theme='end'] input[type='email']),
	:global([data-theme='end'] input[type='password']),
	:global([data-theme='end'] textarea) {
		background: #0d0620;
		border-color: #321f5e;
		color: #ede9fe;
	}

	:global([data-theme='end'] input:focus),
	:global([data-theme='end'] textarea:focus) {
		border-color: rgba(167, 139, 250, 0.6);
		box-shadow: 0 0 0 2px rgba(167, 139, 250, 0.15);
	}

	/* End placeholder text */
	:global([data-theme='end'] input::placeholder),
	:global([data-theme='end'] textarea::placeholder) {
		color: #6b5b96;
	}

	/* End buttons — generic overrides */
	:global([data-theme='end'] button:not([class*='portal']):not([class*='ender'])) {
		transition: all 0.2s;
	}

	/* End links */
	:global([data-theme='end'] a) {
		transition: color 0.3s;
	}

	/* End code/pre blocks */
	:global([data-theme='end'] pre),
	:global([data-theme='end'] code) {
		background: #0d0620;
		border-color: #321f5e;
	}

	/* End tables */
	:global([data-theme='end'] table) {
		border-color: #321f5e;
	}

	:global([data-theme='end'] th) {
		background: #150b2e;
		border-color: #321f5e;
		color: #ede9fe;
	}

	:global([data-theme='end'] td) {
		border-color: #231546;
	}

	:global([data-theme='end'] tr:hover td) {
		background: rgba(167, 139, 250, 0.06);
	}

	/* End cards/panels */
	:global([data-theme='end'] .card),
	:global([data-theme='end'] .panel),
	:global([data-theme='end'] .box) {
		background: #0d0620;
		border-color: #321f5e;
	}

	/* ═══ END THEME — Sidebar ═══ */
	:global([data-theme='end']) .sidebar {
		background: linear-gradient(180deg, #0d0620 0%, #0a0418 100%);
		border-right-color: #321f5e;
		box-shadow: 2px 0 20px rgba(139, 92, 246, 0.08);
	}

	:global([data-theme='end']) .sidebar-header {
		border-bottom-color: #321f5e;
	}

	:global([data-theme='end']) .logo {
		color: #ede9fe;
	}

	:global([data-theme='end']) .logo-tagline {
		color: #6b5b96;
	}

	:global([data-theme='end']) .logo-link:hover {
		background: rgba(167, 139, 250, 0.1);
	}

	:global([data-theme='end']) .logo-link:focus-visible {
		outline-color: rgba(167, 139, 250, 0.5);
	}

	:global([data-theme='end']) .beta-tag {
		background: rgba(167, 139, 250, 0.2);
		border-color: rgba(167, 139, 250, 0.4);
		color: #c4b5fd;
	}

	/* ═══ END THEME — Navigation ═══ */
	:global([data-theme='end']) .nav-list a {
		color: #8b7ab8;
	}

	:global([data-theme='end']) .nav-list a:hover {
		background: rgba(167, 139, 250, 0.12);
		color: #ede9fe;
	}

	:global([data-theme='end']) .nav-list a.active {
		background: rgba(167, 139, 250, 0.2);
		color: #ede9fe;
		border-color: rgba(167, 139, 250, 0.45);
		box-shadow: inset 0 0 12px rgba(139, 92, 246, 0.1);
	}

	/* ═══ END THEME — Sidebar Footer ═══ */
	:global([data-theme='end']) .sidebar-footer {
		border-top-color: #321f5e;
	}

	:global([data-theme='end']) .coffee-btn {
		background: rgba(212, 201, 153, 0.12);
		border-color: rgba(212, 201, 153, 0.3);
		color: #d4c999;
	}

	:global([data-theme='end']) .coffee-btn:hover {
		background: rgba(212, 201, 153, 0.2);
		box-shadow: 0 0 12px rgba(212, 201, 153, 0.15);
	}

	:global([data-theme='end']) .logout-btn {
		color: #f472b6;
	}

	:global([data-theme='end']) .logout-btn:hover {
		background: rgba(244, 114, 182, 0.12);
	}

	:global([data-theme='end']) .footer-link {
		color: #8b7ab8;
	}

	:global([data-theme='end']) .footer-link:hover {
		background: rgba(167, 139, 250, 0.1);
		color: #ede9fe;
	}

	/* ═══ END THEME — TopBar ═══ */
	:global([data-theme='end'] .topbar) {
		background: linear-gradient(180deg, #0d0620 0%, #0a0418 100%) !important;
		border-bottom-color: #321f5e !important;
		box-shadow: 0 2px 15px rgba(139, 92, 246, 0.06);
	}

	:global([data-theme='end'] .topbar-search input) {
		background: #150b2e !important;
		border-color: #321f5e !important;
		color: #ede9fe !important;
	}

	:global([data-theme='end'] .topbar-search input:focus) {
		border-color: rgba(167, 139, 250, 0.6) !important;
		box-shadow: 0 0 0 2px rgba(167, 139, 250, 0.15) !important;
	}

	:global([data-theme='end'] .topbar-search input::placeholder) {
		color: #6b5b96 !important;
	}

	:global([data-theme='end'] .search-results) {
		background: #0d0620 !important;
		border-color: #321f5e !important;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.6) !important;
	}

	:global([data-theme='end'] .search-filters) {
		border-bottom-color: #321f5e !important;
	}

	:global([data-theme='end'] .filter-btn) {
		color: #8b7ab8 !important;
	}

	:global([data-theme='end'] .filter-btn.active) {
		color: #ede9fe !important;
		background: rgba(167, 139, 250, 0.2) !important;
		border-color: rgba(167, 139, 250, 0.45) !important;
	}

	:global([data-theme='end'] .filter-btn:hover) {
		color: #ede9fe !important;
		background: rgba(167, 139, 250, 0.1) !important;
	}

	:global([data-theme='end'] .result-item:hover) {
		background: rgba(167, 139, 250, 0.08) !important;
	}

	:global([data-theme='end'] .result-item.focused) {
		background: rgba(167, 139, 250, 0.15) !important;
		border-color: rgba(167, 139, 250, 0.4) !important;
	}

	:global([data-theme='end'] .highlight) {
		color: #c4b5fd !important;
	}

	:global([data-theme='end'] .result-meta.running) {
		color: #c4b5fd !important;
	}

	:global([data-theme='end'] .recent-chip) {
		background: rgba(167, 139, 250, 0.1) !important;
		border-color: #321f5e !important;
		color: #c4b5fd !important;
	}

	:global([data-theme='end'] .recent-chip:hover) {
		background: rgba(167, 139, 250, 0.2) !important;
		border-color: rgba(167, 139, 250, 0.45) !important;
		color: #ede9fe !important;
	}

	:global([data-theme='end'] .section-header) {
		color: #6b5b96 !important;
	}

	:global([data-theme='end'] .results-section) {
		border-bottom-color: #231546 !important;
	}

	:global([data-theme='end'] .result-action) {
		background: rgba(167, 139, 250, 0.1) !important;
		border-color: #321f5e !important;
		color: #ede9fe !important;
	}

	:global([data-theme='end'] .result-action:hover:not(:disabled)) {
		background: rgba(167, 139, 250, 0.2) !important;
		border-color: rgba(167, 139, 250, 0.5) !important;
	}

	:global([data-theme='end'] .result-action.danger:hover:not(:disabled)) {
		background: rgba(244, 114, 182, 0.2) !important;
		border-color: rgba(244, 114, 182, 0.5) !important;
	}

	:global([data-theme='end'] .user-info) {
		background: rgba(167, 139, 250, 0.06) !important;
		border-color: #321f5e !important;
	}

	:global([data-theme='end'] .user-info:hover) {
		background: rgba(167, 139, 250, 0.12) !important;
		border-color: rgba(167, 139, 250, 0.45) !important;
	}

	:global([data-theme='end'] .search-icon) {
		color: #6b5b96 !important;
	}

	/* ═══ END THEME — Cards, Panels, Stat Cards ═══ */
	:global([data-theme='end'] .stat-card),
	:global([data-theme='end'] .metric-card),
	:global([data-theme='end'] .profile-card),
	:global([data-theme='end'] .quick-link-card),
	:global([data-theme='end'] .setting-card),
	:global([data-theme='end'] .info-card),
	:global([data-theme='end'] .result-card) {
		background: linear-gradient(160deg, rgba(21, 11, 46, 0.95), rgba(13, 6, 32, 0.95)) !important;
		border-color: #321f5e !important;
		box-shadow: 0 10px 30px rgba(0, 0, 0, 0.4) !important;
	}

	:global([data-theme='end'] .stat-card:hover),
	:global([data-theme='end'] .metric-card:hover),
	:global([data-theme='end'] .quick-link-card:hover) {
		border-color: rgba(167, 139, 250, 0.5) !important;
		box-shadow: 0 10px 35px rgba(139, 92, 246, 0.12) !important;
	}

	:global([data-theme='end'] .stat-icon) {
		color: #c4b5fd !important;
	}

	:global([data-theme='end'] .stat-value) {
		color: #ede9fe !important;
	}

	:global([data-theme='end'] .stat-label),
	:global([data-theme='end'] .metric-label) {
		color: #8b7ab8 !important;
	}

	/* ═══ END THEME — Status Badges ═══ */
	:global([data-theme='end'] .status-badge.success),
	:global([data-theme='end'] .badge-success) {
		background: rgba(167, 139, 250, 0.18) !important;
		border-color: rgba(167, 139, 250, 0.4) !important;
		color: #c4b5fd !important;
	}

	:global([data-theme='end'] .status-dot.success) {
		background: #a78bfa !important;
		box-shadow: 0 0 6px rgba(167, 139, 250, 0.6) !important;
	}

	:global([data-theme='end'] .status-badge.error),
	:global([data-theme='end'] .badge-error) {
		background: rgba(244, 114, 182, 0.18) !important;
		border-color: rgba(244, 114, 182, 0.4) !important;
		color: #f9a8d4 !important;
	}

	:global([data-theme='end'] .status-badge.warning),
	:global([data-theme='end'] .badge-warning) {
		background: rgba(212, 201, 153, 0.15) !important;
		border-color: rgba(212, 201, 153, 0.35) !important;
		color: #e8deb5 !important;
	}

	:global([data-theme='end'] .status-badge.info),
	:global([data-theme='end'] .badge-info) {
		background: rgba(129, 140, 248, 0.15) !important;
		border-color: rgba(129, 140, 248, 0.35) !important;
		color: #a5b4fc !important;
	}

	/* ═══ END THEME — Buttons ═══ */
	:global([data-theme='end'] .btn-primary) {
		background: linear-gradient(135deg, #7c3aed, #6d28d9) !important;
		border-color: #a78bfa !important;
		color: #fff !important;
		box-shadow: 0 2px 10px rgba(124, 58, 237, 0.3) !important;
	}

	:global([data-theme='end'] .btn-primary:hover) {
		background: linear-gradient(135deg, #8b5cf6, #7c3aed) !important;
		box-shadow: 0 4px 15px rgba(139, 92, 246, 0.4) !important;
	}

	:global([data-theme='end'] .btn-secondary) {
		background: rgba(50, 31, 94, 0.5) !important;
		border-color: #321f5e !important;
		color: #c4b5fd !important;
	}

	:global([data-theme='end'] .btn-secondary:hover) {
		background: rgba(50, 31, 94, 0.8) !important;
		border-color: rgba(167, 139, 250, 0.4) !important;
		color: #ede9fe !important;
	}

	:global([data-theme='end'] .btn-success) {
		background: linear-gradient(135deg, #7c3aed, #6d28d9) !important;
		border-color: #a78bfa !important;
		color: #fff !important;
	}

	:global([data-theme='end'] .btn-danger) {
		background: rgba(244, 114, 182, 0.18) !important;
		border-color: rgba(244, 114, 182, 0.45) !important;
		color: #f9a8d4 !important;
	}

	:global([data-theme='end'] .btn-danger:hover) {
		background: rgba(244, 114, 182, 0.3) !important;
		box-shadow: 0 0 15px rgba(244, 114, 182, 0.25) !important;
	}

	:global([data-theme='end'] .btn-warning) {
		background: rgba(212, 201, 153, 0.15) !important;
		border-color: rgba(212, 201, 153, 0.4) !important;
		color: #e8deb5 !important;
	}

	:global([data-theme='end'] .btn-ghost),
	:global([data-theme='end'] .btn-link) {
		color: #a78bfa !important;
	}

	:global([data-theme='end'] .btn-ghost:hover),
	:global([data-theme='end'] .btn-link:hover) {
		background: rgba(167, 139, 250, 0.1) !important;
		color: #c4b5fd !important;
	}

	:global([data-theme='end'] .btn-icon) {
		color: #8b7ab8 !important;
	}

	:global([data-theme='end'] .btn-icon:hover) {
		color: #a78bfa !important;
		background: rgba(167, 139, 250, 0.1) !important;
	}

	:global([data-theme='end'] .btn-create),
	:global([data-theme='end'] .btn-setup) {
		background: linear-gradient(135deg, rgba(167, 139, 250, 0.25), rgba(139, 92, 246, 0.18)) !important;
		border-color: rgba(167, 139, 250, 0.5) !important;
		color: #c4b5fd !important;
	}

	:global([data-theme='end'] .btn-create:hover),
	:global([data-theme='end'] .btn-setup:hover) {
		background: linear-gradient(135deg, rgba(167, 139, 250, 0.35), rgba(139, 92, 246, 0.28)) !important;
		box-shadow: 0 4px 15px rgba(167, 139, 250, 0.2) !important;
	}

	/* ═══ END THEME — Modals/Dialogs ═══ */
	:global([data-theme='end'] .modal-backdrop),
	:global([data-theme='end'] .modal-overlay) {
		background: rgba(3, 1, 8, 0.88) !important;
	}

	:global([data-theme='end'] .modal-container) {
		background: linear-gradient(160deg, #150b2e, #0d0620) !important;
		border-color: #321f5e !important;
		box-shadow: 0 25px 60px rgba(0, 0, 0, 0.5), 0 0 30px rgba(139, 92, 246, 0.08) !important;
	}

	:global([data-theme='end'] .modal-title) {
		color: #ede9fe !important;
	}

	:global([data-theme='end'] .modal-message) {
		color: #c4b5fd !important;
	}

	:global([data-theme='end'] .modal-icon.alert),
	:global([data-theme='end'] .modal-icon.error) {
		color: #f472b6 !important;
	}

	:global([data-theme='end'] .modal-icon.success) {
		color: #a78bfa !important;
	}

	:global([data-theme='end'] .modal-icon.confirm) {
		color: #d4c999 !important;
	}

	/* ═══ END THEME — Console/Logs/Shell ═══ */
	:global([data-theme='end'] .console-container),
	:global([data-theme='end'] .log-panel),
	:global([data-theme='end'] .console-panel),
	:global([data-theme='end'] .shell-panel) {
		background: #06020f !important;
		border-color: #231546 !important;
	}

	:global([data-theme='end'] .console-header) {
		background: #0d0620 !important;
		border-color: #321f5e !important;
	}

	/* ═══ END THEME — Library/Build Panels ═══ */
	:global([data-theme='end'] .library-panel),
	:global([data-theme='end'] .buildtools-panel) {
		background: #0d0620 !important;
		border-color: #321f5e !important;
	}

	/* ═══ END THEME — Forms ═══ */
	:global([data-theme='end'] .field .label-text) {
		color: #c4b5fd !important;
	}

	:global([data-theme='end'] .error-box) {
		background: rgba(244, 114, 182, 0.12) !important;
		border-color: rgba(244, 114, 182, 0.35) !important;
		color: #f9a8d4 !important;
	}

	:global([data-theme='end'] .success-box) {
		background: rgba(167, 139, 250, 0.12) !important;
		border-color: rgba(167, 139, 250, 0.35) !important;
		color: #c4b5fd !important;
	}

	:global([data-theme='end'] .info-box) {
		background: rgba(129, 140, 248, 0.1) !important;
		border-color: rgba(129, 140, 248, 0.3) !important;
		color: #a5b4fc !important;
	}

	/* ═══ END THEME — Settings ═══ */
	:global([data-theme='end'] .setting-header) {
		color: #ede9fe !important;
	}

	:global([data-theme='end'] .setting-description) {
		color: #8b7ab8 !important;
	}

	:global([data-theme='end'] .setting-value) {
		color: #c4b5fd !important;
	}

	/* ═══ END THEME — Info Sections/Grids ═══ */
	:global([data-theme='end'] .info-section) {
		border-color: #231546 !important;
	}

	:global([data-theme='end'] .info-row) {
		border-color: #231546 !important;
	}

	/* ═══ END THEME — Misc text overrides ═══ */
	:global([data-theme='end'] .muted),
	:global([data-theme='end'] .tagline) {
		color: #6b5b96 !important;
	}

	/* ═══ END THEME — Notification menu ═══ */
	:global([data-theme='end'] .notification-panel) {
		background: #0d0620 !important;
		border-color: #321f5e !important;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.5) !important;
	}

	:global([data-theme='end'] .notification-item) {
		border-color: #231546 !important;
	}

	:global([data-theme='end'] .notification-item:hover) {
		background: rgba(167, 139, 250, 0.06) !important;
	}

	/* ═══ END THEME — Feedback button ═══ */
	:global([data-theme='end'] .feedback-btn) {
		background: linear-gradient(135deg, #7c3aed, #5b21b6) !important;
		border-color: rgba(167, 139, 250, 0.5) !important;
		box-shadow: 0 4px 15px rgba(124, 58, 237, 0.3) !important;
	}

	:global([data-theme='end'] .feedback-btn:hover) {
		background: linear-gradient(135deg, #8b5cf6, #7c3aed) !important;
		box-shadow: 0 6px 20px rgba(139, 92, 246, 0.4) !important;
	}

	/* ═══ END THEME — Ambient void pulse animation ═══ */
	@keyframes endAmbientGlow {
		0%, 100% {
			box-shadow: 2px 0 20px rgba(139, 92, 246, 0.08);
		}
		50% {
			box-shadow: 2px 0 30px rgba(167, 139, 250, 0.12);
		}
	}

	:global([data-theme='end']) .sidebar {
		animation: endAmbientGlow 6s ease-in-out infinite;
	}

	/* ═══ THEME TRANSITIONS — Smooth switching ═══ */
	:global([data-theme] body),
	:global([data-theme] .sidebar),
	:global([data-theme] .topbar),
	:global([data-theme] .nav-list a),
	:global([data-theme] .stat-card),
	:global([data-theme] .card),
	:global([data-theme] .panel),
	:global([data-theme] .modal-container),
	:global([data-theme] input),
	:global([data-theme] select),
	:global([data-theme] textarea) {
		transition: background 0.4s ease, background-color 0.4s ease, border-color 0.4s ease, color 0.4s ease, box-shadow 0.4s ease;
	}

	.sidebar-overlay {
		display: none;
	}

	@media (max-width: 768px) {
		.sidebar {
			width: 260px;
			transform: translateX(-100%);
			transition: transform 0.3s ease;
			z-index: 200;
		}

		.sidebar.open {
			transform: translateX(0);
		}

		.sidebar-overlay {
			display: block;
			position: fixed;
			inset: 0;
			background: rgba(0, 0, 0, 0.6);
			z-index: 150;
			backdrop-filter: blur(2px);
		}

		.main-wrapper {
			margin-left: 0;
			width: 100vw;
			max-width: 100vw;
		}

		.main-content {
			padding: 20px;
		}
	}

	@media (max-width: 480px) {
		.main-content {
			padding: 14px;
		}

		.sidebar {
			width: 240px;
		}
	}
</style>
