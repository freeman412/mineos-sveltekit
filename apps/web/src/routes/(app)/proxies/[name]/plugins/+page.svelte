<script lang="ts">
	import PluginsPanel from '$lib/components/server/PluginsPanel.svelte';
	let { data } = $props();
</script>

<PluginsPanel {data} />
