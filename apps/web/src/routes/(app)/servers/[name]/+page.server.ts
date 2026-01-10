import type { PageServerLoad } from './$types';
import { getServerStatus } from '$lib/api/client';

export const load: PageServerLoad = async ({ params, fetch }) => {
	const heartbeat = await getServerStatus(fetch, params.name);
	return {
		heartbeat
	};
};
