<script lang="ts">
	import CronPanel from '$lib/components/server/CronPanel.svelte';
	let { data } = $props();
</script>

<CronPanel {data} />
