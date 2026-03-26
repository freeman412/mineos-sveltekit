<script lang="ts">
	import * as api from '$lib/api/client';
	import type { NeoForgeVersion } from '$lib/api/types';
	import TwoColumnVersionPicker from '$lib/components/TwoColumnVersionPicker.svelte';

	interface Props {
		onselect: (mcVersion: string, neoForgeVersion: NeoForgeVersion) => void;
	}

	let { onselect }: Props = $props();

	let versions = $state<NeoForgeVersion[]>([]);
	let loading = $state(true);
	let error = $state('');
	let selectedMcIndex = $state<number | null>(null);
	let selectedNfIndex = $state<number | null>(null);

	const mcVersions = $derived.by(() => {
		const unique = [...new Set(versions.map((v) => v.minecraftVersion))];
		return unique.sort((a, b) => {
			const ap = a.split('.').map(Number);
			const bp = b.split('.').map(Number);
			for (let i = 0; i < Math.max(ap.length, bp.length); i++) {
				if ((bp[i] ?? 0) !== (ap[i] ?? 0)) return (bp[i] ?? 0) - (ap[i] ?? 0);
			}
			return 0;
		});
	});

	const nfForMc = $derived.by(() => {
		if (selectedMcIndex === null) return [];
		const mc = mcVersions[selectedMcIndex];
		return versions.filter((v) => v.minecraftVersion === mc);
	});

	async function load() {
		loading = true;
		error = '';
		const result = await api.getNeoForgeVersions(fetch);
		if (result.error) {
			error = result.error;
		} else if (result.data) {
			versions = result.data;
		}
		loading = false;
	}

	load();
</script>

<TwoColumnVersionPicker
	leftItems={mcVersions}
	rightItems={nfForMc}
	leftLabel="Minecraft Version"
	rightLabel="NeoForge Build"
	leftDisplay={(v) => v}
	rightDisplay={(v) => v.neoForgeVersion}
	rightBadge={(v) => (v.isLatest ? 'Latest' : null)}
	selectedLeftIndex={selectedMcIndex}
	selectedRightIndex={selectedNfIndex}
	onselectleft={(i, _item) => {
		selectedMcIndex = i;
		selectedNfIndex = null;
		// Auto-select latest
		const mc = mcVersions[i];
		const nfList = versions.filter((v) => v.minecraftVersion === mc);
		const latestIdx = nfList.findIndex((v) => v.isLatest);
		if (latestIdx >= 0) {
			selectedNfIndex = latestIdx;
			onselect(mc, nfList[latestIdx]);
		}
	}}
	onselectright={(i, item) => {
		selectedNfIndex = i;
		if (selectedMcIndex !== null) {
			onselect(mcVersions[selectedMcIndex], item);
		}
	}}
	{loading}
	{error}
/>
