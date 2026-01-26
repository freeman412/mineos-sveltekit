import type { PageLoad } from './$types';
import { getHostServers } from '$lib/api/client';
import { demoLoad } from '$lib/demo/loaders';

export const load: PageLoad = ({ fetch, parent }) =>
	demoLoad(fetch, async (demoFetch) => {
		const { user } = await parent();
		const [servers, curseForgeStatusRes] = await Promise.all([
			getHostServers(demoFetch),
			demoFetch('/api/settings/curseforge/status')
		]);

		const curseForgeStatus = curseForgeStatusRes.ok
			? await curseForgeStatusRes.json()
			: { isConfigured: false };

		return {
			servers,
			curseForgeConfigured: curseForgeStatus.isConfigured,
			isAdmin: user?.role === 'admin'
		};
	});
