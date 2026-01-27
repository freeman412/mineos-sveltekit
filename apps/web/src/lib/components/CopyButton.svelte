<script lang="ts">
	import { modal } from '$lib/stores/modal';

	type Variant = 'solid' | 'ghost';
	type Size = 'sm' | 'md';

	let {
		value,
		title = 'Copy',
		ariaLabel,
		variant = 'solid',
		size = 'md',
		className = '',
		showErrors = true
	}: {
		value: string;
		title?: string;
		ariaLabel?: string;
		variant?: Variant;
		size?: Size;
		className?: string;
		showErrors?: boolean;
	} = $props();

	let copied = $state(false);
	let resetTimer: ReturnType<typeof setTimeout> | null = null;

	async function copyValue(event: MouseEvent) {
		event.stopPropagation();
		event.preventDefault();
		if (!value) return;

		const setCopied = () => {
			copied = true;
			if (resetTimer) clearTimeout(resetTimer);
			resetTimer = setTimeout(() => {
				copied = false;
			}, 2000);
		};

		try {
			await navigator.clipboard.writeText(value);
			setCopied();
		} catch {
			try {
				const textarea = document.createElement('textarea');
				textarea.value = value;
				textarea.style.position = 'fixed';
				textarea.style.opacity = '0';
				document.body.appendChild(textarea);
				textarea.select();
				const success = document.execCommand('copy');
				document.body.removeChild(textarea);

				if (success) {
					setCopied();
				} else if (showErrors) {
					await modal.error('Failed to copy to clipboard');
				}
			} catch (fallbackErr) {
				if (showErrors) {
					await modal.error('Failed to copy to clipboard');
				}
			}
		}
	}
</script>

<button
	type="button"
	class={`copy-button ${variant} ${size} ${className}`.trim()}
	onclick={copyValue}
	title={title}
	aria-label={ariaLabel ?? title}
	disabled={!value}
>
	{#if copied}
		<svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true">
			<path
				d="M5 13l4 4L19 7"
				fill="none"
				stroke="currentColor"
				stroke-width="2.5"
				stroke-linecap="round"
				stroke-linejoin="round"
			/>
		</svg>
	{:else}
		<svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true">
			<rect
				x="9"
				y="9"
				width="13"
				height="13"
				rx="2"
				fill="none"
				stroke="currentColor"
				stroke-width="2"
				stroke-linecap="round"
				stroke-linejoin="round"
			/>
			<path
				d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"
				fill="none"
				stroke="currentColor"
				stroke-width="2"
				stroke-linecap="round"
				stroke-linejoin="round"
			/>
		</svg>
	{/if}
</button>

<style>
	.copy-button {
		border: none;
		padding: 0;
		cursor: pointer;
		display: inline-flex;
		align-items: center;
		justify-content: center;
		transition: all 0.2s ease;
	}

	.copy-button:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.copy-button svg {
		display: block;
	}

	.copy-button.solid {
		background: var(--mc-grass, #6ab04c);
		color: white;
		box-shadow: 0 4px 12px rgba(106, 176, 76, 0.25);
	}

	.copy-button.solid:hover:not(:disabled) {
		background: var(--mc-grass-dark, #4a8b34);
		transform: translateY(-1px);
	}

	.copy-button.ghost {
		background: rgba(15, 18, 32, 0.6);
		border: 1px solid rgba(88, 101, 242, 0.35);
		color: #c7cbe0;
	}

	.copy-button.ghost:hover:not(:disabled) {
		background: rgba(88, 101, 242, 0.2);
		color: #eef0f8;
		transform: translateY(-1px);
	}

	.copy-button.sm {
		width: 22px;
		height: 22px;
		border-radius: 999px;
	}

	.copy-button.md {
		width: 40px;
		height: 40px;
		border-radius: 8px;
	}
</style>
