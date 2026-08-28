<script lang="ts">
	import ServerShell from '$lib/components/server/ServerShell.svelte';
	let { data, children } = $props();
</script>

<ServerShell {data}>{@render children()}</ServerShell>
