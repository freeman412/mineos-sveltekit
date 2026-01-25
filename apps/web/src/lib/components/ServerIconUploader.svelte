<script lang="ts">
	import { browser } from '$app/environment';
	import { modal } from '$lib/stores/modal';

	let { serverName }: { serverName: string } = $props();

	let fileInput: HTMLInputElement;
	let uploading = $state(false);
	let iconUrl = $state('');
	let hasIcon = $state(false);
	let previewUrl = $state<string | null>(null);
	let lastServerName = '';

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

		// Validate file type
		if (!file.type.startsWith('image/')) {
			await modal.error('Please select an image file');
			return;
		}

		// Validate file size (max 1MB)
		if (file.size > 1024 * 1024) {
			await modal.error('Image file must be smaller than 1MB');
			return;
		}

		// Show preview
		const reader = new FileReader();
		reader.onload = (e) => {
			previewUrl = e.target?.result as string;
		};
		reader.readAsDataURL(file);

		// Upload the file
		uploading = true;
		try {
			const arrayBuffer = await file.arrayBuffer();
			const res = await fetch(`/api/servers/${encodeURIComponent(serverName)}/icon`, {
				method: 'POST',
				headers: {
					'Content-Type': file.type || 'image/png'
				},
				body: arrayBuffer
			});

			if (res.ok) {
				// Refresh the icon with a cache-busting timestamp
				iconUrl = `${baseIconUrl}?t=${Date.now()}`;
				hasIcon = true;
				previewUrl = null;
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
			// Reset file input
			target.value = '';
		}
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
				previewUrl = null;
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
		{#if previewUrl}
			<img src={previewUrl} alt="Preview" class="preview-image" />
			<div class="preview-overlay">Preview</div>
		{:else if hasIcon}
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
	</div>

	<div class="icon-actions">
		<button class="btn btn-upload" onclick={triggerUpload} disabled={uploading}>
			{uploading ? 'Uploading...' : 'Upload Icon'}
		</button>
		{#if hasIcon}
			<button class="btn btn-delete" onclick={handleDeleteIcon} disabled={uploading}>
				Delete
			</button>
		{/if}
	</div>

	<p class="help-text">Recommended: 64x64 PNG image</p>

	<input
		type="file"
		bind:this={fileInput}
		onchange={handleFileSelect}
		accept="image/*"
		style="display: none;"
	/>
</div>

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

	.preview-image {
		opacity: 0.7;
	}

	.preview-overlay {
		position: absolute;
		top: 0;
		left: 0;
		right: 0;
		bottom: 0;
		background: rgba(0, 0, 0, 0.5);
		display: flex;
		align-items: center;
		justify-content: center;
		color: white;
		font-size: 12px;
		font-weight: 600;
		text-transform: uppercase;
		letter-spacing: 0.05em;
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

	.icon-actions {
		display: flex;
		gap: 8px;
	}

	.btn {
		background: var(--mc-panel-light, #2a2f47);
		color: var(--mc-text, #eef0f8);
		border: 1px solid var(--border-color, #2a2f47);
		padding: 8px 16px;
		border-radius: 8px;
		cursor: pointer;
		font-size: 13px;
		font-weight: 600;
		transition: all 0.2s;
		font-family: inherit;
	}

	.btn:hover:not(:disabled) {
		background: var(--mc-panel-lighter, #3a3f5a);
		border-color: var(--mc-panel-lighter, #3a3f5a);
		transform: translateY(-1px);
	}

	.btn:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.btn-upload {
		background: var(--mc-grass, #6ab04c);
		border-color: var(--mc-grass, #6ab04c);
		color: white;
	}

	.btn-upload:hover:not(:disabled) {
		background: var(--mc-grass-dark, #4a8b34);
		border-color: var(--mc-grass-dark, #4a8b34);
	}

	.btn-delete {
		background: rgba(210, 94, 72, 0.2);
		color: #ffb6a6;
		border-color: rgba(210, 94, 72, 0.4);
	}

	.btn-delete:hover:not(:disabled) {
		background: rgba(210, 94, 72, 0.3);
	}

	.help-text {
		margin: 0;
		font-size: 11px;
		color: var(--mc-text-dim, #7c87b2);
	}
</style>
