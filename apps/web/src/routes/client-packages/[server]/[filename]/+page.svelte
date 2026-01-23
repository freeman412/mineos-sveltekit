<script lang="ts">
	import { onMount } from 'svelte';

	let { params }: { params: { server: string; filename: string } } = $props();

	const downloadPath = `/api/servers/${encodeURIComponent(
		params.server
	)}/client-packages/${encodeURIComponent(params.filename)}/download?raw=1`;

	onMount(() => {
		const timer = setTimeout(() => {
			window.location.href = downloadPath;
		}, 800);
		return () => clearTimeout(timer);
	});
</script>

<div class="page">
	<div class="card">
		<h1>Install the Minecraft Pack</h1>
		<p class="lead">Your download should start automatically.</p>

		<div class="steps">
			<h2>Steps (CurseForge App)</h2>
			<ol>
				<li>Open the CurseForge app.</li>
				<li>Click <strong>Create Custom Profile</strong>.</li>
				<li>Choose <strong>Import</strong> and pick the downloaded zip.</li>
				<li>Launch the profile and join the server.</li>
			</ol>
		</div>

		<div class="actions">
			<a class="btn-primary" href={downloadPath}>Download Again</a>
		</div>

		<p class="hint">
			If you don’t see the download, check your browser downloads or click “Download Again.”
		</p>
	</div>
</div>

<style>
	.page {
		min-height: 100vh;
		display: flex;
		align-items: center;
		justify-content: center;
		background: radial-gradient(circle at top, #1f2438, #0e111a 55%, #090b12);
		padding: 32px;
	}

	.card {
		max-width: 520px;
		width: 100%;
		background: #161b2b;
		border: 1px solid #2a2f47;
		border-radius: 16px;
		padding: 28px;
		box-shadow: 0 20px 50px rgba(0, 0, 0, 0.45);
		color: #eef0f8;
	}

	h1 {
		margin: 0 0 10px;
		font-size: 26px;
	}

	.lead {
		margin: 0 0 20px;
		color: #b0b7d1;
	}

	.steps {
		margin: 16px 0 20px;
		background: #101422;
		border-radius: 12px;
		padding: 16px 18px;
	}

	.steps h2 {
		margin: 0 0 10px;
		font-size: 16px;
		color: #d2d7ef;
	}

	ol {
		margin: 0;
		padding-left: 18px;
		color: #c3c9e4;
	}

	ol li {
		margin-bottom: 8px;
	}

	.actions {
		display: flex;
		gap: 12px;
		margin-bottom: 12px;
	}

	.btn-primary {
		background: #5a6bff;
		color: white;
		text-decoration: none;
		padding: 10px 18px;
		border-radius: 8px;
		font-weight: 600;
	}

	.hint {
		margin: 0;
		color: #8c95b8;
		font-size: 13px;
	}
</style>
