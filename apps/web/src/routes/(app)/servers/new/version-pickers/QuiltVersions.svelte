<script lang="ts">
	import * as api from '$lib/api/client';
	import type { QuiltGameVersion, QuiltLoaderVersion } from '$lib/api/types';
	import TwoColumnVersionPicker from '$lib/components/TwoColumnVersionPicker.svelte';

	interface Props {
		onselect: (mcVersion: string, loaderVersion: string) => void;
		onconfirm?: (fn: (() => void) | null) => void;
	}

	let { onselect, onconfirm }: Props = $props();

	function reportReady() {
		if (!onconfirm) return;
		if (selectedMcIndex !== null && selectedLoaderIndex !== null) {
			onconfirm(() => {
				onselect(stableGameVersions[selectedMcIndex!].version, stableLoaderVersions[selectedLoaderIndex!].version);
			});
		} else {
			onconfirm(null);
		}
	}

	let gameVersions = $state<QuiltGameVersion[]>([]);
	let loaderVersions = $state<QuiltLoaderVersion[]>([]);
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
			api.getQuiltGameVersions(fetch),
			api.getQuiltLoaderVersions(fetch)
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
	onselectleft={(i, _item) => {
		selectedMcIndex = i;
		if (selectedLoaderIndex === null && stableLoaderVersions.length > 0) {
			selectedLoaderIndex = 0;
		}
		reportReady();
	}}
	onselectright={(i, _item) => {
		selectedLoaderIndex = i;
		reportReady();
	}}
	{loading}
	{error}
/>
