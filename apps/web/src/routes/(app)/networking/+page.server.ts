import type { PageServerLoad } from './$types';
import { getAllServers } from '$lib/api/client';
import { loadProxyOverviews } from '$lib/utils/networking';

export const load: PageServerLoad = async ({ fetch }) => {
	const { data: servers } = await getAllServers(fetch);
	const proxies = (servers ?? []).filter((s) => s.serverType === 'proxy');
	const overviews = await loadProxyOverviews(
		fetch,
		proxies.map((p) => p.name)
	);

	return {
		proxies: proxies.map((p) => ({
			name: p.name,
			status: p.status,
			overview: overviews.find((o) => o.proxyName === p.name)!
		}))
	};
};
