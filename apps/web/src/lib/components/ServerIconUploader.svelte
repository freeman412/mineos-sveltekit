<script lang="ts">
	import { browser } from '$app/environment';
	import { modal } from '$lib/stores/modal';

	let { serverName }: { serverName: string } = $props();

	let fileInput: HTMLInputElement;
	let uploading = $state(false);
	let iconUrl = $state('');
	let hasIcon = $state(false);
	let lastServerName = '';

	// Crop modal state
	let showCropModal = $state(false);
	let cropImage = $state<HTMLImageElement | null>(null);
	let cropCanvas = $state<HTMLCanvasElement | undefined>(undefined);

	// Drag & zoom state
	let scale = $state(1);
	let offsetX = $state(0);
	let offsetY = $state(0);
	let dragging = $state(false);
	let dragStartX = 0;
	let dragStartY = 0;
	let dragOffsetStartX = 0;
	let dragOffsetStartY = 0;

	const CROP_SIZE = 220;
	const OUTPUT_SIZE = 64;

	const baseIconUrl = $derived(`/api/servers/${encodeURIComponent(serverName)}/icon`);

	async function refreshIconPresence(currentServerName: string) {
		if (!browser || !currentServerName) return;
		try {
			const res = await fetch(baseIconUrl, { method: 'HEAD' });
			if (res.ok) {
				hasIcon = true;
				iconUrl = `${baseIconUrl}?t=${Date.now()}`;
			} else {
				hasIcon = false;
				iconUrl = '';
			}
		} catch {
			hasIcon = false;
			iconUrl = '';
		}
	}

	$effect(() => {
		if (!serverName || serverName === lastServerName) return;
		lastServerName = serverName;
		void refreshIconPresence(serverName);
	});

	function handleImageError() {
		hasIcon = false;
		iconUrl = '';
	}

	function handleImageLoad() {
		hasIcon = true;
	}

	function triggerUpload() {
		fileInput.click();
	}

	async function handleFileSelect(event: Event) {
		const target = event.target as HTMLInputElement;
		const file = target.files?.[0];
		if (!file) return;
		target.value = '';

		if (!file.type.startsWith('image/')) {
			await modal.error('Please select an image file');
			return;
		}

		if (file.size > 1024 * 1024) {
			await modal.error('Image file must be smaller than 1MB');
			return;
		}

		// Load image for cropping
		const img = new Image();
		img.onload = () => {
			cropImage = img;
			// Initialize: fit image to crop area
			const fitScale = CROP_SIZE / Math.min(img.width, img.height);
			scale = fitScale;
			offsetX = 0;
			offsetY = 0;
			showCropModal = true;
		};
		img.onerror = () => {
			modal.error('Failed to load image');
		};
		img.src = URL.createObjectURL(file);
	}

	// Redraw preview when crop parameters change
	$effect(() => {
		// Reference reactive values to track them
		scale;
		offsetX;
		offsetY;
		if (showCropModal && cropImage && cropCanvas) {
			drawCropPreview();
		}
	});

	function drawCropPreview() {
		if (!cropCanvas || !cropImage) return;
		const ctx = cropCanvas.getContext('2d');
		if (!ctx) return;

		cropCanvas.width = OUTPUT_SIZE;
		cropCanvas.height = OUTPUT_SIZE;
		ctx.imageSmoothingEnabled = true;
		ctx.imageSmoothingQuality = 'high';

		const imgW = cropImage.width * scale;
		const imgH = cropImage.height * scale;
		// Image position relative to crop area center
		const drawX = (CROP_SIZE / 2 - imgW / 2 + offsetX);
		const drawY = (CROP_SIZE / 2 - imgH / 2 + offsetY);

		// Map crop area to canvas
		const ratio = OUTPUT_SIZE / CROP_SIZE;
		ctx.clearRect(0, 0, OUTPUT_SIZE, OUTPUT_SIZE);
		ctx.drawImage(
			cropImage,
			drawX * ratio,
			drawY * ratio,
			imgW * ratio,
			imgH * ratio
		);
	}

	function handlePointerDown(e: PointerEvent) {
		dragging = true;
		dragStartX = e.clientX;
		dragStartY = e.clientY;
		dragOffsetStartX = offsetX;
		dragOffsetStartY = offsetY;
		(e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
	}

	function handlePointerMove(e: PointerEvent) {
		if (!dragging) return;
		offsetX = dragOffsetStartX + (e.clientX - dragStartX);
		offsetY = dragOffsetStartY + (e.clientY - dragStartY);
	}

	function handlePointerUp() {
		dragging = false;
	}

	function handleWheel(e: WheelEvent) {
		e.preventDefault();
		const delta = e.deltaY > 0 ? -0.05 : 0.05;
		const newScale = Math.max(0.1, Math.min(5, scale + delta));
		scale = newScale;
	}

	function cancelCrop() {
		showCropModal = false;
		if (cropImage) {
			URL.revokeObjectURL(cropImage.src);
		}
		cropImage = null;
	}

	async function applyCrop() {
		if (!cropCanvas || !cropImage) return;

		// Draw final cropped image to an offscreen canvas at 64x64
		const outputCanvas = document.createElement('canvas');
		outputCanvas.width = OUTPUT_SIZE;
		outputCanvas.height = OUTPUT_SIZE;
		const ctx = outputCanvas.getContext('2d');
		if (!ctx) return;

		ctx.imageSmoothingEnabled = true;
		ctx.imageSmoothingQuality = 'high';

		const imgW = cropImage.width * scale;
		const imgH = cropImage.height * scale;
		const drawX = (CROP_SIZE / 2 - imgW / 2 + offsetX);
		const drawY = (CROP_SIZE / 2 - imgH / 2 + offsetY);

		const ratio = OUTPUT_SIZE / CROP_SIZE;
		ctx.drawImage(
			cropImage,
			drawX * ratio,
			drawY * ratio,
			imgW * ratio,
			imgH * ratio
		);

		// Convert to blob and upload
		outputCanvas.toBlob(async (blob) => {
			if (!blob) {
				await modal.error('Failed to process image');
				return;
			}

			uploading = true;
			showCropModal = false;

			try {
				const arrayBuffer = await blob.arrayBuffer();
				const res = await fetch(`/api/servers/${encodeURIComponent(serverName)}/icon`, {
					method: 'POST',
					headers: { 'Content-Type': 'image/png' },
					body: arrayBuffer
				});

				if (res.ok) {
					iconUrl = `${baseIconUrl}?t=${Date.now()}`;
					hasIcon = true;
					await modal.success('Server icon uploaded successfully!');
				} else {
					const error = await res.json();
					await modal.error(error.error || 'Failed to upload server icon');
				}
			} catch (err) {
				console.error('Upload error:', err);
				await modal.error('Failed to upload server icon');
			} finally {
				uploading = false;
				if (cropImage) {
					URL.revokeObjectURL(cropImage.src);
				}
				cropImage = null;
			}
		}, 'image/png');
	}

	async function handleDeleteIcon() {
		uploading = true;
		try {
			const res = await fetch(`/api/servers/${encodeURIComponent(serverName)}/icon`, {
				method: 'DELETE'
			});

			if (res.ok) {
				hasIcon = false;
				iconUrl = '';
				await modal.success('Server icon deleted successfully!');
			} else {
				const error = await res.json();
				await modal.error(error.error || 'Failed to delete server icon');
			}
		} catch (err) {
			console.error('Delete error:', err);
			await modal.error('Failed to delete server icon');
		} finally {
			uploading = false;
		}
	}
</script>

<div class="icon-uploader">
	<div class="icon-preview">
		{#if hasIcon}
			<img src={iconUrl} alt="Server icon" onload={handleImageLoad} onerror={handleImageError} />
		{:else}
			<div class="placeholder">
				<svg viewBox="0 0 64 64" width="64" height="64" aria-hidden="true">
					<rect width="64" height="64" fill="#141827" />
					<rect x="14" y="18" width="36" height="28" rx="6" fill="#1f2a4a" />
					<path
						d="M32 22v16m0-16l-6 6m6-6l6 6"
						fill="none"
						stroke="#6ab04c"
						stroke-width="3"
						stroke-linecap="round"
						stroke-linejoin="round"
					/>
					<rect x="20" y="40" width="24" height="6" rx="3" fill="#6ab04c" />
				</svg>
				<span class="placeholder-text">Upload icon</span>
			</div>
		{/if}
		<div class="icon-overlay">
			<button
				type="button"
				class="icon-action upload"
				onclick={triggerUpload}
				disabled={uploading}
				title="Recommended: 64x64 PNG image."
				aria-label="Upload server icon"
			>
				<svg viewBox="0 0 24 24" aria-hidden="true">
					<path
						d="M12 16V6m0 0l-4 4m4-4l4 4M5 18h14"
						fill="none"
						stroke="currentColor"
						stroke-width="2"
						stroke-linecap="round"
						stroke-linejoin="round"
					/>
				</svg>
			</button>
			{#if hasIcon}
				<button
					type="button"
					class="icon-action delete"
					onclick={handleDeleteIcon}
					disabled={uploading}
					title="Delete server icon"
					aria-label="Delete server icon"
				>
					<svg viewBox="0 0 24 24" aria-hidden="true">
						<path
							d="M4 7h16m-2 0l-1 12a2 2 0 0 1-2 2H9a2 2 0 0 1-2-2L6 7m4 0V5a1 1 0 0 1 1-1h2a1 1 0 0 1 1 1v2"
							fill="none"
							stroke="currentColor"
							stroke-width="2"
							stroke-linecap="round"
							stroke-linejoin="round"
						/>
					</svg>
				</button>
			{/if}
		</div>
	</div>

	<input
		type="file"
		bind:this={fileInput}
		onchange={handleFileSelect}
		accept="image/*"
		style="display: none;"
	/>
</div>

{#if showCropModal}
	<!-- svelte-ignore a11y_no_static_element_interactions -->
	<div class="crop-backdrop" onclick={cancelCrop} onkeydown={(e) => e.key === 'Escape' && cancelCrop()}>
		<!-- svelte-ignore a11y_no_static_element_interactions -->
		<div class="crop-modal" onclick={(e) => e.stopPropagation()}>
			<h3>Crop Server Icon</h3>
			<p class="crop-hint">Drag to position, scroll to zoom. Icon will be saved as 64x64.</p>

			<div class="crop-workspace">
				<div
					class="crop-area"
					style="width: {CROP_SIZE}px; height: {CROP_SIZE}px;"
					role="application"
					aria-label="Crop area - drag to position image"
					onpointerdown={handlePointerDown}
					onpointermove={handlePointerMove}
					onpointerup={handlePointerUp}
					onpointercancel={handlePointerUp}
					onwheel={handleWheel}
				>
					{#if cropImage}
						<img
							src={cropImage.src}
							alt="Crop source"
							class="crop-source"
							style="
								width: {cropImage.width * scale}px;
								height: {cropImage.height * scale}px;
								transform: translate({CROP_SIZE / 2 - (cropImage.width * scale) / 2 + offsetX}px, {CROP_SIZE / 2 - (cropImage.height * scale) / 2 + offsetY}px);
							"
							draggable="false"
						/>
					{/if}
					<div class="crop-grid">
						<div class="grid-line horizontal"></div>
						<div class="grid-line vertical"></div>
					</div>
				</div>

				<div class="crop-preview-section">
					<span class="preview-label">Preview</span>
					<canvas
						bind:this={cropCanvas}
						width={OUTPUT_SIZE}
						height={OUTPUT_SIZE}
						class="crop-preview-canvas"
					></canvas>
				</div>
			</div>

			<div class="crop-controls">
				<label class="zoom-label">
					<svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true">
						<circle cx="11" cy="11" r="7" fill="none" stroke="currentColor" stroke-width="2" />
						<path d="M21 21l-4.35-4.35" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" />
						<path d="M8 11h6M11 8v6" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" />
					</svg>
					<input
						type="range"
						min="0.1"
						max="5"
						step="0.01"
						bind:value={scale}
						class="zoom-slider"
					/>
				</label>
			</div>

			<div class="crop-actions">
				<button type="button" class="btn-cancel" onclick={cancelCrop}>Cancel</button>
				<button type="button" class="btn-crop" onclick={applyCrop}>Crop & Upload</button>
			</div>
		</div>
	</div>
{/if}

<style>
	.icon-uploader {
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 12px;
	}

	.icon-preview {
		position: relative;
		width: 128px;
		height: 128px;
		border: 2px solid var(--border-color, #2a2f47);
		border-radius: 8px;
		background: var(--mc-panel-darkest, #0d1117);
		display: flex;
		align-items: center;
		justify-content: center;
		overflow: hidden;
	}

	.icon-preview img {
		width: 100%;
		height: 100%;
		object-fit: contain;
		image-rendering: pixelated;
		image-rendering: -moz-crisp-edges;
		image-rendering: crisp-edges;
	}

	.icon-overlay {
		position: absolute;
		inset: 0;
		background: rgba(11, 14, 22, 0.6);
		display: flex;
		align-items: center;
		justify-content: center;
		gap: 10px;
		opacity: 0;
		pointer-events: none;
		transition: opacity 0.2s ease;
	}

	.icon-preview:hover .icon-overlay,
	.icon-preview:focus-within .icon-overlay {
		opacity: 1;
		pointer-events: auto;
	}

	.icon-action {
		width: 40px;
		height: 40px;
		border-radius: 12px;
		border: 1px solid rgba(255, 255, 255, 0.15);
		background: rgba(20, 24, 39, 0.85);
		color: #e8efff;
		display: inline-flex;
		align-items: center;
		justify-content: center;
		cursor: pointer;
		transition: transform 0.2s ease, background 0.2s ease, border-color 0.2s ease;
	}

	.icon-action svg {
		width: 20px;
		height: 20px;
	}

	.icon-action.upload {
		background: rgba(106, 176, 76, 0.2);
		border-color: rgba(106, 176, 76, 0.5);
		color: #c7f7b3;
	}

	.icon-action.delete {
		background: rgba(210, 94, 72, 0.2);
		border-color: rgba(210, 94, 72, 0.5);
		color: #ffb6a6;
	}

	.icon-action:hover:not(:disabled) {
		transform: translateY(-1px);
	}

	.icon-action:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.placeholder {
		width: 100%;
		height: 100%;
		display: flex;
		align-items: center;
		justify-content: center;
		flex-direction: column;
		gap: 8px;
	}

	.placeholder-text {
		font-size: 11px;
		color: var(--mc-text-dim, #7c87b2);
		letter-spacing: 0.03em;
		text-transform: uppercase;
	}

	/* Crop Modal */
	.crop-backdrop {
		position: fixed;
		inset: 0;
		background: rgba(0, 0, 0, 0.7);
		backdrop-filter: blur(4px);
		display: flex;
		align-items: center;
		justify-content: center;
		z-index: 1000;
	}

	.crop-modal {
		background: #1a1e2f;
		border: 1px solid #2a2f47;
		border-radius: 16px;
		padding: 24px;
		max-width: 400px;
		width: 90vw;
		display: flex;
		flex-direction: column;
		gap: 16px;
		box-shadow: 0 20px 60px rgba(0, 0, 0, 0.5);
	}

	.crop-modal h3 {
		margin: 0;
		font-size: 18px;
		font-weight: 600;
		color: #eef0f8;
	}

	.crop-hint {
		margin: 0;
		font-size: 13px;
		color: #8a93ba;
	}

	.crop-workspace {
		display: flex;
		align-items: center;
		gap: 20px;
		justify-content: center;
	}

	.crop-area {
		position: relative;
		overflow: hidden;
		border-radius: 8px;
		border: 2px solid #5865f2;
		background: #0d1117;
		cursor: grab;
		touch-action: none;
		user-select: none;
		flex-shrink: 0;
	}

	.crop-area:active {
		cursor: grabbing;
	}

	.crop-source {
		position: absolute;
		top: 0;
		left: 0;
		pointer-events: none;
		user-select: none;
	}

	.crop-grid {
		position: absolute;
		inset: 0;
		pointer-events: none;
	}

	.grid-line {
		position: absolute;
		background: rgba(255, 255, 255, 0.1);
	}

	.grid-line.horizontal {
		left: 0;
		right: 0;
		top: 50%;
		height: 1px;
	}

	.grid-line.vertical {
		top: 0;
		bottom: 0;
		left: 50%;
		width: 1px;
	}

	.crop-preview-section {
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 8px;
	}

	.preview-label {
		font-size: 11px;
		color: #737aa3;
		text-transform: uppercase;
		letter-spacing: 0.08em;
	}

	.crop-preview-canvas {
		width: 64px;
		height: 64px;
		border-radius: 6px;
		border: 1px solid #2a2f47;
		background: #0d1117;
		image-rendering: pixelated;
		image-rendering: -moz-crisp-edges;
		image-rendering: crisp-edges;
	}

	.crop-controls {
		display: flex;
		align-items: center;
		gap: 12px;
	}

	.zoom-label {
		display: flex;
		align-items: center;
		gap: 8px;
		flex: 1;
		color: #8a93ba;
	}

	.zoom-slider {
		flex: 1;
		height: 4px;
		accent-color: #5865f2;
		cursor: pointer;
	}

	.crop-actions {
		display: flex;
		gap: 12px;
		justify-content: flex-end;
	}

	.btn-cancel {
		background: #2b2f45;
		color: #d4d9f1;
		border: none;
		border-radius: 8px;
		padding: 10px 20px;
		font-family: inherit;
		font-size: 14px;
		font-weight: 500;
		cursor: pointer;
		transition: background 0.2s;
	}

	.btn-cancel:hover {
		background: #3a3f5a;
	}

	.btn-crop {
		background: #5865f2;
		color: white;
		border: none;
		border-radius: 8px;
		padding: 10px 20px;
		font-family: inherit;
		font-size: 14px;
		font-weight: 600;
		cursor: pointer;
		transition: background 0.2s;
	}

	.btn-crop:hover {
		background: #4752c4;
	}

	@media (max-width: 480px) {
		.crop-workspace {
			flex-direction: column;
		}

		.crop-modal {
			padding: 16px;
		}
	}
</style>
