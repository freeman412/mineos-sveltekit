import { describe, expect, it } from 'vitest';
import {
	addBackendToBungee,
	addBackendToVelocity,
	backendAddress,
	loadProxyOverviews
} from './proxy';
import type { BungeeConfig, ProxyBackendSummary, VelocityConfig } from '$lib/api/types';

function backend(name: string) {
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

function summary(proxyName: string, backends: string[]): ProxyBackendSummary {
	return { proxyName, backends: backends.map(backend) };
}

/** Fake fetcher: maps paths to JSON payloads or status codes, recording requested URLs. */
function fakeFetcher(routes: Record<string, { status?: number; body?: unknown }>) {
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

function velocityFixture(): VelocityConfig {
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

function bungeeFixture(): BungeeConfig {
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

describe('addBackendToVelocity', () => {
	it('adds the backend to the servers map without mutating the original config', () => {
		const original = velocityFixture();

		const updated = addBackendToVelocity(original, 'creative', 'localhost:25567');

		expect(updated.servers).toEqual({ lobby: 'localhost:25566', creative: 'localhost:25567' });
		expect(original.servers).toEqual({ lobby: 'localhost:25566' });
	});

	it('leaves everything except the servers map untouched', () => {
		const original = velocityFixture();

		const updated = addBackendToVelocity(original, 'creative', 'localhost:25567');

		expect(updated.try).toEqual(['lobby']);
		expect(updated.bind).toBe(original.bind);
		expect(updated.playerInfoForwardingMode).toBe(original.playerInfoForwardingMode);
	});

	it('overwrites an existing backend of the same name instead of duplicating', () => {
		const original = velocityFixture();

		const updated = addBackendToVelocity(original, 'lobby', 'localhost:29999');

		expect(Object.keys(updated.servers)).toEqual(['lobby']);
		expect(updated.servers.lobby).toBe('localhost:29999');
	});
});

describe('addBackendToBungee', () => {
	it('adds the backend as an unrestricted entry without mutating the original config', () => {
		const original = bungeeFixture();

		const updated = addBackendToBungee(original, 'creative', 'localhost:25567');

		expect(updated.servers.creative).toEqual({
			address: 'localhost:25567',
			motd: 'creative',
			restricted: false
		});
		expect(original.servers.creative).toBeUndefined();
	});

	it('overwrites an existing backend of the same name instead of duplicating', () => {
		const original = bungeeFixture();

		const updated = addBackendToBungee(original, 'lobby', 'localhost:29999');

		expect(Object.keys(updated.servers)).toEqual(['lobby']);
		expect(updated.servers.lobby.address).toBe('localhost:29999');
	});
});

describe('backendAddress', () => {
	it('builds a localhost address from the assigned port', () => {
		expect(backendAddress(25568)).toBe('localhost:25568');
	});

	it('returns null when the server has no assigned port yet', () => {
		expect(backendAddress(null)).toBeNull();
		expect(backendAddress(undefined)).toBeNull();
	});
});

describe('loadProxyOverviews', () => {
	it('fetches each proxy backend summary and keeps input order', async () => {
		const { fetcher, requested } = fakeFetcher({
			'/api/servers/hub-a/forwarding/backends': { body: summary('hub-a', ['lobby']) },
			'/api/servers/hub-b/forwarding/backends': { body: summary('hub-b', ['creative']) }
		});

		const overviews = await loadProxyOverviews(fetcher, ['hub-a', 'hub-b']);

		expect(requested).toEqual([
			'/api/servers/hub-a/forwarding/backends',
			'/api/servers/hub-b/forwarding/backends'
		]);
		expect(overviews).toEqual([
			{ proxyName: 'hub-a', summary: summary('hub-a', ['lobby']), error: null },
			{ proxyName: 'hub-b', summary: summary('hub-b', ['creative']), error: null }
		]);
	});

	it('degrades a failed proxy to an error entry without losing the others', async () => {
		const { fetcher } = fakeFetcher({
			'/api/servers/hub-a/forwarding/backends': { status: 503, body: { error: 'boom' } },
			'/api/servers/hub-b/forwarding/backends': { body: summary('hub-b', []) }
		});

		const [a, b] = await loadProxyOverviews(fetcher, ['hub-a', 'hub-b']);

		expect(a.summary).toBeNull();
		expect(a.error).toContain('boom');
		expect(b.error).toBeNull();
		expect(b.summary?.proxyName).toBe('hub-b');
	});

	it('returns an empty overview list when there are no proxies', async () => {
		const { fetcher, requested } = fakeFetcher({});
		expect(await loadProxyOverviews(fetcher, [])).toEqual([]);
		expect(requested).toEqual([]);
	});
});
