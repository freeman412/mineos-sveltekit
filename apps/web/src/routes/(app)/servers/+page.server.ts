import type { PageServerLoad } from './$types';
import { getAllServers, getHostImports, getHostServers } from '$lib/api/client';

export const load: PageServerLoad = async ({ fetch }) => {
	// Proxies live on the Proxies page, so the grid needs to know which
	// entries to hide (the host summary stream itself carries no serverType).
	const [servers, imports, details] = await Promise.all([
		getHostServers(fetch),
		getHostImports(fetch),
		getAllServers(fetch)
	]);
	const proxyNames = (details.data ?? [])
		.filter((s) => s.serverType === 'proxy')
		.map((s) => s.name);
	return { servers, imports, proxyNames };
};
