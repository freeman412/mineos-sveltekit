/**
 * Shared fixtures and fakes for the proxy unit tests. Lives outside
 * *.test.ts so importing it never registers a second copy of a suite.
 */
import type { BungeeConfig, ProxyBackendSummary, VelocityConfig } from '$lib/api/types';

export function backend(name: string) {
	return {
		serverName: name,
		status: 'Secured',
		isSpoofable: false,
		proxyKind: 'VelocityModern',
		tier: 'Native',
		proxyName: 'hub',
		loader: 'paper',
		serverOnlineMode: false,
		backendForwardingConfigured: true,
		secretMatches: true,
		exposure: 'NotExposed',
		exposureDetail: null,
		remediationAction: null
	} as ProxyBackendSummary['backends'][number];
}

export function summary(proxyName: string, backends: string[]): ProxyBackendSummary {
	return { proxyName, backends: backends.map(backend) };
}

/** Fake fetcher: maps paths to JSON payloads or status codes, recording requested URLs. */
export function fakeFetcher(routes: Record<string, { status?: number; body?: unknown }>) {
	const requested: string[] = [];
	const fetcher = async (input: RequestInfo | URL) => {
		const path = String(input);
		requested.push(path);
		const route = routes[path];
		if (!route) throw new Error(`unexpected request: ${path}`);
		return new Response(JSON.stringify(route.body ?? {}), {
			status: route.status ?? 200
		});
	};
	return { fetcher, requested };
}

export function velocityFixture(): VelocityConfig {
	return {
		exists: true,
		bind: '0.0.0.0:25565',
		motd: 'A Velocity Server',
		showMaxPlayers: 500,
		onlineMode: true,
		forceKeyAuthentication: true,
		preventClientProxyConnections: true,
		playerInfoForwardingMode: 'modern',
		forwardingSecretFile: 'forwarding.secret',
		announceForge: false,
		kickExistingPlayers: false,
		pingPassthrough: 'DISABLED',
		enablePlayerAddressLogging: false,
		servers: { lobby: 'localhost:25566' },
		try: ['lobby'],
		forcedHosts: {}
	};
}

export function bungeeFixture(): BungeeConfig {
	return {
		exists: true,
		onlineMode: false,
		ipForward: true,
		playerLimit: 100,
		timeout: 30000,
		networkCompressionThreshold: 256,
		forgeSupport: false,
		logCommands: false,
		logPings: false,
		connectionThrottle: 4000,
		connectionThrottleLimit: 3,
		host: '0.0.0.0:25565',
		motd: 'A BungeeCord server',
		maxPlayers: 100,
		queryEnabled: false,
		queryPort: 25577,
		pingPassthrough: false,
		forceDefaultServer: false,
		tabList: 'SERVER',
		proxyProtocol: false,
		priorities: ['lobby'],
		forcedHosts: {},
		servers: { lobby: { address: 'localhost:25566', motd: 'lobby', restricted: false } }
	};
}
