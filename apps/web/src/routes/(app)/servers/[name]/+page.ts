import type { PageLoad } from './$types';
import { getServerStatus, getServerWatchdogStatus } from '$lib/api/client';
import { demoLoad } from '$lib/demo/loaders';

export const load: PageLoad = ({ fetch, params }) =>
	demoLoad(fetch, async (demoFetch) => {
		const heartbeat = await getServerStatus(demoFetch, params.name);
		const watchdog = await getServerWatchdogStatus(demoFetch, params.name);
		return { heartbeat, watchdog };
	});
