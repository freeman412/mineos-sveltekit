<script lang="ts">
	import type { ProxyBackendSummary, BackendForwarding } from '$lib/api/types';

	let { summary }: { summary: ProxyBackendSummary | null } = $props();

	let backends = $derived(summary?.backends ?? []);
	let openCount = $derived(backends.filter((b) => b.isSpoofable).length);

	function label(b: BackendForwarding): string {
		if (b.isSpoofable) return 'Open to impersonation';
		switch (b.status) {
			case 'Secured':
				return 'Secured';
			case 'Securable':
				return 'Not secured yet';
			case 'Misconfigured':
				return 'Misconfigured';
			case 'Unverifiable':
				return 'Cannot be verified';
			default:
				return 'Not a backend';
		}
	}

	function tone(b: BackendForwarding): string {
		if (b.isSpoofable) return 'danger';
		if (b.status === 'Secured') return 'ok';
		return 'warn';
	}
</script>

{#if backends.length > 0}
	<section class="rollup">
		<div class="head">
			<h2>Backend security</h2>
			{#if openCount > 0}
				<span class="chip danger">
					{openCount} of {backends.length} open to impersonation
				</span>
			{:else}
				<span class="chip">{backends.length} backend{backends.length === 1 ? '' : 's'}</span>
			{/if}
		</div>

		<div class="scroll">
			<table>
				<thead>
					<tr>
						<th>Server</th>
						<th>Type</th>
						<th>Status</th>
						<th>Reachable from outside</th>
					</tr>
				</thead>
				<tbody>
					{#each backends as backend (backend.serverName)}
						<tr>
							<td>
								<a href="/servers/{backend.serverName}">{backend.serverName}</a>
							</td>
							<td>{backend.loader ?? 'unknown'}</td>
							<td class={tone(backend)}>{label(backend)}</td>
							<td>
								{#if backend.status === 'Secured'}
									<span class="muted">n/a — identities are verified</span>
								{:else if backend.exposure === 'Exposed'}
									<span class="danger">Yes</span>
								{:else if backend.exposure === 'NotExposed'}
									<span class="ok">No</span>
								{:else}
									<span class="muted">Unknown</span>
								{/if}
							</td>
						</tr>
					{/each}
				</tbody>
			</table>
		</div>

		<p class="note">
			Open a server to secure it. Backends that cannot verify forwarded players — Forge, vanilla,
			or anything behind BungeeCord — rely entirely on not being reachable from outside.
		</p>
	</section>
{/if}

<style>
	.rollup {
		background: var(--mc-panel, rgba(22, 27, 46, 0.95));
		border: 1px solid var(--border-color, #2a2f47);
		border-radius: 0.75rem;
		padding: 1.25rem;
		margin-bottom: 1rem;
	}

	.head {
		display: flex;
		align-items: center;
		gap: 0.75rem;
		flex-wrap: wrap;
		margin-bottom: 0.75rem;
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
	}

	/* Wide content scrolls inside its own box rather than the page. */
	.scroll {
		overflow-x: auto;
	}

	table {
		width: 100%;
		border-collapse: collapse;
		font-size: 0.875rem;
	}

	th,
	td {
		text-align: left;
		padding: 0.5rem 0.75rem;
		border-bottom: 1px solid var(--border-color, #2a2f47);
		white-space: nowrap;
	}

	th {
		font-weight: 600;
		opacity: 0.8;
	}

	.danger {
		color: var(--danger, #d9534f);
	}

	.ok {
		color: var(--success, #4caf50);
	}

	.warn {
		color: var(--warning, #d99a2b);
	}

	.muted {
		opacity: 0.65;
	}

	.note {
		margin: 0.75rem 0 0;
		font-size: 0.8rem;
		opacity: 0.75;
		line-height: 1.5;
	}
</style>
