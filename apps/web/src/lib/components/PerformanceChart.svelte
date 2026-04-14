<script lang="ts">
	type Props = {
		title: string;
		unit?: string;
		color?: string;
		points: number[];
		timestamps?: string[];
		maxValue?: number;
		minValue?: number;
	};

	let {
		title,
		unit = '',
		color = '#7ae68d',
		points,
		timestamps = [],
		maxValue,
		minValue
	}: Props = $props();

	const normalized = $derived.by(() => {
		if (!points || points.length === 0) {
			return { path: '', latest: 0, min: 0, max: 1 };
		}

		const safePoints = points.map((value) => (Number.isFinite(value) ? value : 0));
		const rawMin = minValue ?? Math.min(...safePoints);
		const rawMax = maxValue ?? Math.max(...safePoints);
		const safeMin = Number.isFinite(rawMin) ? rawMin : 0;
		const safeMax = Number.isFinite(rawMax) ? rawMax : safeMin + 1;
		const span = safeMax - safeMin || 1;
		const width = 100;
		const height = 40;
		const step = safePoints.length > 1 ? width / (safePoints.length - 1) : width;

		const coords = safePoints.map((value, index) => {
			const clamped = Number.isFinite(value) ? value : safeMin;
			const x = index * step;
			const y = height - ((clamped - safeMin) / span) * height;
			return `${x.toFixed(2)},${y.toFixed(2)}`;
		});

		const latestValue = safePoints[safePoints.length - 1];

		return {
			path: coords.join(' '),
			latest: Number.isFinite(latestValue) ? latestValue : 0,
			min: safeMin,
			max: safeMax
		};
	});

	const timeLabels = $derived.by(() => {
		if (!timestamps || timestamps.length < 2) return [];
		const count = Math.min(5, timestamps.length);
		const labels: { label: string; position: number }[] = [];
		for (let i = 0; i < count; i++) {
			const idx = Math.round((i / (count - 1)) * (timestamps.length - 1));
			const date = new Date(timestamps[idx]);
			if (isNaN(date.getTime())) continue;
			labels.push({
				label: date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }),
				position: (idx / (timestamps.length - 1)) * 100
			});
		}
		return labels;
	});

	function formatNumber(value: number | null | undefined, fallback = '0.0', decimals = 1) {
		if (value == null || !Number.isFinite(value)) {
			return fallback;
		}
		return value.toFixed(decimals);
	}
</script>

<div class="chart-card">
	<header>
		<div>
			<p class="title">{title}</p>
			<p class="value">
				{formatNumber(normalized.latest)}
				{#if unit}
					<span class="unit">{unit}</span>
				{/if}
			</p>
		</div>
		<div class="range">
			<span>{formatNumber(normalized.min)}</span>
			<span>{formatNumber(normalized.max)}</span>
		</div>
	</header>

	{#if points.length > 1}
		<svg viewBox="0 0 100 40" preserveAspectRatio="none">
			<polyline points={normalized.path} style={`stroke: ${color};`} />
		</svg>
		{#if timeLabels.length > 0}
			<div class="time-axis">
				{#each timeLabels as tick}
					<span class="time-label" style={`left: ${tick.position}%`}>{tick.label}</span>
				{/each}
			</div>
		{/if}
	{:else}
		<div class="placeholder">Collecting data...</div>
	{/if}
</div>

<style>
	.chart-card {
		background: #1a1e2f;
		border: 1px solid #2a2f47;
		border-radius: 16px;
		padding: 16px;
		display: flex;
		flex-direction: column;
		gap: 12px;
		min-height: 160px;
	}

	header {
		display: flex;
		justify-content: space-between;
		align-items: flex-start;
		gap: 12px;
	}

	.title {
		margin: 0;
		font-size: 12px;
		letter-spacing: 0.14em;
		text-transform: uppercase;
		color: #8a93ba;
	}

	.value {
		margin: 4px 0 0;
		font-size: 22px;
		font-weight: 600;
		color: #eef0f8;
	}

	.unit {
		margin-left: 6px;
		font-size: 12px;
		color: #8a93ba;
		font-weight: 500;
	}

	.range {
		display: flex;
		flex-direction: column;
		gap: 4px;
		font-size: 11px;
		color: #737aa3;
		text-align: right;
	}

	svg {
		width: 100%;
		height: 70px;
	}

	polyline {
		fill: none;
		stroke-width: 2.2;
		stroke-linecap: round;
		stroke-linejoin: round;
	}

	.time-axis {
		position: relative;
		height: 16px;
		margin-top: -4px;
	}

	.time-label {
		position: absolute;
		transform: translateX(-50%);
		font-size: 10px;
		color: #737aa3;
		white-space: nowrap;
	}

	.time-label:first-child {
		transform: translateX(0);
	}

	.time-label:last-child {
		transform: translateX(-100%);
	}

	.placeholder {
		flex: 1;
		display: flex;
		align-items: center;
		justify-content: center;
		color: #8a93ba;
		font-size: 13px;
		border: 1px dashed #2a2f47;
		border-radius: 12px;
		padding: 12px;
	}
	</style>
