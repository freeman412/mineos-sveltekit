import type { Fetcher } from '$lib/api/client';
import { getProxyBackends } from '$lib/api/client';
import type { ProxyBackendSummary } from '$lib/api/types';

/**
 * One proxy's backend rollup for the networking overview. `summary` is null
 * when the backend fetch failed; `error` carries the reason in that case.
 */
export type ProxyOverview = {
	proxyName: string;
	summary: ProxyBackendSummary | null;
	error: string | null;
};

/**
 * Partition servers into proxy-type and game servers by name, preserving
 * each group's original order.
 */
export function splitByProxy<T extends { name: string }>(
	servers: readonly T[],
	proxyNames: ReadonlySet<string>
): { proxies: T[]; game: T[] } {
	const proxies: T[] = [];
	const game: T[] = [];
	for (const server of servers) {
		if (proxyNames.has(server.name)) {
			proxies.push(server);
		} else {
			game.push(server);
		}
	}
	return { proxies, game };
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
