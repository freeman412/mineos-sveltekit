import {
	getServerStatus,
	getServerWatchdogStatus,
	getForwardingStatus,
	getServer
} from '$lib/api/client';
import { loadProxyOverviews, type ProxyOverview } from '$lib/utils/proxy';

type LoadEvent = { params: { name: string }; fetch: typeof globalThis.fetch };

/**
 * Status, watchdog and forwarding for a server's Overview tab. Shared by
 * /servers/[name] and /proxies/[name]; forwarding in particular is what tells
 * a proxy's overview whether its backends can be impersonated.
 */
export async function loadOverview({ params, fetch }: LoadEvent) {
	const heartbeat = await getServerStatus(fetch, params.name);
	const watchdog = await getServerWatchdogStatus(fetch, params.name);
	// Derived fresh on every load rather than cached: a hand-edited
	// server.properties must be reflected the next time the page is opened.
	const forwarding = await getForwardingStatus(fetch, params.name);

	// A proxy's overview leads with which servers sit behind it and whether
	// they can be impersonated — the one view that is about being a proxy
	// rather than about being a process.
	let proxyOverview: ProxyOverview | null = null;
	const detail = await getServer(fetch, params.name);
	if (detail.data?.serverType === 'proxy') {
		const [overview] = await loadProxyOverviews(fetch, [params.name]);
		proxyOverview = overview ?? null;
	}

	return {
		heartbeat,
		watchdog,
		forwarding,
		proxyOverview
	};
}
