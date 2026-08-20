import type { PageServerLoad } from './$types';
import { getServerStatus, getServerWatchdogStatus, getForwardingStatus } from '$lib/api/client';

export const load: PageServerLoad = async ({ params, fetch }) => {
	const heartbeat = await getServerStatus(fetch, params.name);
	const watchdog = await getServerWatchdogStatus(fetch, params.name);
	// Derived fresh on every load rather than cached: a hand-edited
	// server.properties must be reflected the next time the page is opened.
	const forwarding = await getForwardingStatus(fetch, params.name);
	return {
		heartbeat,
		watchdog,
		forwarding
	};
};
