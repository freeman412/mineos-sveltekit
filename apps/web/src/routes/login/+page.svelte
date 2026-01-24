<script lang="ts">
	import { enhance } from '$app/forms';
	import type { ActionData } from './$types';

	let { form }: { form: ActionData } = $props();
	let loading = $state(false);
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

<div class="container">
	<div class="login-box">
		<div class="header">
			<div class="logo-wrap">
				<img src="/mineos-logo.svg" alt="MineOS logo" class="logo-icon" />
				<div>
					<h1>MineOS</h1>
					<p class="tagline">Minecraft Control</p>
				</div>
			</div>
			<p class="subtitle">Sign in to manage your servers</p>
		</div>

		<form
			method="POST"
			use:enhance={() => {
				loading = true;
				return async ({ update }) => {
					await update();
					loading = false;
				};
			}}
		>
			<div class="field">
				<label for="username">Username</label>
				<input
					type="text"
					id="username"
					name="username"
					required
					autocomplete="username"
					disabled={loading}
				/>
			</div>

			<div class="field">
				<label for="password">Password</label>
				<input
					type="password"
					id="password"
					name="password"
					required
					autocomplete="current-password"
					disabled={loading}
				/>
			</div>

			{#if form?.error}
				<div class="error">{form.error}</div>
			{/if}

			<button type="submit" disabled={loading}>
				{loading ? 'Signing in...' : 'Sign in'}
			</button>
		</form>
	</div>
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

	.container {
		min-height: 100vh;
		display: flex;
		align-items: center;
		justify-content: center;
		padding: 24px;
	}

	.login-box {
		background: #1a1e2f;
		border-radius: 18px;
		padding: 40px;
		box-shadow: 0 24px 50px rgba(0, 0, 0, 0.35);
		width: 100%;
		max-width: 420px;
		border: 1px solid rgba(106, 176, 76, 0.2);
	}

	.header {
		text-align: center;
		margin-bottom: 32px;
	}

	.logo-wrap {
		display: flex;
		align-items: center;
		justify-content: center;
		gap: 12px;
		margin-bottom: 12px;
	}

	.logo-icon {
		width: 44px;
		height: 44px;
	}

	h1 {
		margin: 0 0 6px;
		font-size: 22px;
		font-weight: 400;
		font-family: 'Press Start 2P', 'Space Grotesk', sans-serif;
	}

	.tagline {
		margin: 0;
		font-size: 11px;
		color: #7c87b2;
		letter-spacing: 0.04em;
		text-transform: uppercase;
	}

	.subtitle {
		margin: 0;
		color: #aab2d3;
		font-size: 14px;
	}

	form {
		display: flex;
		flex-direction: column;
		gap: 20px;
	}

	.field {
		display: flex;
		flex-direction: column;
		gap: 8px;
	}

	label {
		font-size: 14px;
		font-weight: 500;
		color: #9aa2c5;
	}

	input {
		background: #141827;
		border: 1px solid #2a2f47;
		border-radius: 8px;
		padding: 12px 16px;
		color: #eef0f8;
		font-family: inherit;
		font-size: 14px;
		transition: border-color 0.2s;
	}

	input:focus {
		outline: none;
		border-color: rgba(106, 176, 76, 0.7);
		box-shadow: 0 0 0 2px rgba(106, 176, 76, 0.2);
	}

	input:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	button {
		background: #6ab04c;
		color: white;
		border: none;
		border-radius: 8px;
		padding: 14px 24px;
		font-family: inherit;
		font-size: 15px;
		font-weight: 600;
		cursor: pointer;
		transition: background 0.2s;
	}

	button:hover:not(:disabled) {
		background: #4a8b34;
	}

	button:disabled {
		opacity: 0.6;
		cursor: not-allowed;
	}

	.error {
		background: rgba(255, 92, 92, 0.1);
		border: 1px solid rgba(255, 92, 92, 0.3);
		border-radius: 8px;
		padding: 12px 16px;
		color: #ff9f9f;
		font-size: 14px;
	}
</style>
