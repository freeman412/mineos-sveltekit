<script lang="ts">
	import LogsPanel from '$lib/components/server/LogsPanel.svelte';
	let { data } = $props();
</script>

<LogsPanel {data} />
