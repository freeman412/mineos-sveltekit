<script lang="ts">
	import type { BackendForwarding, ProxyBackendSummary } from '$lib/api/types';

	interface Props {
		summary: ProxyBackendSummary | null;
		/** Name of the backend an action is currently running on, if any. */
		busyBackend?: string | null;
		/** Offer "Fix forwarding" when a backend advertises a remediation. */
		onremediate?: (backend: BackendForwarding) => void;
		/** Offer removing a backend from this proxy's list. */
		ondetach?: (backend: BackendForwarding) => void;
	}

	let { summary, busyBackend = null, onremediate, ondetach }: Props = $props();

	let backends = $derived(summary?.backends ?? []);
	let openCount = $derived(backends.filter((b) => b.isSpoofable).length);
	let hasRowActions = $derived(Boolean(onremediate || ondetach));

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
						{#if hasRowActions}
							<th>Actions</th>
						{/if}
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
							{#if hasRowActions}
								<td class="row-actions">
									{#if backend.remediationAction && onremediate}
										<button
											class="row-btn fix"
											type="button"
											disabled={busyBackend !== null}
											onclick={() => onremediate(backend)}
										>
											{busyBackend === backend.serverName
												? 'Fixing…'
												: backend.remediationAction === 'install-mod'
													? 'Install forwarding mod'
													: 'Fix forwarding'}
										</button>
									{/if}
									{#if ondetach}
										<button
											class="row-btn detach"
											type="button"
											disabled={busyBackend !== null}
											title="Remove {backend.serverName} from this proxy's backend list"
											onclick={() => ondetach(backend)}
										>
											Remove
										</button>
									{/if}
								</td>
							{/if}
						</tr>
					{/each}
				</tbody>
			</table>
		</div>

		<p class="note">
			{#if onremediate}
				Fix forwarding lets the proxy vouch for a backend's players. Backends that cannot verify
				forwarded players — Forge, vanilla, or anything behind BungeeCord — rely entirely on not
				being reachable from outside.
			{:else}
				Open a server to secure it. Backends that cannot verify forwarded players — Forge,
				vanilla, or anything behind BungeeCord — rely entirely on not being reachable from
				outside.
			{/if}
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

	.row-actions {
		display: flex;
		gap: 6px;
	}

	.row-btn {
		padding: 4px 10px;
		font-size: 0.78rem;
		font-weight: 600;
		font-family: inherit;
		border-radius: 6px;
		border: 1px solid var(--border-color, #2a2f47);
		background: var(--mc-panel-light, #2a2f47);
		color: #c4cff5;
		cursor: pointer;
	}

	.row-btn:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.row-btn.fix {
		border-color: rgba(106, 176, 76, 0.4);
		background: rgba(106, 176, 76, 0.12);
		color: var(--mc-grass, #6ab04c);
	}

	.row-btn.detach:hover:not(:disabled) {
		border-color: rgba(210, 94, 72, 0.5);
		color: #ffb6a6;
	}
</style>
