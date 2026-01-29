<script lang="ts">
	import { theme } from '$lib/stores/uiPreferences';

	const version = '2.0.0-beta';
	const buildId = import.meta.env.PUBLIC_BUILD_ID ?? 'dev';

	function toggleEndTheme() {
		if ($theme === 'end') {
			theme.set('overworld');
		} else {
			theme.set('end');
		}
	}
</script>

<div class="about-page">
	<header class="page-header">
		<h1>About MineOS</h1>
		<p class="subtitle">Modern Minecraft Server Management</p>
	</header>

	<div class="hero-section">
		<div class="logo-container">
			<img src="/mineos-logo.svg" alt="MineOS logo" class="logo" />
		</div>
		<div class="hero-text">
			<h2>MineOS SvelteKit Edition</h2>
			<p class="version">Version {version}</p>
			<p class="build-id">Build {buildId}</p>
			<p class="status-line">Status: Beta</p>
			<p class="tagline">
				A complete rewrite of the beloved MineOS web UI, built with modern technologies
				for a faster, more responsive Minecraft server management experience.
				Vanilla and Forge servers are solid, and Modrinth installs are working.
			</p>
		</div>
	</div>

	<div class="cards-grid">
		<section class="card ai-built tech-stack">
			<div class="card-icon">🤖</div>
			<h3>AI-Assisted Development</h3>
			<p>
				This project is primarily AI-generated code, maintained and guided by a human developer.
				We use Claude and other AI assistants to rapidly develop and iterate on features.
			</p>
			<p>
				<strong>Want to contribute?</strong> You can help by donating your AI tokens!
				Clone the repository, point your AI coding assistant at an open issue, and submit a pull request.
			</p>
			<div class="contribute-steps">
				<ol>
					<li>Fork the <a href="https://github.com/freeman412/mineos-sveltekit" target="_blank" rel="noopener noreferrer">repository</a></li>
					<li>Pick an <a href="https://github.com/freeman412/mineos-sveltekit/issues" target="_blank" rel="noopener noreferrer">open issue</a></li>
					<li>Run your AI agent (Claude Code, Cursor, Copilot, etc.)</li>
					<li>Submit a pull request with your changes</li>
				</ol>
			</div>
			<p class="ai-note">
				Every contribution helps, whether it's fixing a bug, adding a feature, or improving documentation!
			</p>
		</section>
		<section class="card tribute">
			<div class="card-icon">🙏</div>
			<h3>Standing on the Shoulders of Giants</h3>
			<p>
				This project is a spiritual successor to the original
				<a href="https://github.com/hexparrot/mineos-node" target="_blank" rel="noopener noreferrer">
					MineOS by hexparrot (William Dizon)
				</a>. The original MineOS has helped countless server administrators
				manage their Minecraft servers since 2011.
			</p>
			<p>
				While this is a complete rewrite with no shared code, we honor the vision
				and spirit of the original project: making Minecraft server management
				accessible to everyone.
			</p>
			<div class="attribution">
				<span class="label">Original Creator:</span>
				<a href="https://github.com/hexparrot" target="_blank" rel="noopener noreferrer">
					hexparrot (William Dizon)
				</a>
			</div>
		</section>

		

		<section class="card tech-stack">
			<div class="card-icon">⚡</div>
			<h3>Built With</h3>
			<ul class="tech-list">
				<li>
					<span class="tech-name">SvelteKit</span>
					<span class="tech-desc">Modern reactive frontend framework</span>
				</li>
				<li>
					<span class="tech-name">.NET 8</span>
					<span class="tech-desc">High-performance .NET minimal API backend</span>
				</li>
				<li>
					<span class="tech-name">SQLite</span>
					<span class="tech-desc">Lightweight, embedded database</span>
				</li>
				<li>
					<span class="tech-name">Entity Framework Core</span>
					<span class="tech-desc">Modern ORM for .NET</span>
				</li>
				<li>
					<span class="tech-name">TypeScript</span>
					<span class="tech-desc">Type-safe JavaScript development</span>
				</li>
			</ul>
		</section>

		<section class="card integrations">
			<div class="card-icon">🔗</div>
			<h3>Integrations</h3>
			<div class="integration-list">
				<a href="https://www.curseforge.com/" target="_blank" rel="noopener noreferrer" class="integration">
					<div class="integration-logo curseforge">CF</div>
					<div class="integration-info">
						<span class="integration-name">CurseForge</span>
						<span class="integration-desc">Mod & modpack downloads</span>
					</div>
				</a>
				<a href="https://www.spigotmc.org/wiki/buildtools/" target="_blank" rel="noopener noreferrer" class="integration">
					<div class="integration-logo spigot">🔧</div>
					<div class="integration-info">
						<span class="integration-name">SpigotMC BuildTools</span>
						<span class="integration-desc">Build Spigot & CraftBukkit</span>
					</div>
				</a>
				<a href="https://papermc.io/" target="_blank" rel="noopener noreferrer" class="integration">
					<div class="integration-logo paper">📄</div>
					<div class="integration-info">
						<span class="integration-name">PaperMC</span>
						<span class="integration-desc">High-performance server downloads</span>
					</div>
				</a>
				<a href="https://fabricmc.net/" target="_blank" rel="noopener noreferrer" class="integration">
					<div class="integration-logo fabric">🧵</div>
					<div class="integration-info">
						<span class="integration-name">Fabric</span>
						<span class="integration-desc">Modding toolchain support</span>
					</div>
				</a>
				<a href="https://files.minecraftforge.net/" target="_blank" rel="noopener noreferrer" class="integration">
					<div class="integration-logo forge">⚒️</div>
					<div class="integration-info">
						<span class="integration-name">Minecraft Forge</span>
						<span class="integration-desc">Modded server support</span>
					</div>
				</a>
			</div>
		</section>

		<section class="card features">
			<div class="card-icon">✨</div>
			<h3>Key Features</h3>
			<ul class="feature-list">
				<li>Real-time server monitoring with live metrics</li>
				<li>One-click vanilla and Forge server creation</li>
				<li>Modrinth and CurseForge mod installs</li>
				<li>Guided server.properties editor with smart defaults</li>
				<li>World backup, restore, and management</li>
				<li>BuildTools integration for Spigot/Paper builds</li>
				<li>Server console with command history</li>
				<li>Responsive design for desktop and mobile</li>
				<li>Multi-user access control</li>
			</ul>
		</section>

		<section class="card license">
			<div class="card-icon">📜</div>
			<h3>License</h3>
			<p>
				MineOS SvelteKit Edition is open source software.
				The project welcomes contributions from the community.
			</p>
			<div class="license-badge">
				<a href="https://github.com/freeman412/mineos-sveltekit" target="_blank" rel="noopener noreferrer" class="github-link">
					<svg viewBox="0 0 24 24" class="github-icon">
						<path fill="currentColor" d="M12 0c-6.626 0-12 5.373-12 12 0 5.302 3.438 9.8 8.207 11.387.599.111.793-.261.793-.577v-2.234c-3.338.726-4.033-1.416-4.033-1.416-.546-1.387-1.333-1.756-1.333-1.756-1.089-.745.083-.729.083-.729 1.205.084 1.839 1.237 1.839 1.237 1.07 1.834 2.807 1.304 3.492.997.107-.775.418-1.305.762-1.604-2.665-.305-5.467-1.334-5.467-5.931 0-1.311.469-2.381 1.236-3.221-.124-.303-.535-1.524.117-3.176 0 0 1.008-.322 3.301 1.23.957-.266 1.983-.399 3.003-.404 1.02.005 2.047.138 3.006.404 2.291-1.552 3.297-1.23 3.297-1.23.653 1.653.242 2.874.118 3.176.77.84 1.235 1.911 1.235 3.221 0 4.609-2.807 5.624-5.479 5.921.43.372.823 1.102.823 2.222v3.293c0 .319.192.694.801.576 4.765-1.589 8.199-6.086 8.199-11.386 0-6.627-5.373-12-12-12z"/>
					</svg>
					View on GitHub
				</a>
			</div>
		</section>

		<section class="card roadmap full-width">
			<div class="card-icon">[R]</div>
			<h3>Roadmap</h3>
			<p>
				This list mirrors the
				<a href="https://github.com/freeman412/mineos-sveltekit/issues" target="_blank" rel="noopener noreferrer">GitHub issues</a>.
				Pick one and jump in.
			</p>
			<div class="roadmap-grid">
				<div class="roadmap-category">
					<h4>Next Up</h4>
					<ul>
						<li><span class="status planned">Planned</span> <a href="https://github.com/freeman412/mineos-sveltekit/issues/47" target="_blank" rel="noopener noreferrer">Rename users</a></li>
						<li><span class="status planned">Planned</span> <a href="https://github.com/freeman412/mineos-sveltekit/issues/48" target="_blank" rel="noopener noreferrer">Default server visibility</a></li>
						<li><span class="status planned">Planned</span> <a href="https://github.com/freeman412/mineos-sveltekit/issues/35" target="_blank" rel="noopener noreferrer">Server icon cropping</a></li>
						<li><span class="status planned">Planned</span> <a href="https://github.com/freeman412/mineos-sveltekit/issues/34" target="_blank" rel="noopener noreferrer">Add mods during server creation</a></li>
						<li><span class="status planned">Planned</span> <a href="https://github.com/freeman412/mineos-sveltekit/issues/26" target="_blank" rel="noopener noreferrer">Invite links and auto-whitelist</a></li>
					</ul>
				</div>
				<div class="roadmap-category">
					<h4>Modding and Backups</h4>
					<ul>
						<li><span class="status planned">Planned</span> <a href="https://github.com/freeman412/mineos-sveltekit/issues/38" target="_blank" rel="noopener noreferrer">Detect individual mod updates</a></li>
						<li><span class="status planned">Planned</span> <a href="https://github.com/freeman412/mineos-sveltekit/issues/21" target="_blank" rel="noopener noreferrer">Auto install mod dependencies</a></li>
						<li><span class="status planned">Planned</span> <a href="https://github.com/freeman412/mineos-sveltekit/issues/22" target="_blank" rel="noopener noreferrer">Scheduled cron backups</a></li>
						<li><span class="status planned">Planned</span> <a href="https://github.com/freeman412/mineos-sveltekit/issues/10" target="_blank" rel="noopener noreferrer">Modded client download</a></li>
					</ul>
				</div>
				<div class="roadmap-category">
					<h4>Platform</h4>
					<ul>
						<li><span class="status planned">Planned</span> <a href="https://github.com/freeman412/mineos-sveltekit/issues/25" target="_blank" rel="noopener noreferrer">OAuth login support</a></li>
						<li><span class="status planned">Planned</span> <a href="https://github.com/freeman412/mineos-sveltekit/issues/7" target="_blank" rel="noopener noreferrer">Consolidate EventSource streams</a></li>
						<li><span class="status planned">Planned</span> <a href="https://github.com/freeman412/mineos-sveltekit/issues/9" target="_blank" rel="noopener noreferrer">Time reference on performance chart</a></li>
					</ul>
				</div>
				<div class="roadmap-category">
					<h4>Big Bets</h4>
					<ul>
						<li><span class="status future">Future</span> <a href="https://github.com/freeman412/mineos-sveltekit/issues/23" target="_blank" rel="noopener noreferrer">Run each server in its own Docker container</a></li>
					</ul>
				</div>
			</div>
		</section>
	</div>

	<!-- svelte-ignore a11y_no_static_element_interactions -->
	<div class="ender-pearl" class:ender-pearl-active={$theme === 'end'} onclick={toggleEndTheme} onkeydown={(e) => { if (e.key === 'Enter') toggleEndTheme(); }} role="button" tabindex="-1" aria-hidden="true" title="">
		<span class="pearl-core">●</span>
	</div>

	<footer class="about-footer">
		<p>
			Made with 💚 for the Minecraft community
		</p>
		<p class="copyright">
			Minecraft is a trademark of Mojang Studios. This project is not affiliated with Mojang or Microsoft.
		</p>
	</footer>
</div>

<style>
	.about-page {
		max-width: 1200px;
		margin: 0 auto;
	}

	.page-header {
		text-align: center;
		margin-bottom: 40px;
	}

	h1 {
		margin: 0 0 12px;
		font-size: 36px;
		font-weight: 700;
		background: linear-gradient(135deg, var(--mc-grass) 0%, var(--color-success-light) 100%);
		-webkit-background-clip: text;
		-webkit-text-fill-color: transparent;
		background-clip: text;
	}

	.subtitle {
		margin: 0;
		color: var(--mc-text-muted);
		font-size: 18px;
	}

	.hero-section {
		display: flex;
		align-items: center;
		gap: 40px;
		background: linear-gradient(135deg, var(--mc-panel) 0%, var(--mc-panel-dark) 100%);
		border-radius: 20px;
		padding: 40px;
		margin-bottom: 32px;
		border: 1px solid var(--color-success-border);
	}

	.logo-container {
		flex-shrink: 0;
	}

	.logo {
		width: 120px;
		height: 120px;
		filter: drop-shadow(0 8px 16px rgba(0, 0, 0, 0.3));
	}

	.hero-text h2 {
		margin: 0 0 8px;
		font-size: 28px;
		color: var(--mc-text);
	}

	.version {
		display: inline-block;
		background: var(--color-success-bg);
		color: var(--mc-grass);
		border: 1px solid var(--color-success-border);
		padding: 4px 12px;
		border-radius: 20px;
		font-size: 13px;
		font-weight: 600;
		margin-bottom: 16px;
	}

	.build-id {
		display: inline-block;
		background: var(--color-info-bg);
		color: var(--color-info-light);
		border: 1px solid var(--color-info-border);
		padding: 4px 12px;
		border-radius: 20px;
		font-size: 12px;
		font-weight: 600;
		margin: 0 0 16px;
	}

	.status-line {
		display: inline-block;
		background: var(--color-warning-bg);
		color: var(--color-warning-light);
		border: 1px solid var(--color-warning-border);
		padding: 4px 12px;
		border-radius: 20px;
		font-size: 12px;
		font-weight: 600;
		margin: 0 0 16px;
	}

	.tagline {
		margin: 0;
		color: var(--mc-text-muted);
		font-size: 16px;
		line-height: 1.6;
		max-width: 500px;
	}

	.cards-grid {
		display: grid;
		grid-template-columns: repeat(auto-fit, minmax(340px, 1fr));
		gap: 24px;
		margin-bottom: 40px;
	}

	.card {
		background: linear-gradient(135deg, var(--mc-panel) 0%, var(--mc-panel-dark) 100%);
		border-radius: 16px;
		padding: 28px;
		border: 1px solid var(--border-color);
		transition: all 0.2s;
	}

	.card:hover {
		border-color: var(--color-success-border);
		transform: translateY(-2px);
	}

	.card-icon {
		font-size: 32px;
		margin-bottom: 16px;
	}

	.card h3 {
		margin: 0 0 16px;
		font-size: 20px;
		color: var(--mc-text);
	}

	.card p {
		margin: 0 0 16px;
		color: var(--mc-text-muted);
		font-size: 14px;
		line-height: 1.6;
	}

	.card a {
		color: var(--mc-grass);
		text-decoration: none;
		transition: color 0.2s;
	}

	.card a:hover {
		color: var(--color-success-light);
		text-decoration: underline;
	}

	.attribution {
		display: flex;
		gap: 8px;
		padding-top: 12px;
		border-top: 1px solid var(--border-color);
		font-size: 14px;
	}

	.attribution .label {
		color: var(--mc-text-dim);
	}

	.tech-list {
		list-style: none;
		padding: 0;
		margin: 0;
		display: flex;
		flex-direction: column;
		gap: 12px;
	}

	.tech-list li {
		display: flex;
		flex-direction: column;
		gap: 2px;
		padding: 12px;
		background: rgba(20, 24, 39, 0.6);
		border-radius: 10px;
		border: 1px solid var(--border-color);
	}

	.tech-name {
		font-weight: 600;
		color: var(--mc-text);
		font-size: 14px;
	}

	.tech-desc {
		color: var(--mc-text-dim);
		font-size: 12px;
	}

	.integration-list {
		display: flex;
		flex-direction: column;
		gap: 12px;
	}

	.integration {
		display: flex;
		align-items: center;
		gap: 16px;
		padding: 14px;
		background: rgba(20, 24, 39, 0.6);
		border-radius: 12px;
		border: 1px solid var(--border-color);
		text-decoration: none !important;
		transition: all 0.2s;
	}

	.integration:hover {
		border-color: var(--color-success-border);
		background: rgba(20, 24, 39, 0.8);
	}

	.integration-logo {
		width: 44px;
		height: 44px;
		border-radius: 10px;
		display: flex;
		align-items: center;
		justify-content: center;
		font-size: 20px;
		font-weight: 700;
		flex-shrink: 0;
	}

	.integration-logo.curseforge {
		background: linear-gradient(135deg, #f16436 0%, #e04e26 100%);
		color: white;
	}

	.integration-logo.spigot {
		background: linear-gradient(135deg, #f7b731 0%, #e6a22a 100%);
	}

	.integration-logo.paper {
		background: linear-gradient(135deg, #5b9eff 0%, #4a8df5 100%);
	}

	.integration-logo.fabric {
		background: linear-gradient(135deg, #dbd0b4 0%, #c4b89a 100%);
	}

	.integration-logo.forge {
		background: linear-gradient(135deg, #3e5571 0%, #2d4a68 100%);
	}

	.integration-info {
		display: flex;
		flex-direction: column;
		gap: 2px;
	}

	.integration-name {
		font-weight: 600;
		color: var(--mc-text);
		font-size: 15px;
	}

	.integration-desc {
		color: var(--mc-text-dim);
		font-size: 13px;
	}

	.feature-list {
		list-style: none;
		padding: 0;
		margin: 0;
		display: grid;
		gap: 10px;
	}

	.feature-list li {
		padding: 10px 14px;
		background: var(--color-success-bg);
		border-radius: 8px;
		color: var(--mc-text-secondary);
		font-size: 14px;
		border-left: 3px solid var(--color-success-border);
	}

	.status-list,
	.recent-list {
		list-style: none;
		padding: 0;
		margin: 0;
		display: grid;
		gap: 10px;
	}

	.status-list li,
	.recent-list li {
		padding: 10px 14px;
		background: var(--color-info-bg);
		border-radius: 8px;
		color: var(--mc-text-secondary);
		font-size: 14px;
		border-left: 3px solid var(--color-info-border);
	}

	.license-badge {
		margin-top: 20px;
	}

	.github-link {
		display: inline-flex;
		align-items: center;
		gap: 10px;
		background: #24292e;
		color: #fff !important;
		padding: 12px 20px;
		border-radius: 10px;
		text-decoration: none !important;
		font-weight: 600;
		font-size: 14px;
		transition: background 0.2s;
	}

	.github-link:hover {
		background: #2f363d;
	}

	.github-icon {
		width: 20px;
		height: 20px;
	}

	.support-links {
		display: flex;
		gap: 12px;
		flex-wrap: wrap;
	}

	.support-link {
		display: inline-flex;
		align-items: center;
		gap: 8px;
		padding: 10px 18px;
		background: var(--color-success-bg);
		border: 1px solid var(--color-success-border);
		border-radius: 8px;
		color: var(--color-success-light) !important;
		font-size: 14px;
		font-weight: 500;
		text-decoration: none !important;
		transition: all 0.2s;
	}

	.support-link:hover {
		background: rgba(106, 176, 76, 0.2);
		border-color: rgba(106, 176, 76, 0.5);
	}

	/* AI-Built Section */
	.ai-built.full-width {
		grid-column: 1 / -1;
	}

	.ai-built.full-width .contribute-steps {
		display: grid;
		grid-template-columns: 1fr;
	}

	.ai-built.full-width .contribute-steps ol {
		display: grid;
		grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
		gap: 8px 24px;
		padding-left: 24px;
	}

	.ai-built .contribute-steps {
		background: var(--color-info-bg);
		border: 1px solid var(--color-info-border);
		border-radius: 10px;
		padding: 16px 16px 16px 20px;
		margin-bottom: 16px;
	}

	.ai-built .contribute-steps ol {
		margin: 0;
		padding-left: 20px;
		color: var(--mc-text-muted);
		font-size: 14px;
		line-height: 1.8;
	}

	.ai-built .ai-note {
		background: var(--color-success-bg);
		border-radius: 8px;
		padding: 12px 16px;
		color: var(--color-success-light);
		font-size: 13px;
		margin-bottom: 0;
	}

	/* Roadmap Section */
	.roadmap.full-width {
		grid-column: 1 / -1;
	}

	.roadmap-grid {
		display: grid;
		grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
		gap: 20px;
		margin-top: 16px;
	}

	.roadmap-category {
		background: rgba(20, 24, 39, 0.6);
		border: 1px solid var(--border-color);
		border-radius: 12px;
		padding: 16px;
	}

	.roadmap-category h4 {
		margin: 0 0 12px;
		font-size: 14px;
		font-weight: 600;
		color: var(--color-info-light);
		text-transform: uppercase;
		letter-spacing: 0.5px;
	}

	.roadmap-category ul {
		list-style: none;
		padding: 0;
		margin: 0;
	}

	.roadmap-category li {
		display: flex;
		align-items: center;
		gap: 8px;
		padding: 8px 0;
		border-bottom: 1px solid var(--border-color);
		font-size: 13px;
		color: var(--mc-text-muted);
	}

	.roadmap-category li:last-child {
		border-bottom: none;
	}

	.status {
		display: inline-block;
		padding: 2px 8px;
		border-radius: 4px;
		font-size: 10px;
		font-weight: 600;
		text-transform: uppercase;
		flex-shrink: 0;
	}

	.status.planned {
		background: var(--color-success-bg);
		color: var(--mc-grass);
	}

	.status.future {
		background: var(--color-info-bg);
		color: var(--color-info-light);
	}

	.about-footer {
		text-align: center;
		padding: 32px 0;
		border-top: 1px solid var(--border-color);
	}

	.about-footer p {
		margin: 0 0 8px;
		color: var(--mc-text-muted);
		font-size: 14px;
	}

	.copyright {
		font-size: 12px !important;
		color: var(--mc-text-dim) !important;
	}

	/* Floating Ender Pearl easter egg */
	@keyframes floatAround {
		0% {
			transform: translate(0, 0) rotate(0deg);
		}
		25% {
			transform: translate(-100px, 60px) rotate(90deg);
		}
		50% {
			transform: translate(80px, 40px) rotate(180deg);
		}
		75% {
			transform: translate(-50px, -30px) rotate(270deg);
		}
		100% {
			transform: translate(0, 0) rotate(360deg);
		}
	}

	@keyframes pearlPulse {
		0%, 100% {
			box-shadow:
				0 0 8px rgba(20, 184, 166, 0.4),
				0 0 16px rgba(20, 184, 166, 0.2),
				inset 0 0 8px rgba(94, 234, 212, 0.3);
		}
		50% {
			box-shadow:
				0 0 12px rgba(20, 184, 166, 0.6),
				0 0 24px rgba(20, 184, 166, 0.3),
				inset 0 0 12px rgba(94, 234, 212, 0.5);
		}
	}

	.ender-pearl {
		position: fixed;
		top: 100px;
		right: 150px;
		width: 16px;
		height: 16px;
		background: radial-gradient(circle at 35% 35%, rgba(94, 234, 212, 0.9), rgba(20, 184, 166, 0.7), rgba(13, 148, 136, 0.5));
		border-radius: 50%;
		cursor: pointer;
		user-select: none;
		animation: floatAround 30s ease-in-out infinite, pearlPulse 2s ease-in-out infinite;
		box-shadow:
			0 0 8px rgba(20, 184, 166, 0.4),
			0 0 16px rgba(20, 184, 166, 0.2),
			inset 0 0 8px rgba(94, 234, 212, 0.3);
		transition: transform 0.3s, width 0.3s, height 0.3s;
		z-index: 1000;
		opacity: 0.6;
	}

	.pearl-core {
		position: absolute;
		top: 3px;
		left: 4px;
		width: 4px;
		height: 4px;
		background: rgba(255, 255, 255, 0.9);
		border-radius: 50%;
		box-shadow: 0 0 4px rgba(255, 255, 255, 0.8);
	}

	.ender-pearl:hover {
		opacity: 1;
		animation: floatAround 30s ease-in-out infinite, pearlPulse 1s ease-in-out infinite;
		transform: scale(1.4);
		box-shadow:
			0 0 16px rgba(20, 184, 166, 0.7),
			0 0 32px rgba(20, 184, 166, 0.4),
			inset 0 0 12px rgba(94, 234, 212, 0.5);
	}

	.ender-pearl-active {
		width: 20px;
		height: 20px;
		opacity: 1;
		background: radial-gradient(circle at 35% 35%, rgba(139, 92, 246, 0.95), rgba(124, 58, 237, 0.8), rgba(109, 40, 217, 0.6));
		box-shadow:
			0 0 16px rgba(139, 92, 246, 0.6),
			0 0 32px rgba(139, 92, 246, 0.4),
			inset 0 0 12px rgba(167, 139, 250, 0.5);
		animation: floatAround 20s ease-in-out infinite, pearlPulse 1.5s ease-in-out infinite;
	}

	.ender-pearl-active .pearl-core {
		background: rgba(196, 181, 253, 0.95);
		box-shadow: 0 0 6px rgba(196, 181, 253, 0.9);
	}

	.ender-pearl-active:hover {
		transform: scale(1.6);
		box-shadow:
			0 0 24px rgba(139, 92, 246, 0.8),
			0 0 48px rgba(139, 92, 246, 0.5),
			inset 0 0 16px rgba(167, 139, 250, 0.7);
	}

	@keyframes pearlActivated {
		0%, 100% {
			transform: scale(1);
		}
		50% {
			transform: scale(1.8);
		}
	}

	@media (max-width: 768px) {
		.hero-section {
			flex-direction: column;
			text-align: center;
			padding: 30px;
		}

		.tagline {
			max-width: none;
		}

		.cards-grid {
			grid-template-columns: 1fr;
		}

		.support-links {
			flex-direction: column;
		}
	}
</style>
