import type { PageLoad } from './$types';
import { demoLoad } from '$lib/demo/loaders';

export const load: PageLoad = ({ fetch }) =>
	demoLoad(fetch, async (demoFetch) => {
		const settingsRes = await demoFetch('/api/settings');
		const settings = settingsRes.ok ? await settingsRes.json() : null;

		return {
			settings: {
				data: settings,
				error: settingsRes.ok ? null : `Failed to load settings (${settingsRes.status})`
			}
		};
	});
