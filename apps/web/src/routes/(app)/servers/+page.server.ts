import type { PageServerLoad } from './$types';
import { getHostImports, getHostServers } from '$lib/api/client';

export const load: PageServerLoad = async ({ fetch }) => {
	const [servers, imports] = await Promise.all([getHostServers(fetch), getHostImports(fetch)]);
	return {
		servers,
		imports
	};
};
