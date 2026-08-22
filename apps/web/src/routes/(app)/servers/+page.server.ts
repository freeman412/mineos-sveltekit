import type { PageServerLoad } from './$types';
import { getAllServers, getHostImports, getHostServers } from '$lib/api/client';

export const load: PageServerLoad = async ({ fetch }) => {
	// The host/servers summary has no serverType, so fetch the detailed list too
	// to learn which entries are proxies (the card grid splits them out).
	const [servers, imports, details] = await Promise.all([
		getHostServers(fetch),
		getHostImports(fetch),
		getAllServers(fetch)
	]);
	const proxyNames = (details.data ?? [])
		.filter((s) => s.serverType === 'proxy')
		.map((s) => s.name);
	return {
		servers,
		imports,
		proxyNames
	};
};
