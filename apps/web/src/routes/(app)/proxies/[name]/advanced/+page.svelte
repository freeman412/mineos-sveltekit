<script lang="ts">
	import JavaConfigPanel from '$lib/components/server/JavaConfigPanel.svelte';
	let { data, form } = $props();
</script>

<JavaConfigPanel {data} {form} />
