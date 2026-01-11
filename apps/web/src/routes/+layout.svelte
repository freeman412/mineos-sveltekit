<script lang="ts">
	import favicon from '$lib/assets/favicon.svg';
	import Modal from '$lib/components/Modal.svelte';

	let { children } = $props();
</script>

<svelte:head>
	<link rel="icon" href={favicon} />
</svelte:head>

{@render children()}
<Modal />
