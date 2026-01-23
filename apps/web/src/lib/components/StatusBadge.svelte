<script lang="ts">
	type Variant = 'success' | 'error' | 'warning' | 'info' | 'neutral';
	type Size = 'sm' | 'md' | 'lg';

	interface Props {
		/** The status variant determines the color */
		variant?: Variant;
		/** Size of the badge */
		size?: Size;
		/** If true, renders as a small dot indicator only */
		dot?: boolean;
		/** Whether to show a pulsing animation (useful for 'running' states) */
		pulse?: boolean;
		/** Additional CSS classes */
		class?: string;
	}

	let {
		variant = 'neutral',
		size = 'md',
		dot = false,
		pulse = false,
		class: className = ''
	}: Props = $props();
</script>

{#if dot}
	<span
		class="status-dot {variant} {size} {className}"
		class:pulse
	></span>
{:else}
	<span
		class="status-badge {variant} {size} {className}"
		class:pulse
	>
		<slot />
	</span>
{/if}

<style>
	/* Dot indicator styles */
	.status-dot {
		display: inline-block;
		border-radius: 50%;
		flex-shrink: 0;
	}

	.status-dot.sm {
		width: 6px;
		height: 6px;
	}

	.status-dot.md {
		width: 8px;
		height: 8px;
	}

	.status-dot.lg {
		width: 10px;
		height: 10px;
	}

	/* Badge styles */
	.status-badge {
		display: inline-flex;
		align-items: center;
		border-radius: 999px;
		font-weight: 600;
		text-transform: uppercase;
		white-space: nowrap;
	}

	.status-badge.sm {
		padding: 2px 8px;
		font-size: 10px;
	}

	.status-badge.md {
		padding: 4px 12px;
		font-size: 11px;
	}

	.status-badge.lg {
		padding: 6px 14px;
		font-size: 12px;
	}

	/* Color variants - dots */
	.status-dot.success {
		background: var(--color-success);
		box-shadow: 0 0 6px var(--color-success-border);
	}

	.status-dot.error {
		background: var(--color-error);
		box-shadow: 0 0 6px var(--color-error-border);
	}

	.status-dot.warning {
		background: var(--color-warning);
		box-shadow: 0 0 6px var(--color-warning-border);
	}

	.status-dot.info {
		background: var(--color-info);
		box-shadow: 0 0 6px var(--color-info-border);
	}

	.status-dot.neutral {
		background: var(--mc-text-muted);
		box-shadow: 0 0 6px rgba(154, 162, 197, 0.5);
	}

	/* Color variants - badges */
	.status-badge.success {
		background: var(--color-success-bg);
		color: var(--color-success-light);
		border: 1px solid var(--color-success-border);
	}

	.status-badge.error {
		background: var(--color-error-bg);
		color: var(--color-error-light);
		border: 1px solid var(--color-error-border);
	}

	.status-badge.warning {
		background: var(--color-warning-bg);
		color: var(--color-warning-light);
		border: 1px solid var(--color-warning-border);
	}

	.status-badge.info {
		background: var(--color-info-bg);
		color: var(--color-info-light);
		border: 1px solid var(--color-info-border);
	}

	.status-badge.neutral {
		background: rgba(154, 162, 197, 0.15);
		color: var(--mc-text-secondary);
		border: 1px solid rgba(154, 162, 197, 0.3);
	}

	/* Pulse animation for active states */
	.pulse {
		animation: pulse 2s ease-in-out infinite;
	}

	@keyframes pulse {
		0%,
		100% {
			opacity: 1;
		}
		50% {
			opacity: 0.6;
		}
	}
</style>
