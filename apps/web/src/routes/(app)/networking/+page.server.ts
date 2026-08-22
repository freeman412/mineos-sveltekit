import type { PageServerLoad } from './$types';
import { getAllServers, getHostServers } from '$lib/api/client';
import { loadProxyOverviews } from '$lib/utils/networking';

export const load: PageServerLoad = async ({ fetch }) => {
	// The host summary carries live status/players/memory but no serverType;
	// the detailed list knows which entries are proxies.
	const [details, host] = await Promise.all([getAllServers(fetch), getHostServers(fetch)]);
	const proxies = (details.data ?? []).filter((s) => s.serverType === 'proxy');
	const summaries = new Map((host.data ?? []).map((s) => [s.name, s]));
	const overviews = await loadProxyOverviews(
		fetch,
		proxies.map((p) => p.name)
	);

	return {
		proxies: proxies.map((p) => ({
			name: p.name,
			detailStatus: p.status,
			summary: summaries.get(p.name) ?? null,
			overview: overviews.find((o) => o.proxyName === p.name)!
		}))
	};
};
