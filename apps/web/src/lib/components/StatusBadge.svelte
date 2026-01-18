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
		background: #6ab04c;
		box-shadow: 0 0 6px rgba(106, 176, 76, 0.5);
	}

	.status-dot.error {
		background: #ff6b6b;
		box-shadow: 0 0 6px rgba(255, 107, 107, 0.5);
	}

	.status-dot.warning {
		background: #ffb74d;
		box-shadow: 0 0 6px rgba(255, 183, 77, 0.5);
	}

	.status-dot.info {
		background: #5b9eff;
		box-shadow: 0 0 6px rgba(91, 158, 255, 0.5);
	}

	.status-dot.neutral {
		background: #9aa2c5;
		box-shadow: 0 0 6px rgba(154, 162, 197, 0.5);
	}

	/* Color variants - badges */
	.status-badge.success {
		background: rgba(106, 176, 76, 0.15);
		color: #7ae68d;
		border: 1px solid rgba(106, 176, 76, 0.3);
	}

	.status-badge.error {
		background: rgba(255, 92, 92, 0.15);
		color: #ff9f9f;
		border: 1px solid rgba(255, 92, 92, 0.3);
	}

	.status-badge.warning {
		background: rgba(255, 183, 77, 0.15);
		color: #ffcc80;
		border: 1px solid rgba(255, 183, 77, 0.3);
	}

	.status-badge.info {
		background: rgba(91, 158, 255, 0.15);
		color: #a5b4fc;
		border: 1px solid rgba(91, 158, 255, 0.3);
	}

	.status-badge.neutral {
		background: rgba(154, 162, 197, 0.15);
		color: #c4cff5;
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
