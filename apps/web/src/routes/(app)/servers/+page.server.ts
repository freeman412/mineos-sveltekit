import type { PageServerLoad } from './$types';
import { getHostServers } from '$lib/api/client';

export const load: PageServerLoad = async ({ fetch }) => {
	const servers = await getHostServers(fetch);
	return {
		servers
	};
};
