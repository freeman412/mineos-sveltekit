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
	// In parallel, not in series: these four are independent, and a full page
	// load blocks on all of them before a single byte is sent. Serially they
	// stack up into a request slow enough for a reverse proxy in front of
	// MineOS to time out on.
	//
	// Forwarding is derived fresh on every load rather than cached: a
	// hand-edited server.properties must be reflected the next time the page
	// is opened.
	const [heartbeat, watchdog, forwarding, detail] = await Promise.all([
		getServerStatus(fetch, params.name),
		getServerWatchdogStatus(fetch, params.name),
		getForwardingStatus(fetch, params.name),
		getServer(fetch, params.name)
	]);

	// A proxy's overview leads with which servers sit behind it and whether
	// they can be impersonated — the one view that is about being a proxy
	// rather than about being a process.
	let proxyOverview: ProxyOverview | null = null;
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
