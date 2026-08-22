import { describe, expect, it } from 'vitest';
import { loadProxyOverviews, splitByProxy } from './networking';
import type { ProxyBackendSummary } from '$lib/api/types';

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

describe('splitByProxy', () => {
	it('partitions servers into proxies and game servers, preserving order', () => {
		const servers = [
			{ name: 'fabricloco', up: true },
			{ name: 'hub', up: true },
			{ name: 'serverloco', up: false }
		];
		const { proxies, game } = splitByProxy(servers, new Set(['hub']));
		expect(proxies).toEqual([{ name: 'hub', up: true }]);
		expect(game).toEqual([
			{ name: 'fabricloco', up: true },
			{ name: 'serverloco', up: false }
		]);
	});

	it('treats every server as a game server when no proxies exist', () => {
		const servers = [{ name: 'solo' }, { name: 'other' }];
		const { proxies, game } = splitByProxy(servers, new Set());
		expect(proxies).toEqual([]);
		expect(game).toEqual(servers);
	});

	it('returns empty groups for an empty list', () => {
		expect(splitByProxy([], new Set(['hub']))).toEqual({ proxies: [], game: [] });
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
