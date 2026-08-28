<script lang="ts">
	import FilesPanel from '$lib/components/server/FilesPanel.svelte';
	let { data } = $props();
</script>

<FilesPanel {data} />
