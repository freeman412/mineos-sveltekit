import type { PageServerLoad } from './$types';
import { getHostServers } from '$lib/api/client';

export const load: PageServerLoad = async ({ fetch, parent }) => {
	const { user } = await parent();
	const [servers, curseForgeStatusRes] = await Promise.all([
		getHostServers(fetch),
		fetch('/api/settings/curseforge/status')
	]);

	const curseForgeStatus = curseForgeStatusRes.ok ? await curseForgeStatusRes.json() : { isConfigured: false };

	return {
		servers,
		curseForgeConfigured: curseForgeStatus.isConfigured,
		isAdmin: user?.role === 'admin'
	};
};
