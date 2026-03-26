<!-- apps/web/src/lib/components/InstallProgress.svelte -->
<script lang="ts">
	import ProgressBar from './ProgressBar.svelte';

	interface Props {
		/** SSE stream URL to connect to */
		streamUrl: string;
		/** Label for the progress display */
		label: string;
		/** Called when install completes successfully */
		oncomplete?: () => void;
		/** Called when install fails */
		onerror?: (error: string) => void;
		/** Called when install is rolled back */
		onrollback?: () => void;
		/** Color for the progress bar */
		color?: 'green' | 'blue' | 'orange' | 'red';
	}

	let {
		streamUrl,
		label,
		oncomplete,
		onerror,
		onrollback,
		color = 'green'
	}: Props = $props();

	let progress = $state(0);
	let currentStep = $state('Starting...');
	let output = $state('');
	let outputExpanded = $state(false);
	let watching = $state(false);
	let completed = $state(false);
	let error = $state('');

	let eventSource: EventSource | null = null;

	function startWatch() {
		if (watching) return;
		watching = true;
		error = '';

		eventSource = new EventSource(streamUrl);
		eventSource.onmessage = (event) => {
			try {
				const data = JSON.parse(event.data);
				progress = data.progress ?? 0;
				currentStep = data.currentStep || 'Installing...';
				if (data.output) output = data.output;

				if (data.status === 'completed') {
					eventSource?.close();
					completed = true;
					oncomplete?.();
				} else if (data.status === 'failed') {
					eventSource?.close();
					error = data.error || 'Installation failed';
					onerror?.(error);
				}
			} catch (err) {
				console.error('Failed to parse install event:', err);
			}
		};
		eventSource.onerror = () => {
			eventSource?.close();
			if (!completed) {
				error = 'Lost connection to install stream';
				onerror?.(error);
			}
		};
	}

	// Auto-start watching when streamUrl is set
	$effect(() => {
		if (streamUrl && !watching) {
			startWatch();
		}
		return () => {
			eventSource?.close();
		};
	});
</script>

<div class="install-progress">
	<div class="progress-header">
		<span class="label">{label}</span>
		<span class="step">{currentStep}</span>
	</div>

	<ProgressBar value={progress} {color} size="md" showLabel />

	{#if error}
		<div class="error-message">
			<span>Error: {error}</span>
		</div>
	{/if}

	{#if output}
		<div class="output-section">
			<button
				class="output-toggle"
				onclick={() => (outputExpanded = !outputExpanded)}
				type="button"
			>
				{outputExpanded ? 'Hide' : 'Show'} output
			</button>
			{#if outputExpanded}
				<pre class="output-log">{output}</pre>
			{/if}
		</div>
	{/if}
</div>

<style>
	.install-progress {
		display: flex;
		flex-direction: column;
		gap: 0.75rem;
	}

	.progress-header {
		display: flex;
		justify-content: space-between;
		align-items: center;
	}

	.label {
		font-weight: 600;
		font-size: 0.9rem;
		color: var(--text-primary, #f9fafb);
	}

	.step {
		font-size: 0.8rem;
		color: var(--text-secondary, #9ca3af);
	}

	.error-message {
		padding: 0.5rem 0.75rem;
		background: rgba(239, 68, 68, 0.1);
		border: 1px solid rgba(239, 68, 68, 0.3);
		border-radius: 0.375rem;
		color: #ef4444;
		font-size: 0.85rem;
	}

	.output-section {
		display: flex;
		flex-direction: column;
		gap: 0.5rem;
	}

	.output-toggle {
		align-self: flex-start;
		background: none;
		border: none;
		color: var(--text-secondary, #9ca3af);
		cursor: pointer;
		font-size: 0.8rem;
		padding: 0;
		text-decoration: underline;
	}

	.output-toggle:hover {
		color: var(--text-primary, #f9fafb);
	}

	.output-log {
		max-height: 300px;
		overflow-y: auto;
		padding: 0.75rem;
		background: #0f172a;
		border-radius: 0.375rem;
		font-size: 0.75rem;
		line-height: 1.5;
		white-space: pre-wrap;
		word-break: break-all;
		margin: 0;
		color: #94a3b8;
	}
</style>
