<script lang="ts">
	import OverviewPanel from '$lib/components/server/OverviewPanel.svelte';
	let { data } = $props();
</script>

<OverviewPanel {data} />
