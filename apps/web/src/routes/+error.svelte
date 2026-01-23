<script lang="ts">
	export let status: number;
	export let error: App.Error;

	const code = status || 500;
	const detail = error?.message;

	const errorInfo = (() => {
		const table: Record<number, { title: string; description: string; hint: string }> = {
			400: {
				title: 'Bad request',
				description: 'That request did not look right from here.',
				hint: 'Check the form values and try again.'
			},
			401: {
				title: 'Sign in required',
				description: 'You need to authenticate before continuing.',
				hint: 'Sign in and retry the action.'
			},
			403: {
				title: 'Access denied',
				description: 'You do not have permission to view this area.',
				hint: 'Ask an admin to grant access.'
			},
			404: {
				title: 'Not found',
				description: 'We could not find that page or resource.',
				hint: 'Double-check the URL or go back to the dashboard.'
			},
			409: {
				title: 'Conflict',
				description: 'Something already exists with that name.',
				hint: 'Pick a different name and try again.'
			},
			429: {
				title: 'Too many requests',
				description: 'You are doing that a bit too quickly.',
				hint: 'Wait a moment and try again.'
			},
			500: {
				title: 'Server error',
				description: 'The server hit an unexpected problem.',
				hint: 'Check logs or try again soon.'
			},
			502: {
				title: 'Upstream error',
				description: 'The service upstream did not respond correctly.',
				hint: 'Try again in a moment.'
			},
			503: {
				title: 'Service unavailable',
				description: 'The service is taking a break right now.',
				hint: 'Try again after a short wait.'
			},
			504: {
				title: 'Gateway timeout',
				description: 'That request took too long to complete.',
				hint: 'Retry or check the server status.'
			}
		};

		if (table[code]) return table[code];
		if (code >= 500) {
			return {
				title: 'Server trouble',
				description: 'Something went wrong on our side.',
				hint: 'Try again later.'
			};
		}

		return {
			title: 'Request failed',
			description: 'We could not complete that request.',
			hint: 'Go back and try again.'
		};
	})();
</script>

<svelte:head>
	<title>{code} {errorInfo.title} | MineOS</title>
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

<div class="failwhale-page">
	<div class="copy">
		<div class="eyebrow">Error {code}</div>
		<h1>{errorInfo.title}</h1>
		<p class="description">{errorInfo.description}</p>
		<p class="hint">{errorInfo.hint}</p>
		{#if detail}
			<p class="detail">{detail}</p>
		{/if}
		<div class="actions">
			<a class="btn-primary" href="/">Go to dashboard</a>
			<button class="btn-secondary" type="button" onclick={() => history.back()}>
				Go back
			</button>
		</div>
	</div>

	<div class="failwhale">
		<img src="/minecraft_failwhale.png" alt="Minecraft failwhale resting on a tiny island." />
		<div class="sign-code" aria-hidden="true">{code}</div>
	</div>
</div>

<style>
	.failwhale-page {
		max-width: 1200px;
		min-height: 100vh;
		padding: 56px 24px 80px;
		display: grid;
		grid-template-columns: minmax(260px, 420px) minmax(320px, 1fr);
		gap: 40px;
		align-items: center;
		margin: 0 auto;
		color: var(--mc-text);
	}

	.copy {
		display: flex;
		flex-direction: column;
		gap: 12px;
		background: linear-gradient(160deg, rgba(26, 30, 47, 0.95), rgba(17, 20, 34, 0.95));
		border: 1px solid var(--border-color);
		border-radius: 20px;
		padding: 28px;
		box-shadow: 0 25px 50px rgba(0, 0, 0, 0.35);
	}

	.eyebrow {
		text-transform: uppercase;
		letter-spacing: 0.18em;
		font-size: 12px;
		color: var(--mc-text-muted);
	}

	h1 {
		margin: 0;
		font-size: 36px;
		font-weight: 600;
	}

	.description {
		margin: 0;
		font-size: 16px;
		color: var(--mc-text-secondary);
	}

	.hint {
		margin: 0;
		font-size: 14px;
		color: var(--mc-text-muted);
	}

	.detail {
		margin: 8px 0 0;
		font-size: 12px;
		color: var(--mc-text-dim);
		word-break: break-word;
	}

	.actions {
		margin-top: 16px;
		display: flex;
		gap: 12px;
		flex-wrap: wrap;
	}

	.btn-primary,
	.btn-secondary {
		border-radius: 10px;
		padding: 10px 18px;
		font-size: 14px;
		font-weight: 600;
		border: none;
		cursor: pointer;
		text-decoration: none;
		font-family: inherit;
	}

	.btn-primary {
		background: var(--mc-grass);
		color: white;
	}

	.btn-secondary {
		background: var(--mc-panel-light);
		color: var(--mc-text-secondary);
	}

	.failwhale {
		position: relative;
		width: 100%;
		max-width: 900px;
		justify-self: center;
	}

	.failwhale img {
		width: 100%;
		height: auto;
		display: block;
		border-radius: 24px;
		border: 1px solid rgba(42, 47, 71, 0.6);
		box-shadow: 0 30px 60px rgba(0, 0, 0, 0.35);
	}

	.sign-code {
		position: absolute;
		top: 34%;
		left: 24%;
		transform: rotate(-2deg);
		font-family: "JetBrains Mono", "Consolas", monospace;
		font-weight: 700;
		font-size: clamp(18px, 4vw, 40px);
		color: #8f0000;
		text-shadow: 0 2px 0 rgba(255, 255, 255, 0.35);
	}

	@media (max-width: 900px) {
		.failwhale-page {
			grid-template-columns: 1fr;
			padding: 32px 20px 48px;
		}

		.copy {
			text-align: center;
		}

		.actions {
			justify-content: center;
		}

		.sign-code {
			left: 20%;
			top: 26%;
		}
	}

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
		--mc-panel-darkest: #0d1117;
		--mc-panel-dark: #141827;
		--mc-panel: #1a1e2f;
		--mc-panel-light: #2a2f47;
		--mc-panel-lighter: #3a3f5a;
		--mc-text: #eef0f8;
		--mc-text-secondary: #c4cff5;
		--mc-text-muted: #9aa2c5;
		--mc-text-dim: #7c87b2;
		--border-color: #2a2f47;
	}
</style>
