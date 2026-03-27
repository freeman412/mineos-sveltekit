<!-- apps/web/src/lib/components/InstallProgress.svelte -->
<script lang="ts">
	import ProgressBar from './ProgressBar.svelte';

	import { goto } from '$app/navigation';

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

	function markCompleted() {
		if (completed) return; // Only fire once
		eventSource?.close();
		completed = true;
		progress = 100;
		currentStep = 'Complete!';
		oncomplete?.();
	}

	function startWatch() {
		if (watching) return;
		watching = true;
		error = '';

		// Check status endpoint immediately — install might already be done
		// before EventSource connects (fast installs like Fabric)
		const statusUrl = streamUrl.replace('/stream', '');
		fetch(statusUrl).then(async (res) => {
			if (completed) return;
			if (!res.ok || res.status === 404) {
				markCompleted(); // Cleaned up = completed
				return;
			}
			const body = await res.json();
			const data = body.data ?? body;
			if (data.status === 'completed') {
				markCompleted();
				return;
			}
			if (data.status === 'failed') {
				error = data.error || 'Installation failed';
				onerror?.(error);
				eventSource?.close();
				return;
			}
			if (data.progress) progress = data.progress;
			if (data.currentStep) currentStep = data.currentStep;
		}).catch(() => {});

		eventSource = new EventSource(streamUrl);
		eventSource.onmessage = (event) => {
			if (completed) return; // Already done — ignore further messages
			try {
				const data = JSON.parse(event.data);
				if (data.progress != null) progress = data.progress;
				if (data.currentStep) currentStep = data.currentStep;
				if (data.output) output = data.output;

				if (data.status === 'completed') {
					markCompleted();
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
			if (completed) return; // Already handled
			// Stream disconnected — check status one more time
			const statusUrl = streamUrl.replace('/stream', '');
			fetch(statusUrl).then(async (res) => {
				if (completed) return;
				if (!res.ok || res.status === 404) {
					markCompleted(); // Cleaned up = completed
					return;
				}
				const body = await res.json();
				const data = body.data ?? body;
				if (data.status === 'completed') {
					markCompleted();
				} else if (data.status === 'failed') {
					error = data.error || 'Installation failed';
					onerror?.(error);
				} else {
					error = 'Lost connection to install stream';
					onerror?.(error);
				}
			}).catch(() => {
				if (!completed) {
					error = 'Lost connection to install stream';
					onerror?.(error);
				}
			});
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

	<p class="hint">
		This may take several minutes. You can leave this page and track progress in Notifications.
	</p>

	<div class="actions">
		<button class="btn-background" type="button" onclick={() => goto('/servers')}>
			Send to background
		</button>
	</div>

	{#if error}
		<div class="error-message">
			<span>Error: {error}</span>
		</div>
	{/if}

	{#if completed}
		<div class="success-message">
			Installation complete!
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

	.hint {
		margin: 0;
		font-size: 0.8rem;
		color: var(--mc-text-muted, #9ca3af);
		text-align: center;
	}

	.actions {
		display: flex;
		justify-content: center;
		gap: 0.75rem;
	}

	.btn-background {
		padding: 0.5rem 1.25rem;
		background: var(--mc-panel-light, #2a2f47);
		color: var(--mc-text-secondary, #c4cff5);
		border: none;
		border-radius: 0.5rem;
		cursor: pointer;
		font-size: 0.85rem;
		font-family: inherit;
	}

	.btn-background:hover {
		background: var(--mc-panel-lighter, #3a3f5a);
	}

	.success-message {
		padding: 0.75rem;
		background: rgba(34, 197, 94, 0.1);
		border: 1px solid rgba(34, 197, 94, 0.3);
		border-radius: 0.375rem;
		color: #22c55e;
		text-align: center;
		font-weight: 600;
	}
</style>
