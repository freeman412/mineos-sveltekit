<script lang="ts">
	type ColorVariant = 'green' | 'blue' | 'orange' | 'red';
	type Size = 'sm' | 'md' | 'lg';

	interface Props {
		/** Progress value from 0-100 */
		value?: number;
		/** Whether to show indeterminate animated progress */
		indeterminate?: boolean;
		/** Color variant */
		color?: ColorVariant;
		/** Size variant */
		size?: Size;
		/** Show percentage label */
		showLabel?: boolean;
		/** Custom label text (overrides percentage) */
		label?: string;
		/** CSS class for the container */
		class?: string;
	}

	let {
		value = 0,
		indeterminate = false,
		color = 'green',
		size = 'md',
		showLabel = false,
		label,
		class: className = ''
	}: Props = $props();

	const clampedValue = $derived(Math.max(0, Math.min(100, value)));
	const displayLabel = $derived(label ?? `${Math.round(clampedValue)}%`);
</script>

<div
	class="progress-container {size} {className}"
	class:with-label={showLabel}
	role="progressbar"
	aria-valuenow={indeterminate ? undefined : clampedValue}
	aria-valuemin={0}
	aria-valuemax={100}
>
	<div class="progress-track">
		{#if indeterminate}
			<div class="progress-fill indeterminate {color}"></div>
		{:else}
			<div class="progress-fill {color}" style="width: {clampedValue}%"></div>
		{/if}
	</div>
	{#if showLabel}
		<span class="progress-label">{displayLabel}</span>
	{/if}
</div>

<style>
	.progress-container {
		display: flex;
		align-items: center;
		gap: 10px;
		width: 100%;
	}

	.progress-track {
		flex: 1;
		background: #2a2f47;
		border-radius: 4px;
		overflow: hidden;
	}

	/* Size variants */
	.progress-container.sm .progress-track {
		height: 4px;
	}

	.progress-container.md .progress-track {
		height: 6px;
	}

	.progress-container.lg .progress-track {
		height: 10px;
		border: 1px solid rgba(42, 47, 71, 0.8);
	}

	/* Progress fill */
	.progress-fill {
		height: 100%;
		border-radius: 4px;
		transition: width 0.3s ease;
	}

	/* Color variants */
	.progress-fill.green {
		background: linear-gradient(90deg, var(--color-success), var(--color-success-light));
	}

	.progress-fill.blue {
		background: linear-gradient(90deg, var(--color-info), var(--color-info-light));
	}

	.progress-fill.orange {
		background: linear-gradient(90deg, var(--color-warning), var(--color-warning-light));
	}

	.progress-fill.red {
		background: linear-gradient(90deg, var(--color-error), var(--color-error-light));
	}

	/* Indeterminate animation */
	.progress-fill.indeterminate {
		width: 50%;
		animation: indeterminate 1.5s ease-in-out infinite;
	}

	.progress-fill.indeterminate.green {
		background: linear-gradient(90deg, rgba(106, 176, 76, 0.8), rgba(124, 212, 114, 0.9), rgba(106, 176, 76, 0.8));
		background-size: 200% 100%;
	}

	.progress-fill.indeterminate.blue {
		background: linear-gradient(90deg, #5b9eff, #79c0ff, #5b9eff);
		background-size: 200% 100%;
	}

	.progress-fill.indeterminate.orange {
		background: linear-gradient(90deg, #f6b26b, #ffc77d, #f6b26b);
		background-size: 200% 100%;
	}

	.progress-fill.indeterminate.red {
		background: linear-gradient(90deg, #ff5c5c, #ff7a7a, #ff5c5c);
		background-size: 200% 100%;
	}

	@keyframes indeterminate {
		0% {
			transform: translateX(-100%);
		}
		100% {
			transform: translateX(200%);
		}
	}

	/* Label */
	.progress-label {
		font-size: 11px;
		color: #9aa2c5;
		min-width: 40px;
		text-align: right;
		flex-shrink: 0;
	}

	.progress-container.sm .progress-label {
		font-size: 10px;
		min-width: 32px;
	}

	.progress-container.lg .progress-label {
		font-size: 13px;
		min-width: 48px;
	}
</style>
