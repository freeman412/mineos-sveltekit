<script lang="ts">
	import * as api from '$lib/api/client';
	import type { ForgeVersion } from '$lib/api/types';
	import TwoColumnVersionPicker from '$lib/components/TwoColumnVersionPicker.svelte';

	interface Props {
		onselect: (mcVersion: string, forgeVersion: ForgeVersion) => void;
		onconfirm?: (fn: (() => void) | null) => void;
	}

	let { onselect, onconfirm }: Props = $props();

	function reportReady() {
		if (!onconfirm) return;
		if (selectedMcIndex !== null && selectedForgeIndex !== null) {
			onconfirm(() => {
				const mc = mcVersions[selectedMcIndex!];
				const forgeList = versions.filter((v) => v.minecraftVersion === mc);
				onselect(mc, forgeList[selectedForgeIndex!]);
			});
		} else {
			onconfirm(null);
		}
	}

	let versions = $state<ForgeVersion[]>([]);
	let loading = $state(true);
	let error = $state('');
	let selectedMcIndex = $state<number | null>(null);
	let selectedForgeIndex = $state<number | null>(null);

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

	const forgeForMc = $derived.by(() => {
		if (selectedMcIndex === null) return [];
		const mc = mcVersions[selectedMcIndex];
		return versions.filter((v) => v.minecraftVersion === mc);
	});

	async function load() {
		loading = true;
		error = '';
		const result = await api.getForgeVersions(fetch);
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
	rightItems={forgeForMc}
	leftLabel="Minecraft Version"
	rightLabel="Forge Build"
	leftDisplay={(v) => v}
	rightDisplay={(v) => v.forgeVersion}
	rightBadge={(v) => (v.isRecommended ? 'Recommended' : v.isLatest ? 'Latest' : null)}
	selectedLeftIndex={selectedMcIndex}
	selectedRightIndex={selectedForgeIndex}
	onselectleft={(i, _item) => {
		selectedMcIndex = i;
		selectedForgeIndex = null;
		// Auto-select recommended but don't advance
		const mc = mcVersions[i];
		const forgeList = versions.filter((v) => v.minecraftVersion === mc);
		const recIdx = forgeList.findIndex((v) => v.isRecommended);
		if (recIdx >= 0) {
			selectedForgeIndex = recIdx;
		} else if (forgeList.length > 0) {
			selectedForgeIndex = 0;
		}
		reportReady();
	}}
	onselectright={(i, _item) => {
		selectedForgeIndex = i;
		reportReady();
	}}
	{loading}
	{error}
/>
