import type { PageServerLoad } from './$types';
import { getServerStatus, getServerWatchdogStatus } from '$lib/api/client';

export const load: PageServerLoad = async ({ params, fetch }) => {
	const heartbeat = await getServerStatus(fetch, params.name);
	const watchdog = await getServerWatchdogStatus(fetch, params.name);
	return {
		heartbeat,
		watchdog
	};
};
