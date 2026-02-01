import { browser } from '$app/environment';
import { writable } from 'svelte/store';

const sheepStorageKey = 'mineos_ui_sheep_enabled';
const themeStorageKey = 'mineos_theme';

export type Theme = 'overworld' | 'nether' | 'end';

function createSheepEnabledStore() {
	const { subscribe, set } = writable(false);

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

function createThemeStore() {
	const { subscribe, set } = writable<Theme>('overworld');

	if (browser) {
		const stored = localStorage.getItem(themeStorageKey);
		if (stored === 'overworld' || stored === 'nether' || stored === 'end') {
			set(stored);
		}
	}

	return {
		subscribe,
		set: (value: Theme) => {
			if (browser) {
				localStorage.setItem(themeStorageKey, value);
			}
			set(value);
		},
		toggle: () => {
			if (browser) {
				const current = localStorage.getItem(themeStorageKey);
				const newTheme: Theme = current === 'nether' ? 'overworld' : 'nether';
				localStorage.setItem(themeStorageKey, newTheme);
				set(newTheme);
			}
		}
	};
}

export const sheepEnabled = createSheepEnabledStore();
export const theme = createThemeStore();
