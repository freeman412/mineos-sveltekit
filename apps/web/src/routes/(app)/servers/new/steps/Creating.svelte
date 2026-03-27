<script lang="ts">
	import InstallProgress from '$lib/components/InstallProgress.svelte';
	import ProgressBar from '$lib/components/ProgressBar.svelte';

	interface Props {
		/** Implementation being installed */
		implementation: string;
		/** Server name being created */
		serverName: string;
		/** SSE stream URL (for modloader installs) */
		streamUrl?: string;
		/** Simple progress value (for profile downloads) */
		progress?: number;
		/** Simple step text (for profile downloads) */
		stepText?: string;
		/** Whether creation is complete */
		completed: boolean;
		/** Error if creation failed */
		error?: string;
		/** Navigate to the created server */
		onviewserver: () => void;
	}

	let {
		implementation,
		serverName,
		streamUrl,
		progress,
		stepText,
		completed,
		error,
		onviewserver
	}: Props = $props();

	let streamCompleted = $state(false);
	let streamError = $state('');
	const isCompleted = $derived(completed || streamCompleted);
	const displayError = $derived(error || streamError || '');

	const label = $derived(`Installing ${implementation} server "${serverName}"`);
</script>

<div class="step">
	<h2>Creating server...</h2>

	{#if streamUrl}
		<InstallProgress
			{streamUrl}
			{label}
			oncomplete={() => streamCompleted = true}
			onerror={(e) => streamError = e}
		/>
	{:else}
		<div class="simple-progress">
			<p class="step-text">{stepText || 'Creating server...'}</p>
			<ProgressBar value={progress ?? 0} color="green" size="md" showLabel />
		</div>
	{/if}

	{#if displayError}
		<div class="error">{displayError}</div>
	{/if}

	{#if isCompleted}
		<div class="completed">
			<p>Server created successfully!</p>
			<button class="view-btn" onclick={onviewserver} type="button">View Server</button>
		</div>
	{/if}
</div>

<style>
	.step {
		display: flex;
		flex-direction: column;
		gap: 1.25rem;
	}

	h2 {
		margin: 0;
		font-size: 1.25rem;
		font-weight: 600;
	}

	.step-text {
		margin: 0 0 0.5rem;
		font-size: 0.85rem;
		color: var(--text-secondary, #9ca3af);
	}

	.error {
		padding: 0.75rem;
		background: rgba(239, 68, 68, 0.1);
		border: 1px solid rgba(239, 68, 68, 0.3);
		border-radius: 0.375rem;
		color: #ef4444;
	}

	.completed {
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 1rem;
		padding: 1.5rem;
		background: rgba(34, 197, 94, 0.1);
		border: 1px solid rgba(34, 197, 94, 0.3);
		border-radius: 0.5rem;
	}

	.completed p {
		margin: 0;
		font-weight: 600;
		color: #22c55e;
	}

	.view-btn {
		padding: 0.5rem 1.25rem;
		border: none;
		border-radius: 0.375rem;
		background: #22c55e;
		color: #000;
		font-weight: 600;
		cursor: pointer;
	}

	.view-btn:hover {
		background: #16a34a;
	}
</style>
