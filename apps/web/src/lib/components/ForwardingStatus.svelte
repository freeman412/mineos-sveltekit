<script lang="ts">
	import { invalidateAll } from '$app/navigation';
	import { secureBackend, installForwardingMod } from '$lib/api/client';
	import type { BackendForwarding } from '$lib/api/types';

	let { forwarding }: { forwarding: BackendForwarding | null } = $props();

	let busy = $state(false);
	let error = $state<string | null>(null);

	// A server nothing proxies has nothing to say here. Showing a reassuring
	// "not applicable" panel on every standalone server would be noise, and
	// noise is what makes people stop reading security warnings.
	let visible = $derived(forwarding !== null && forwarding.status !== 'NotABackend');

	let headline = $derived.by(() => {
		if (!forwarding) return '';
		if (forwarding.isSpoofable) return 'Anyone can join this server as anyone';
		switch (forwarding.status) {
			case 'Secured':
				return 'Protected by verified proxy forwarding';
			case 'Securable':
				return 'Not reachable through the proxy yet';
			case 'Misconfigured':
				return 'Proxy forwarding is misconfigured';
			case 'Unverifiable':
				return 'This server cannot verify forwarded players';
			default:
				return '';
		}
	});

	let detail = $derived.by(() => {
		if (!forwarding) return '';
		const proxy = forwarding.proxyName ?? 'a proxy';

		if (forwarding.isSpoofable) {
			return (
				`This server has online-mode turned off, so it no longer checks who players are — ` +
				`and nothing is verifying that for it. Anyone who can reach its port directly can join ` +
				`as any username, including an operator's.`
			);
		}

		switch (forwarding.status) {
			case 'Secured':
				return `${proxy} signs each player's identity and this server verifies the signature.`;
			case 'Securable':
				return (
					`${proxy} lists this server as a backend, but players cannot join through it yet: ` +
					`this server still authenticates them itself. Securing it hands that job to the proxy.`
				);
			case 'Misconfigured':
				return (
					`Verified forwarding is set up, but this server still has online-mode turned on, ` +
					`so players cannot join through ${proxy}. Nobody can impersonate anyone — it is broken, not open.`
				);
			case 'Unverifiable':
				return (
					`${forwarding.loader ?? 'This server'} has no way to check that forwarded player ` +
					`identities are genuine, so keeping its port unreachable from outside is the only protection.`
				);
			default:
				return '';
		}
	});

	async function installMod() {
		if (!forwarding) return;
		busy = true;
		error = null;
		const result = await installForwardingMod(fetch, forwarding.serverName);
		busy = false;
		if (result.error) {
			error = result.error;
			return;
		}
		await invalidateAll();
	}

	async function secure() {
		if (!forwarding) return;
		busy = true;
		error = null;
		const result = await secureBackend(fetch, forwarding.serverName);
		busy = false;
		if (result.error) {
			error = result.error;
			return;
		}
		await invalidateAll();
	}
</script>

{#if visible && forwarding}
	<section
		class="forwarding"
		class:danger={forwarding.isSpoofable}
		class:ok={forwarding.status === 'Secured'}
	>
		<div class="head">
			<h2>{headline}</h2>
			{#if forwarding.proxyName}
				<span class="chip">behind {forwarding.proxyName}</span>
			{/if}
		</div>

		<p>{detail}</p>

		{#if forwarding.status === 'Unverifiable' || forwarding.isSpoofable}
			<p class="exposure" class:danger={forwarding.exposure === 'Exposed'}>
				{#if forwarding.exposure === 'Exposed'}
					<strong>Reachable from outside.</strong>
				{:else if forwarding.exposure === 'NotExposed'}
					<strong>Not reachable from outside.</strong>
				{:else}
					<strong>Exposure unknown.</strong>
				{/if}
				{forwarding.exposureDetail ?? ''}
			</p>
		{/if}

		{#if error}
			<p class="error">{error}</p>
		{/if}

		{#if forwarding.remediationAction === 'secure'}
			<button type="button" onclick={secure} disabled={busy}>
				{busy ? 'Securing…' : 'Secure this backend'}
			</button>
			<p class="note">
				Writes this server's forwarding config and turns off its own online-mode. Both this
				server and the proxy need a restart afterwards.
			</p>
		{:else if forwarding.remediationAction === 'install-mod'}
			<button type="button" onclick={installMod} disabled={busy}>
				{busy ? 'Installing…' : 'Install FabricProxy-Lite'}
			</button>
			<p class="note">
				Fabric servers need the <strong>FabricProxy-Lite</strong> mod to verify forwarded players.
				MineOS picks a build matching this server's Minecraft version; you can secure the backend
				once it is installed.
			</p>
		{/if}
	</section>
{/if}

<style>
	.forwarding {
		background: var(--mc-panel, rgba(22, 27, 46, 0.95));
		border: 1px solid var(--border-color, #2a2f47);
		border-left: 4px solid var(--warning, #d99a2b);
		border-radius: 0.75rem;
		padding: 1.25rem;
		margin-bottom: 1rem;
	}

	.forwarding.danger {
		border-left-color: var(--danger, #d9534f);
	}

	.forwarding.ok {
		border-left-color: var(--success, #4caf50);
	}

	.head {
		display: flex;
		align-items: center;
		gap: 0.75rem;
		flex-wrap: wrap;
	}

	h2 {
		margin: 0;
		font-size: 1rem;
	}

	.chip {
		font-size: 0.75rem;
		padding: 0.15rem 0.5rem;
		border-radius: 999px;
		border: 1px solid var(--border-color, #2a2f47);
		opacity: 0.85;
	}

	p {
		margin: 0.6rem 0 0;
		font-size: 0.9rem;
		line-height: 1.5;
	}

	.exposure.danger {
		color: var(--danger, #d9534f);
	}

	.note {
		font-size: 0.8rem;
		opacity: 0.75;
	}

	.error {
		color: var(--danger, #d9534f);
	}

	button {
		margin-top: 0.9rem;
		padding: 0.5rem 1rem;
		border-radius: 0.5rem;
		border: 1px solid var(--border-color, #2a2f47);
		background: var(--mc-accent, #3a6ea5);
		color: inherit;
		cursor: pointer;
	}

	button:disabled {
		opacity: 0.6;
		cursor: default;
	}
</style>
