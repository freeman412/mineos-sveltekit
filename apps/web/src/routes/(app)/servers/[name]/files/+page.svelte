<script lang="ts">
	import type { PageData } from './$types';
	import type { LayoutData } from '../$layout';
	import { modal } from '$lib/stores/modal';

	let { data }: { data: PageData & { server: LayoutData['server'] } } = $props();

	let currentPath = $state('/');
	let files = $state<any[]>([]);
	let loading = $state(false);
	let selectedFile = $state<string | null>(null);
	let fileContent = $state('');
	let editMode = $state(false);
	let uploadError = $state('');

	$effect(() => {
		loadFiles();
	});

	function buildBrowseUrl(path: string) {
		if (!data.server) return '';
		if (path === '/' || path === '') {
			return `/api/servers/${data.server.name}/files`;
		}
		return `/api/servers/${data.server.name}/files${path}`;
	}

	async function loadFiles() {
		if (!data.server) return;
		loading = true;
		try {
			const res = await fetch(buildBrowseUrl(currentPath));
			if (res.ok) {
				const result = await res.json();
				files = result.entries ?? [];
			} else {
				const error = await res.json().catch(() => ({ error: 'Failed to load files' }));
				await modal.error(error.error || 'Failed to load files');
			}
		} finally {
			loading = false;
		}
	}

	async function navigateTo(name: string, isDirectory: boolean) {
		if (!isDirectory) {
			await viewFile(name);
			return;
		}

		if (currentPath === '/') {
			currentPath = `/${name}`;
		} else {
			currentPath = `${currentPath}/${name}`;
		}
		selectedFile = null;
		editMode = false;
		await loadFiles();
	}

	async function navigateUp() {
		if (currentPath === '/') return;

		const parts = currentPath.split('/').filter(p => p);
		parts.pop();
		currentPath = parts.length === 0 ? '/' : '/' + parts.join('/');
		selectedFile = null;
		editMode = false;
		await loadFiles();
	}

	async function viewFile(name: string) {
		if (!data.server) return;
		loading = true;
		try {
			const filePath = currentPath === '/' ? `/${name}` : `${currentPath}/${name}`;
			const res = await fetch(buildBrowseUrl(filePath));
			if (res.ok) {
				const result = await res.json();
				if (result.kind === 'file' && result.file) {
					fileContent = result.file.content ?? '';
					selectedFile = name;
					editMode = false;
				} else {
					await modal.error('Selected path is not a file');
				}
			} else {
				const error = await res.json().catch(() => ({ error: 'Failed to read file' }));
				await modal.error(error.error || 'Failed to read file');
			}
		} finally {
			loading = false;
		}
	}

	async function saveFile() {
		if (!data.server || !selectedFile) return;
		loading = true;
		try {
			const filePath = currentPath === '/' ? `/${selectedFile}` : `${currentPath}/${selectedFile}`;
			const res = await fetch(buildBrowseUrl(filePath), {
				method: 'PUT',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify({ content: fileContent })
			});
			if (res.ok) {
				await modal.success('File saved successfully');
				editMode = false;
			} else {
				const error = await res.json().catch(() => ({ error: 'Failed to save file' }));
				await modal.error(error.error || 'Failed to save file');
			}
		} finally {
			loading = false;
		}
	}

	async function deleteFile(name: string) {
		if (!data.server) return;
		const confirmed = await modal.confirm(`Are you sure you want to delete "${name}"?`, 'Delete File');
		if (!confirmed) return;

		loading = true;
		try {
			const filePath = currentPath === '/' ? `/${name}` : `${currentPath}/${name}`;
			const res = await fetch(buildBrowseUrl(filePath), {
				method: 'DELETE'
			});
			if (res.ok) {
				await loadFiles();
				if (selectedFile === name) {
					selectedFile = null;
					fileContent = '';
					editMode = false;
				}
			} else {
				const error = await res.json().catch(() => ({ error: 'Failed to delete' }));
				await modal.error(error.error || 'Failed to delete');
			}
		} finally {
			loading = false;
		}
	}

	async function uploadFile(event: Event) {
		if (!data.server) return;
		const input = event.target as HTMLInputElement;
		const file = input.files?.[0];
		if (!file) return;

		uploadError = '';
		loading = true;
		try {
			const filePath = currentPath === '/' ? `/${file.name}` : `${currentPath}/${file.name}`;
			const buffer = await file.arrayBuffer();
			const res = await fetch(buildBrowseUrl(filePath), {
				method: 'POST',
				headers: {
					'Content-Type': file.type || 'application/octet-stream'
				},
				body: buffer
			});

			if (res.ok) {
				await loadFiles();
				input.value = '';
			} else {
				const error = await res.json().catch(() => ({ error: 'Failed to upload file' }));
				uploadError = error.error || 'Failed to upload file';
			}
		} finally {
			loading = false;
		}
	}

	function formatSize(bytes: number): string {
		if (bytes === 0) return '0 B';
		const k = 1024;
		const sizes = ['B', 'KB', 'MB', 'GB'];
		const i = Math.floor(Math.log(bytes) / Math.log(k));
		return `${(bytes / Math.pow(k, i)).toFixed(2)} ${sizes[i]}`;
	}

	function formatDate(date: string): string {
		return new Date(date).toLocaleString();
	}
</script>

<div class="file-browser">
	<div class="toolbar">
		<div class="breadcrumb">
			<button onclick={navigateUp} disabled={currentPath === '/'} class="btn-icon">
				↑ Up
			</button>
			<span class="path">{currentPath}</span>
		</div>
		<div class="toolbar-actions">
			<label class="btn upload-btn">
				<input type="file" onchange={uploadFile} hidden />
				Upload
			</label>
			<button onclick={loadFiles} disabled={loading} class="btn">
				{loading ? 'Loading...' : 'Refresh'}
			</button>
		</div>
	</div>

	<div class="content">
		<div class="file-list">
			<h3>Files & Directories</h3>
			{#if uploadError}
				<p class="error">{uploadError}</p>
			{/if}
			{#if files.length === 0}
				<p class="empty">No files or directories</p>
			{:else}
				<table>
					<thead>
						<tr>
							<th>Name</th>
							<th>Size</th>
							<th>Modified</th>
							<th>Actions</th>
						</tr>
					</thead>
					<tbody>
						{#each files as file}
							<tr class:selected={selectedFile === file.name}>
								<td>
									<button
										class="file-name"
										onclick={() => navigateTo(file.name, file.isDirectory)}
									>
										{file.isDirectory ? '📁' : '📄'} {file.name}
									</button>
								</td>
								<td>{file.isDirectory ? '-' : formatSize(file.size)}</td>
								<td>{formatDate(file.modified)}</td>
								<td>
									{#if !file.isDirectory}
										<button onclick={() => viewFile(file.name)} class="btn-small">View</button>
									{/if}
									<button onclick={() => deleteFile(file.name)} class="btn-small btn-danger">
										Delete
									</button>
								</td>
							</tr>
						{/each}
					</tbody>
				</table>
			{/if}
		</div>

		{#if selectedFile}
			<div class="file-viewer">
				<div class="viewer-toolbar">
					<h3>{selectedFile}</h3>
					<div>
						{#if editMode}
							<button onclick={() => (editMode = false)} class="btn-small">Cancel</button>
							<button onclick={saveFile} class="btn-small btn-primary" disabled={loading}>
								Save
							</button>
						{:else}
							<button onclick={() => (editMode = true)} class="btn-small btn-primary">
								Edit
							</button>
						{/if}
					</div>
				</div>
				<textarea
					bind:value={fileContent}
					readonly={!editMode}
					class="file-content"
					class:editable={editMode}
				></textarea>
			</div>
		{/if}
	</div>
</div>

<style>
	.file-browser {
		display: flex;
		flex-direction: column;
		gap: 1rem;
		height: 100%;
	}

	.toolbar {
		display: flex;
		justify-content: space-between;
		align-items: center;
		padding: 1rem;
		background: #1a1a1a;
		border-radius: 8px;
	}

	.toolbar-actions {
		display: flex;
		gap: 0.5rem;
		align-items: center;
	}

	.breadcrumb {
		display: flex;
		align-items: center;
		gap: 0.5rem;
	}

	.path {
		font-family: 'Courier New', monospace;
		color: #888;
	}

	.content {
		display: grid;
		grid-template-columns: 1fr 1fr;
		gap: 1rem;
		flex: 1;
		overflow: hidden;
	}

	.file-list {
		background: #1a1a1a;
		border-radius: 8px;
		padding: 1rem;
		overflow: auto;
		scrollbar-width: thin;
		scrollbar-color: rgba(106, 176, 76, 0.35) transparent;
	}

	.file-viewer {
		background: #1a1a1a;
		border-radius: 8px;
		padding: 1rem;
		overflow: hidden;
		display: flex;
		flex-direction: column;
	}

	.file-list::-webkit-scrollbar {
		width: 8px;
		height: 8px;
	}

	.file-list::-webkit-scrollbar-track {
		background: transparent;
	}

	.file-list::-webkit-scrollbar-thumb {
		background: rgba(106, 176, 76, 0.35);
		border-radius: 999px;
	}

	.file-list::-webkit-scrollbar-thumb:hover {
		background: rgba(106, 176, 76, 0.6);
	}

	.file-list h3,
	.file-viewer h3 {
		margin-top: 0;
		color: #fff;
	}

	table {
		width: 100%;
		border-collapse: collapse;
	}

	thead {
		background: #2a2a2a;
		position: sticky;
		top: 0;
	}

	th,
	td {
		padding: 0.5rem;
		text-align: left;
		border-bottom: 1px solid #333;
	}

	th {
		font-weight: 600;
		color: #888;
	}

	tr:hover {
		background: #2a2a2a;
	}

	tr.selected {
		background: #2d3748;
	}

	.file-name {
		background: none;
		border: none;
		color: #4299e1;
		cursor: pointer;
		padding: 0;
		font-size: 1rem;
		text-align: left;
	}

	.file-name:hover {
		text-decoration: underline;
	}

	.empty {
		color: #666;
		text-align: center;
		padding: 2rem;
	}

	.error {
		color: #ff9f9f;
		font-size: 0.85rem;
		margin-bottom: 0.5rem;
	}

	.viewer-toolbar {
		display: flex;
		justify-content: space-between;
		align-items: center;
		margin-bottom: 1rem;
		flex-shrink: 0;
	}

	.file-content {
		width: 100%;
		flex: 1;
		min-height: 0;
		background: #0d1117;
		color: #c9d1d9;
		border: 1px solid #333;
		border-radius: 4px;
		padding: 1rem;
		font-family: 'Courier New', monospace;
		font-size: 0.9rem;
		resize: none;
		scrollbar-width: thin;
		scrollbar-color: #3a3f5a #1a1e2f;
	}

	.file-content::-webkit-scrollbar {
		width: 8px;
		height: 8px;
	}

	.file-content::-webkit-scrollbar-track {
		background: #1a1e2f;
		border-radius: 4px;
	}

	.file-content::-webkit-scrollbar-thumb {
		background: #3a3f5a;
		border-radius: 4px;
	}

	.file-content::-webkit-scrollbar-thumb:hover {
		background: #4a5070;
	}

	.file-content.editable {
		border-color: #4299e1;
	}

	.btn,
	.btn-small {
		background: #2d3748;
		color: #fff;
		border: none;
		padding: 0.5rem 1rem;
		border-radius: 4px;
		cursor: pointer;
		font-size: 0.9rem;
	}

	.btn-small {
		padding: 0.25rem 0.5rem;
		font-size: 0.8rem;
	}

	.btn:hover,
	.btn-small:hover {
		background: #4a5568;
	}

	.btn:disabled,
	.btn-small:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.btn-icon {
		background: none;
		border: none;
		color: #4299e1;
		cursor: pointer;
		font-size: 1rem;
		padding: 0.25rem 0.5rem;
	}

	.btn-icon:hover:not(:disabled) {
		color: #63b3ed;
	}

	.btn-icon:disabled {
		color: #555;
		cursor: not-allowed;
	}

	.btn-primary {
		background: #4299e1;
	}

	.btn-primary:hover:not(:disabled) {
		background: #3182ce;
	}

	.btn-danger {
		background: #f56565;
	}

	.btn-danger:hover {
		background: #e53e3e;
	}

	.upload-btn {
		display: inline-flex;
		align-items: center;
	}
</style>
