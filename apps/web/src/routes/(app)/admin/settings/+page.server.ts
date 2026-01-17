import type { PageServerLoad } from './$types';

export const load: PageServerLoad = async ({ fetch }) => {
	const settingsRes = await fetch('/api/settings');
	const settings = settingsRes.ok ? await settingsRes.json() : null;

	return {
		settings: {
			data: settings,
			error: settingsRes.ok ? null : `Failed to load settings (${settingsRes.status})`
		}
	};
};
