import { browser } from '$app/environment';
import { writable } from 'svelte/store';

const sheepStorageKey = 'mineos_ui_sheep_enabled';

function createSheepEnabledStore() {
	const { subscribe, set } = writable(true);

	if (browser) {
		const stored = localStorage.getItem(sheepStorageKey);
		if (stored !== null) {
			set(stored === 'true');
		}
	}

	return {
		subscribe,
		set: (value: boolean) => {
			if (browser) {
				localStorage.setItem(sheepStorageKey, value ? 'true' : 'false');
			}
			set(value);
		}
	};
}

export const sheepEnabled = createSheepEnabledStore();
