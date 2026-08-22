import { describe, expect, it } from 'vitest';
import {
	addBackendToBungee,
	addBackendToVelocity,
	backendAddress,
	loadProxyOverviews,
	overlaySummaries,
	removeBackendFromBungee,
	removeBackendFromVelocity
} from './proxy';
import {
	bungeeFixture,
	fakeFetcher,
	summary,
	velocityFixture
} from './proxyTestUtils';
import type { ServerSummary } from '$lib/api/types';

describe('addBackendToVelocity', () => {
	it('adds the backend to the servers map without mutating the original config', () => {
		const original = velocityFixture();

		const updated = addBackendToVelocity(original, 'creative', 'localhost:25567');

		expect(updated.servers).toEqual({ lobby: 'localhost:25566', creative: 'localhost:25567' });
		expect(original.servers).toEqual({ lobby: 'localhost:25566' });
	});

	it('adds the backend to the try list without disturbing other fields', () => {
		const original = velocityFixture();

		const updated = addBackendToVelocity(original, 'creative', 'localhost:25567');

		expect(updated.try).toEqual(['lobby', 'creative']);
		expect(updated.bind).toBe(original.bind);
		expect(updated.playerInfoForwardingMode).toBe(original.playerInfoForwardingMode);
	});

	it('does not duplicate a try entry when overwriting an existing backend', () => {
		const original = velocityFixture();

		const updated = addBackendToVelocity(original, 'lobby', 'localhost:29999');

		expect(updated.try).toEqual(['lobby']);
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

	it('adds the backend to the priorities list without duplicating an existing entry', () => {
		const original = bungeeFixture();

		expect(addBackendToBungee(original, 'creative', 'localhost:25567').priorities).toEqual([
			'lobby',
			'creative'
		]);
		expect(addBackendToBungee(original, 'lobby', 'localhost:29999').priorities).toEqual(['lobby']);
	});
});

describe('removeBackendFromVelocity', () => {
	it('removes the backend without mutating the original config', () => {
		const original = velocityFixture();

		const updated = removeBackendFromVelocity(original, 'lobby');

		expect(updated.servers).toEqual({});
		expect(original.servers).toEqual({ lobby: 'localhost:25566' });
	});

	it('leaves other backends and fields untouched', () => {
		const original = addBackendToVelocity(velocityFixture(), 'creative', 'localhost:25567');

		const updated = removeBackendFromVelocity(original, 'creative');

		expect(updated.servers).toEqual({ lobby: 'localhost:25566' });
		expect(updated.bind).toBe(original.bind);
	});

	it('also drops the server from the try list so Velocity does not warn about it', () => {
		const original = velocityFixture();

		const updated = removeBackendFromVelocity(original, 'lobby');

		expect(updated.try).toEqual([]);
		expect(original.try).toEqual(['lobby']);
	});

	it('returns an equivalent config when the backend is already absent', () => {
		const original = velocityFixture();

		const updated = removeBackendFromVelocity(original, 'ghost');

		expect(updated.servers).toEqual(original.servers);
	});
});

describe('removeBackendFromBungee', () => {
	it('removes the backend without mutating the original config', () => {
		const original = bungeeFixture();

		const updated = removeBackendFromBungee(original, 'lobby');

		expect(updated.servers).toEqual({});
		expect(original.servers.lobby).toBeDefined();
	});

	it('leaves other backends untouched', () => {
		const original = addBackendToBungee(bungeeFixture(), 'creative', 'localhost:25567');

		const updated = removeBackendFromBungee(original, 'lobby');

		expect(Object.keys(updated.servers)).toEqual(['creative']);
	});

	it('also drops the server from the priorities list', () => {
		const original = bungeeFixture();

		const updated = removeBackendFromBungee(original, 'lobby');

		expect(updated.priorities).toEqual([]);
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

function hostSummary(name: string, up: boolean): ServerSummary {
	return {
		name,
		up,
		status: up ? 'Running' : 'Stopped',
		profile: 'velocity',
		port: 25565,
		playersOnline: up ? 3 : 0,
		playersMax: 100,
		memoryBytes: 123456,
		needsRestart: false
	} as ServerSummary;
}

describe('overlaySummaries', () => {
	it('replaces the load-time summary with the streamed one for known proxies', () => {
		const rows = [{ name: 'hub', detailStatus: 'Running', summary: hostSummary('hub', false) }];
		const live = { hub: hostSummary('hub', true) };

		const [updated] = overlaySummaries(rows, live);

		expect(updated.summary?.up).toBe(true);
	});

	it('keeps the load-time summary when the stream has no entry for a proxy', () => {
		const loadTime = hostSummary('hub', false);
		const rows = [{ name: 'hub', detailStatus: 'Stopped', summary: loadTime }];

		const [updated] = overlaySummaries(rows, {});

		expect(updated.summary).toBe(loadTime);
	});

	it('does not mutate the input rows', () => {
		const loadTime = hostSummary('hub', false);
		const rows = [{ name: 'hub', detailStatus: 'Stopped', summary: loadTime }];

		overlaySummaries(rows, { hub: hostSummary('hub', true) });

		expect(rows[0].summary?.up).toBe(false);
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
