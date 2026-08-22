import type { Fetcher } from '$lib/api/client';
import { getProxyBackends } from '$lib/api/client';
import type {
	BungeeBackend,
	BungeeConfig,
	ProxyBackendSummary,
	ServerSummary,
	VelocityConfig
} from '$lib/api/types';

/**
 * Overlay the newest host-stream summaries onto load-time proxy rows so
 * status/players/memory tick live. Rows the stream has no entry for keep
 * their load-time summary.
 */
export function overlaySummaries<T extends { name: string; summary: ServerSummary | null }>(
	proxies: readonly T[],
	liveSummaries: Readonly<Record<string, ServerSummary>>
): T[] {
	return proxies.map((proxy) => {
		const live = liveSummaries[proxy.name];
		return live ? { ...proxy, summary: live } : proxy;
	});
}

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
 * Register a game server in a Velocity proxy's server map and append it to
 * the try list (fallback order) so players can be routed to it. Returns a
 * new config; the original is left untouched. Re-attaching an existing name
 * overwrites its address rather than duplicating it.
 */
export function addBackendToVelocity(
	config: VelocityConfig,
	name: string,
	address: string
): VelocityConfig {
	return {
		...config,
		servers: { ...config.servers, [name]: address },
		try: config.try.includes(name) ? config.try : [...config.try, name]
	};
}

/**
 * Register a game server in a BungeeCord proxy's server map and append it to
 * the priorities list (fallback order), mirroring the Velocity try handling.
 * New backends are unrestricted and reuse the name as their MOTD. Returns a
 * new config; the original is left untouched.
 */
export function addBackendToBungee(
	config: BungeeConfig,
	name: string,
	address: string
): BungeeConfig {
	const entry: BungeeBackend = { address, motd: name, restricted: false };
	return {
		...config,
		servers: { ...config.servers, [name]: entry },
		priorities: config.priorities.includes(name)
			? config.priorities
			: [...config.priorities, name]
	};
}

/**
 * Remove a game server from a Velocity proxy's server map and its try
 * list (a try entry without a matching server makes Velocity warn at
 * boot). Returns a new config; the original is left untouched. Removing
 * an absent name is a no-op.
 */
export function removeBackendFromVelocity(config: VelocityConfig, name: string): VelocityConfig {
	const { [name]: _removed, ...servers } = config.servers;
	return { ...config, servers, try: config.try.filter((n) => n !== name) };
}

/**
 * Remove a game server from a BungeeCord proxy's server map and its
 * priorities list, mirroring the Velocity try handling. Returns a new
 * config; the original is left untouched. Removing an absent name is a no-op.
 */
export function removeBackendFromBungee(config: BungeeConfig, name: string): BungeeConfig {
	const { [name]: _removed, ...servers } = config.servers;
	return { ...config, servers, priorities: config.priorities.filter((n) => n !== name) };
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
