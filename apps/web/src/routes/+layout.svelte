<script lang="ts">
	import { onMount } from 'svelte';
	import favicon from '$lib/assets/favicon.svg';
	import Modal from '$lib/components/Modal.svelte';
	import { installDemoRuntime } from '$lib/demo/runtime';

	let { children } = $props();

	onMount(() => {
		installDemoRuntime();
	});
</script>

<svelte:head>
	<link rel="icon" href={favicon} />
</svelte:head>

{@render children()}
<Modal />
