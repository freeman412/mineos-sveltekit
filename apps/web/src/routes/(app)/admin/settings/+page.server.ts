import type { PageServerLoad } from './$types';

export const load: PageServerLoad = async ({ fetch }) => {
	const [settingsRes, metaRes] = await Promise.all([
		fetch('/api/settings'),
		fetch('/api/meta')
	]);

	const settings = settingsRes.ok ? await settingsRes.json() : null;
	const meta = metaRes.ok ? await metaRes.json() : null;

	return {
		settings: {
			data: settings,
			error: settingsRes.ok ? null : `Failed to load settings (${settingsRes.status})`
		},
		meta
	};
};
