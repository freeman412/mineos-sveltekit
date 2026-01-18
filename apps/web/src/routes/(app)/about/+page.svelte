<script lang="ts">
	const version = '2.0.0-alpha';
</script>

<div class="about-page">
	<header class="page-header">
		<h1>About MineOS</h1>
		<p class="subtitle">Modern Minecraft Server Management</p>
	</header>

	<div class="hero-section">
		<div class="logo-container">
			<svg viewBox="0 0 100 100" class="logo">
				<defs>
					<linearGradient id="grass-gradient" x1="0%" y1="0%" x2="0%" y2="100%">
						<stop offset="0%" stop-color="#7bc96f" />
						<stop offset="40%" stop-color="#6ab04c" />
						<stop offset="100%" stop-color="#4a8035" />
					</linearGradient>
					<linearGradient id="dirt-gradient" x1="0%" y1="0%" x2="0%" y2="100%">
						<stop offset="0%" stop-color="#8b6914" />
						<stop offset="100%" stop-color="#5c4a1f" />
					</linearGradient>
				</defs>
				<!-- Block base -->
				<rect x="10" y="10" width="80" height="80" rx="8" fill="url(#dirt-gradient)" />
				<!-- Grass top -->
				<rect x="10" y="10" width="80" height="25" rx="8" fill="url(#grass-gradient)" />
				<rect x="10" y="27" width="80" height="8" fill="url(#grass-gradient)" />
				<!-- Pixelated grass detail -->
				<rect x="15" y="32" width="8" height="6" fill="#6ab04c" opacity="0.6" />
				<rect x="30" y="35" width="6" height="8" fill="#6ab04c" opacity="0.5" />
				<rect x="55" y="33" width="7" height="5" fill="#6ab04c" opacity="0.6" />
				<rect x="75" y="34" width="8" height="6" fill="#6ab04c" opacity="0.5" />
				<!-- Server icon overlay -->
				<rect x="30" y="50" width="40" height="30" rx="4" fill="#1a1e2f" opacity="0.9" />
				<rect x="35" y="56" width="30" height="3" rx="1" fill="#6ab04c" />
				<rect x="35" y="62" width="22" height="3" rx="1" fill="#5865f2" />
				<rect x="35" y="68" width="26" height="3" rx="1" fill="#9aa2c5" />
			</svg>
		</div>
		<div class="hero-text">
			<h2>MineOS SvelteKit Edition</h2>
			<p class="version">Version {version}</p>
			<p class="tagline">
				A complete rewrite of the beloved MineOS web UI, built with modern technologies
				for a faster, more responsive Minecraft server management experience.
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
					<span class="tech-name">.NET 9</span>
					<span class="tech-desc">High-performance backend API</span>
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
				<li>One-click server creation with profile templates</li>
				<li>Integrated mod management via CurseForge</li>
				<li>World backup, restore, and management</li>
				<li>BuildTools integration for Spigot/Paper builds</li>
				<li>Multi-user access control</li>
				<li>Server console with command history</li>
				<li>Responsive design for desktop and mobile</li>
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
			<div class="card-icon">🗺️</div>
			<h3>Roadmap</h3>
			<p>
				Here's what's planned for future releases. Want to help? Pick an item and contribute!
			</p>
			<div class="roadmap-grid">
				<div class="roadmap-category">
					<h4>High Priority</h4>
					<ul>
						<li><span class="status planned">Planned</span> Visual Cron Job Builder</li>
						<li><span class="status planned">Planned</span> Plugin Configuration Editor</li>
						<li><span class="status planned">Planned</span> Bulk Server Operations</li>
						<li><span class="status planned">Planned</span> Discord Integration</li>
					</ul>
				</div>
				<div class="roadmap-category">
					<h4>Content Management</h4>
					<ul>
						<li><span class="status planned">Planned</span> Server Version Migration</li>
						<li><span class="status planned">Planned</span> Resource Pack Manager</li>
						<li><span class="status planned">Planned</span> Backup Comparison & Diff</li>
						<li><span class="status planned">Planned</span> Dynmap Integration</li>
					</ul>
				</div>
				<div class="roadmap-category">
					<h4>Templates & Automation</h4>
					<ul>
						<li><span class="status planned">Planned</span> Server Templates</li>
						<li><span class="status planned">Planned</span> Auto Server Detection</li>
						<li><span class="status planned">Planned</span> Server Cloning</li>
						<li><span class="status planned">Planned</span> Command Palette (Cmd+K)</li>
					</ul>
				</div>
				<div class="roadmap-category">
					<h4>Advanced Features</h4>
					<ul>
						<li><span class="status future">Future</span> Multi-Host Support</li>
						<li><span class="status future">Future</span> Log Analysis with ML</li>
						<li><span class="status future">Future</span> API Keys & Webhooks</li>
						<li><span class="status future">Future</span> Performance Profiling</li>
					</ul>
				</div>
			</div>
		</section>
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
		background: linear-gradient(135deg, #6ab04c 0%, #a8e063 100%);
		-webkit-background-clip: text;
		-webkit-text-fill-color: transparent;
		background-clip: text;
	}

	.subtitle {
		margin: 0;
		color: #9aa2c5;
		font-size: 18px;
	}

	.hero-section {
		display: flex;
		align-items: center;
		gap: 40px;
		background: linear-gradient(135deg, #1a1e2f 0%, #141827 100%);
		border-radius: 20px;
		padding: 40px;
		margin-bottom: 32px;
		border: 1px solid rgba(106, 176, 76, 0.2);
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
		color: #eef0f8;
	}

	.version {
		display: inline-block;
		background: rgba(106, 176, 76, 0.15);
		color: #6ab04c;
		padding: 4px 12px;
		border-radius: 20px;
		font-size: 13px;
		font-weight: 600;
		margin-bottom: 16px;
	}

	.tagline {
		margin: 0;
		color: #9aa2c5;
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
		background: linear-gradient(135deg, #1a1e2f 0%, #141827 100%);
		border-radius: 16px;
		padding: 28px;
		border: 1px solid #2a2f47;
		transition: all 0.2s;
	}

	.card:hover {
		border-color: rgba(106, 176, 76, 0.3);
		transform: translateY(-2px);
	}

	.card-icon {
		font-size: 32px;
		margin-bottom: 16px;
	}

	.card h3 {
		margin: 0 0 16px;
		font-size: 20px;
		color: #eef0f8;
	}

	.card p {
		margin: 0 0 16px;
		color: #9aa2c5;
		font-size: 14px;
		line-height: 1.6;
	}

	.card a {
		color: #6ab04c;
		text-decoration: none;
		transition: color 0.2s;
	}

	.card a:hover {
		color: #a8e063;
		text-decoration: underline;
	}

	.attribution {
		display: flex;
		gap: 8px;
		padding-top: 12px;
		border-top: 1px solid #2a2f47;
		font-size: 14px;
	}

	.attribution .label {
		color: #7c87b2;
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
		border: 1px solid #2a2f47;
	}

	.tech-name {
		font-weight: 600;
		color: #eef0f8;
		font-size: 14px;
	}

	.tech-desc {
		color: #7c87b2;
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
		border: 1px solid #2a2f47;
		text-decoration: none !important;
		transition: all 0.2s;
	}

	.integration:hover {
		border-color: rgba(106, 176, 76, 0.4);
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
		color: #eef0f8;
		font-size: 15px;
	}

	.integration-desc {
		color: #7c87b2;
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
		background: rgba(106, 176, 76, 0.08);
		border-radius: 8px;
		color: #c4d1c0;
		font-size: 14px;
		border-left: 3px solid rgba(106, 176, 76, 0.4);
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
		background: rgba(106, 176, 76, 0.1);
		border: 1px solid rgba(106, 176, 76, 0.3);
		border-radius: 8px;
		color: #b7f5a2 !important;
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
		background: rgba(88, 101, 242, 0.1);
		border: 1px solid rgba(88, 101, 242, 0.2);
		border-radius: 10px;
		padding: 16px 16px 16px 20px;
		margin-bottom: 16px;
	}

	.ai-built .contribute-steps ol {
		margin: 0;
		padding-left: 20px;
		color: #9aa2c5;
		font-size: 14px;
		line-height: 1.8;
	}

	.ai-built .ai-note {
		background: rgba(106, 176, 76, 0.1);
		border-radius: 8px;
		padding: 12px 16px;
		color: #b7f5a2;
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
		border: 1px solid #2a2f47;
		border-radius: 12px;
		padding: 16px;
	}

	.roadmap-category h4 {
		margin: 0 0 12px;
		font-size: 14px;
		font-weight: 600;
		color: #a5b4fc;
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
		border-bottom: 1px solid #2a2f47;
		font-size: 13px;
		color: #9aa2c5;
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
		background: rgba(106, 176, 76, 0.15);
		color: #6ab04c;
	}

	.status.future {
		background: rgba(88, 101, 242, 0.15);
		color: #a5b4fc;
	}

	.about-footer {
		text-align: center;
		padding: 32px 0;
		border-top: 1px solid #2a2f47;
	}

	.about-footer p {
		margin: 0 0 8px;
		color: #9aa2c5;
		font-size: 14px;
	}

	.copyright {
		font-size: 12px !important;
		color: #7c87b2 !important;
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
