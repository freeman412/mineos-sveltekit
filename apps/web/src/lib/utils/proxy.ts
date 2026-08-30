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
 *
 * MineOS runs every server on the host the proxy runs on, so `localhost`
 * is always right today; backends on another host or in a separate
 * container would need the address to come from the API instead.
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
 * A forced host's backends are stored in velocity.toml as an ordered list —
 * Velocity tries them in that order. The editor keeps them as one comma-separated
 * string per row, so both directions go through here.
 */
export function splitBackendList(value: string): string[] {
	return value
		.split(',')
		.map((s) => s.trim())
		.filter((s) => s.length > 0);
}

/**
 * Backends to offer for one forced host: every name already routed here that is
 * no longer a defined backend, followed by the defined ones.
 *
 * The stale names come first and are never dropped. A picker that offered only
 * defined backends would silently delete a hostname's existing routing the first
 * time someone opened the row — including config MineOS never wrote.
 */
export function forcedHostOptions(
	chosen: readonly string[],
	backendNames: readonly string[]
): string[] {
	const stale = chosen.filter((n) => !backendNames.includes(n));
	return [...stale, ...backendNames];
}

/**
 * Add or remove one backend from a forced host, preserving the order of the rest.
 * A newly ticked backend goes last, because that is the lowest routing priority
 * and the least surprising place for it.
 *
 * Order is the whole point: the <select multiple> this replaced reported its
 * selection in DOM order, so touching a row whose config listed `survival, lobby`
 * rewrote it to `lobby, survival` and changed where players landed.
 */
export function toggleForcedHostBackend(
	current: readonly string[],
	name: string,
	checked: boolean
): string[] {
	if (!checked) return current.filter((n) => n !== name);
	return current.includes(name) ? [...current] : [...current, name];
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

/**
 * The host summary stream carries no serverType, so a server created in
 * another tab cannot be told apart from a proxy until the page data
 * reloads. Returns a callback for stream payloads that asks for exactly
 * one reload per newly-seen name — never a reload per message.
 */
export function createClassificationRefresher(
	knownNames: readonly string[],
	refresh: () => void
): (names: readonly { name: string }[]) => void {
	const seen = new Set(knownNames);
	return (rows) => {
		let sawNew = false;
		for (const row of rows) {
			if (seen.has(row.name)) continue;
			seen.add(row.name);
			sawNew = true;
		}
		if (sawNew) refresh();
	};
}

/**
 * Tabs a proxy keeps, mapped from their /servers path segment to the
 * /proxies one. Anything absent here has no meaning for a proxy — worlds,
 * players and mods were already disabled before the move, and archives are
 * ceremony for a directory whose whole non-jar content is under a megabyte.
 */
const PROXY_TAB_PATHS: Readonly<Record<string, string>> = {
	advanced: 'advanced',
	backups: 'backups',
	cron: 'cron',
	files: 'files',
	performance: 'performance',
	plugins: 'plugins',
	'proxy-config': 'proxy-config',
	// A game server's Properties is server.properties; a proxy's is its
	// velocity.toml/config.yml editor.
	config: 'proxy-config',
	// The Server/Java/Crash log viewer, which no tab ever linked to.
	console: 'logs'
};

/**
 * Where a /servers/<name>/... URL belongs once <name> is known to be a proxy.
 *
 * #176 pulled proxies out of /servers but left this page behind it, so a
 * proxy's console, files and backups were reachable only by guessing the URL.
 * Callers redirect to whatever this returns.
 */
export function proxyDetailPath(name: string, pathname: string): string {
	const encoded = encodeURIComponent(name);
	const base = `/proxies/${encoded}`;

	const prefix = `/servers/${name}`;
	if (!pathname.startsWith(prefix)) return base;

	const rest = pathname.slice(prefix.length).replace(/^\/+/, '').replace(/\/+$/, '');
	if (!rest) return base;

	const tab = PROXY_TAB_PATHS[rest.split('/')[0]];
	return tab ? `${base}/${tab}` : base;
}
