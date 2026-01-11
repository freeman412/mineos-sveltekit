<script lang="ts">
	type Props = {
		title: string;
		unit?: string;
		color?: string;
		points: number[];
		maxValue?: number;
		minValue?: number;
	};

	let {
		title,
		unit = '',
		color = '#7ae68d',
		points,
		maxValue,
		minValue
	}: Props = $props();

	const normalized = $derived(() => {
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
