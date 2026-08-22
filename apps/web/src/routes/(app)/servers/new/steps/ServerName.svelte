<script lang="ts">
	interface Props {
		value: string;
		error: string;
		isProxy?: boolean;
		proxies?: string[];
		attachProxy?: string;
		onattachchange?: (proxyName: string) => void;
		onchange: (name: string) => void;
		oncreate: () => void;
		onback: () => void;
	}

	let { value, error, isProxy = false, proxies = [], attachProxy = '', onattachchange, onchange, oncreate, onback }: Props = $props();

	const namePattern = /^[a-zA-Z0-9][a-zA-Z0-9 _\-\.]{0,63}$/;
	const isValid = $derived(namePattern.test(value.trim()) && !value.includes('..'));
</script>

<div class="step">
	<div class="header">
		<h2>{isProxy ? 'Name your proxy' : 'Name your server'}</h2>
	</div>

	<div class="name-input-group">
		<input
			type="text"
			placeholder="my-server"
			value={value}
			oninput={(e) => onchange(e.currentTarget.value)}
			class="name-input"
			class:invalid={value.trim() && !isValid}
		/>
		{#if value.trim() && !isValid}
			<p class="validation-error">
				Name must start with a letter or number, and contain only letters, numbers, spaces,
				hyphens, underscores, or dots. Max 64 characters.
			</p>
		{/if}
		{#if error}
			<p class="validation-error">{error}</p>
		{/if}
	</div>

	{#if proxies.length > 0}
		<div class="attach-group">
			<label class="attach-label" for="attach-proxy">Behind a proxy?</label>
			<select
				id="attach-proxy"
				class="attach-select"
				value={attachProxy}
				onchange={(e) => onattachchange?.(e.currentTarget.value)}
			>
				<option value="">No — standalone server</option>
				{#each proxies as proxyName (proxyName)}
					<option value={proxyName}>Yes — join {proxyName}</option>
				{/each}
			</select>
			<p class="attach-hint">
				Attaching registers this server with the proxy and sets up verified forwarding, so
				players reach it through the network instead of its own port.
			</p>
		</div>
	{/if}

</div>

<style>
	.step {
		display: flex;
		flex-direction: column;
		gap: 1.25rem;
	}

	.header {
		display: flex;
		align-items: center;
		gap: 1rem;
	}

	h2 {
		margin: 0;
		font-size: 1.25rem;
		font-weight: 600;
	}

	.back-btn {
		background: none;
		border: 1px solid var(--border-color, #374151);
		color: var(--text-secondary, #9ca3af);
		padding: 0.35rem 0.75rem;
		border-radius: 0.375rem;
		cursor: pointer;
		font-size: 0.85rem;
	}

	.back-btn:hover {
		color: var(--text-primary, #f9fafb);
		border-color: var(--text-secondary, #9ca3af);
	}

	.name-input-group {
		display: flex;
		flex-direction: column;
		gap: 0.5rem;
	}

	.name-input {
		padding: 0.6rem 0.75rem;
		border: 2px solid var(--border-color, #374151);
		border-radius: 0.5rem;
		background: var(--input-bg, #1f2937);
		color: inherit;
		font-size: 1rem;
	}

	.name-input:focus {
		outline: none;
		border-color: #3b82f6;
	}

	.name-input.invalid {
		border-color: #ef4444;
	}

	.validation-error {
		margin: 0;
		font-size: 0.8rem;
		color: #ef4444;
	}

	.attach-group {
		display: flex;
		flex-direction: column;
		gap: 0.5rem;
		padding-top: 0.25rem;
		border-top: 1px solid var(--border-color, #374151);
	}

	.attach-label {
		font-size: 0.9rem;
		font-weight: 600;
	}

	.attach-select {
		padding: 0.55rem 0.75rem;
		border: 2px solid var(--border-color, #374151);
		border-radius: 0.5rem;
		background: var(--input-bg, #1f2937);
		color: inherit;
		font-size: 0.95rem;
	}

	.attach-hint {
		margin: 0;
		font-size: 0.78rem;
		color: var(--text-secondary, #9ca3af);
	}
</style>
