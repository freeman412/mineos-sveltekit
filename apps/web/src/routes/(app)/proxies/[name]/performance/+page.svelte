<script lang="ts">
	import PerformancePanel from '$lib/components/server/PerformancePanel.svelte';
	let { data } = $props();
</script>

<PerformancePanel {data} />
