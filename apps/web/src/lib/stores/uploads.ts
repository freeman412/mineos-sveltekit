import { writable, get } from 'svelte/store';

export type UploadStatus = 'uploading' | 'completed' | 'failed';

export type UploadEntry = {
	id: string;
	filename: string;
	progress: number;
	status: UploadStatus;
	error?: string;
	abortController?: AbortController;
};

function createUploadStore() {
	const { subscribe, update } = writable<UploadEntry[]>([]);

	return {
		subscribe,

		add(id: string, filename: string, abortController: AbortController) {
			update((uploads) => [
				...uploads,
				{ id, filename, progress: 0, status: 'uploading', abortController }
			]);
		},

		updateProgress(id: string, progress: number) {
			update((uploads) =>
				uploads.map((u) => (u.id === id ? { ...u, progress } : u))
			);
		},

		complete(id: string) {
			update((uploads) =>
				uploads.map((u) =>
					u.id === id ? { ...u, status: 'completed' as UploadStatus, progress: 100 } : u
				)
			);
			// Auto-remove after 3 seconds
			setTimeout(() => {
				update((uploads) => uploads.filter((u) => u.id !== id));
			}, 3000);
		},

		fail(id: string, error: string) {
			update((uploads) =>
				uploads.map((u) =>
					u.id === id ? { ...u, status: 'failed' as UploadStatus, error } : u
				)
			);
		},

		cancel(id: string) {
			update((uploads) => {
				const upload = uploads.find((u) => u.id === id);
				if (upload?.abortController) {
					upload.abortController.abort();
				}
				return uploads.filter((u) => u.id !== id);
			});
		},

		remove(id: string) {
			update((uploads) => uploads.filter((u) => u.id !== id));
		},

		getActive(): UploadEntry[] {
			return get({ subscribe }).filter((u) => u.status === 'uploading');
		}
	};
}

export const uploads = createUploadStore();

// Global upload function that uses the store
export async function uploadFile(file: File): Promise<boolean> {
	const id = crypto.randomUUID();
	const abortController = new AbortController();

	uploads.add(id, file.name, abortController);

	try {
		const res = await fetch('/api/host/imports/upload', {
			method: 'POST',
			headers: {
				'X-File-Name': file.name
			},
			body: file,
			signal: abortController.signal
		});

		if (!res.ok) {
			const error = await res.json().catch(() => ({ error: 'Upload failed' }));
			uploads.fail(id, error.error || 'Upload failed');
			return false;
		}

		uploads.complete(id);
		return true;
	} catch (err) {
		if (err instanceof Error && err.name === 'AbortError') {
			uploads.remove(id);
			return false;
		}
		uploads.fail(id, err instanceof Error ? err.message : 'Upload failed');
		return false;
	}
}

// Upload multiple files
export async function uploadFiles(files: FileList | File[]): Promise<void> {
	const fileArray = Array.from(files);
	// Upload files sequentially to show individual progress
	for (const file of fileArray) {
		await uploadFile(file);
	}
}
