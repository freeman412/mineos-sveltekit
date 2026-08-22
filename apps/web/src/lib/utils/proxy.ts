import type { Fetcher } from '$lib/api/client';
import { getProxyBackends } from '$lib/api/client';
import type { BungeeBackend, BungeeConfig, ProxyBackendSummary, VelocityConfig } from '$lib/api/types';

/**
 * One proxy's backend rollup for the proxies overview. `summary` is null
 * when the backend fetch failed; `error` carries the reason in that case.
 */
export type ProxyOverview = {
	proxyName: string;
	summary: ProxyBackendSummary | null;
	error: string | null;
};

/**
 * The address a proxy uses to reach a backend server, or null when the
 * server has no assigned port yet (and therefore cannot be attached).
 */
export function backendAddress(port?: number | null): string | null {
	if (!port || port <= 0) return null;
	return `localhost:${port}`;
}

/**
 * Register a game server in a Velocity proxy's server map. Returns a new
 * config; the original is left untouched. Re-attaching an existing name
 * overwrites its address rather than duplicating it.
 */
export function addBackendToVelocity(
	config: VelocityConfig,
	name: string,
	address: string
): VelocityConfig {
	return { ...config, servers: { ...config.servers, [name]: address } };
}

/**
 * Register a game server in a BungeeCord proxy's server map. New backends are
 * unrestricted and reuse the name as their MOTD. Returns a new config; the
 * original is left untouched.
 */
export function addBackendToBungee(
	config: BungeeConfig,
	name: string,
	address: string
): BungeeConfig {
	const entry: BungeeBackend = { address, motd: name, restricted: false };
	return { ...config, servers: { ...config.servers, [name]: entry } };
}

/**
 * Fetch the backend summary for every proxy in parallel. A proxy whose fetch
 * fails degrades to an error entry instead of failing the whole overview.
 */
export async function loadProxyOverviews(
	fetcher: Fetcher,
	proxyNames: readonly string[]
): Promise<ProxyOverview[]> {
	const results = await Promise.all(
		proxyNames.map(async (proxyName) => {
			const { data, error } = await getProxyBackends(fetcher, proxyName);
			return {
				proxyName,
				summary: data,
				error: data ? null : (error ?? 'Unknown error')
			};
		})
	);
	return results;
}
