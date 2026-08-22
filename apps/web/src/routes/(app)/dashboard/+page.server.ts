import type { PageServerLoad } from './$types';
import * as api from '$lib/api/client';

export const load: PageServerLoad = async ({ fetch }) => {
	const [servers, hostMetrics, details] = await Promise.all([
		api.listServers(fetch),
		api.getHostMetrics(fetch),
		api.getAllServers(fetch)
	]);

	return {
		servers,
		hostMetrics,
		// The host summary has no serverType, so proxy names come from the
		// detailed list — proxies live on the Proxies page, not the dashboard.
		proxyNames: (details.data ?? []).filter((s) => s.serverType === 'proxy').map((s) => s.name)
	};
};
