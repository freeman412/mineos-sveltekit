<script lang="ts">
	import * as api from '$lib/api/client';
	import type { FabricGameVersion, FabricLoaderVersion } from '$lib/api/types';
	import TwoColumnVersionPicker from '$lib/components/TwoColumnVersionPicker.svelte';

	interface Props {
		onselect: (mcVersion: string, loaderVersion: string) => void;
	}

	let { onselect }: Props = $props();

	let gameVersions = $state<FabricGameVersion[]>([]);
	let loaderVersions = $state<FabricLoaderVersion[]>([]);
	let loading = $state(true);
	let error = $state('');
	let selectedMcIndex = $state<number | null>(null);
	let selectedLoaderIndex = $state<number | null>(null);

	const stableGameVersions = $derived(gameVersions.filter((v) => v.isStable));
	const stableLoaderVersions = $derived(loaderVersions.filter((v) => v.isStable));

	async function load() {
		loading = true;
		error = '';
		const [gameResult, loaderResult] = await Promise.all([
			api.getFabricGameVersions(fetch),
			api.getFabricLoaderVersions(fetch)
		]);
		if (gameResult.error) error = gameResult.error;
		else if (gameResult.data) gameVersions = gameResult.data;
		if (loaderResult.error) error = loaderResult.error;
		else if (loaderResult.data) loaderVersions = loaderResult.data;
		loading = false;
	}

	load();
</script>

<TwoColumnVersionPicker
	leftItems={stableGameVersions}
	rightItems={stableLoaderVersions}
	leftLabel="Minecraft Version"
	rightLabel="Loader Version"
	leftDisplay={(v) => v.version}
	rightDisplay={(v) => v.version}
	rightBadge={(_v) => null}
	selectedLeftIndex={selectedMcIndex}
	selectedRightIndex={selectedLoaderIndex}
	onselectleft={(i, item) => {
		selectedMcIndex = i;
		// Auto-select first stable loader
		if (stableLoaderVersions.length > 0) {
			selectedLoaderIndex = 0;
			onselect(item.version, stableLoaderVersions[0].version);
		}
	}}
	onselectright={(i, item) => {
		selectedLoaderIndex = i;
		if (selectedMcIndex !== null) {
			onselect(stableGameVersions[selectedMcIndex].version, item.version);
		}
	}}
	{loading}
	{error}
/>
