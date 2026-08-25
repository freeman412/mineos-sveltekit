<script lang="ts">
	import BackupsPanel from '$lib/components/server/BackupsPanel.svelte';
	let { data } = $props();
</script>

<BackupsPanel {data} />
