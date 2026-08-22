import { describe, expect, it } from 'vitest';
import { attachServerToProxy, detachServerFromProxy } from './proxyAttach';
import type { BackendForwarding, ServerSummary, VelocityConfig } from '$lib/api/types';
import { velocityFixture } from './proxyTestUtils';

type Route = { status?: number; body?: unknown };

/** Fake fetcher keyed by "METHOD path", recording every call with its parsed body. */
function recordingFetcher(routes: Record<string, Route>) {
	const calls: { method: string; path: string; body?: unknown }[] = [];
	const fetcher = async (input: RequestInfo | URL, init?: RequestInit) => {
		const path = String(input);
		const method = (init?.method ?? 'GET').toUpperCase();
		const body = init?.body ? (JSON.parse(String(init.body)) as unknown) : undefined;
		calls.push({ method, path, body });
		const route = routes[`${method} ${path}`];
		if (!route) throw new Error(`unexpected request: ${method} ${path}`);
		return new Response(JSON.stringify(route.body ?? {}), { status: route.status ?? 200 });
	};
	return { fetcher, calls };
}

function forwarding(remediationAction: BackendForwarding['remediationAction']): BackendForwarding {
	return {
		serverName: 'creative',
		status: 'Securable',
		isSpoofable: remediationAction !== null,
		proxyKind: 'VelocityModern',
		tier: 'Native',
		proxyName: 'hub',
		loader: 'paper',
		serverOnlineMode: false,
		backendForwardingConfigured: false,
		secretMatches: false,
		exposure: 'NotExposed',
		exposureDetail: null,
		remediationAction
	};
}

function hostList(...entries: { name: string; port: number | null }[]) {
	return entries.map((e) => ({ name: e.name, port: e.port })) as ServerSummary[];
}

describe('attachServerToProxy', () => {
	it('registers the backend in the velocity config, then secures forwarding', async () => {
		const { fetcher, calls } = recordingFetcher({
			'GET /api/host/servers': { body: hostList({ name: 'creative', port: 25567 }) },
			'GET /api/servers/hub/velocity-config': { body: velocityFixture() },
			'PUT /api/servers/hub/velocity-config': { body: { ok: true } },
			'GET /api/servers/creative/forwarding': { body: forwarding('secure') },
			'POST /api/servers/creative/forwarding/secure': { body: forwarding(null) }
		});

		const result = await attachServerToProxy(fetcher, {
			serverName: 'creative',
			proxyName: 'hub'
		});

		expect(result).toEqual({ ok: true });
		expect(calls.map((c) => `${c.method} ${c.path}`)).toEqual([
			'GET /api/host/servers',
			'GET /api/servers/hub/velocity-config',
			'PUT /api/servers/hub/velocity-config',
			'GET /api/servers/creative/forwarding',
			'POST /api/servers/creative/forwarding/secure'
		]);
		const putBody = calls[2].body as { servers: Record<string, string> };
		expect(putBody.servers.creative).toBe('localhost:25567');
	});

	it('falls back to the bungee config and installs the forwarding mod when needed', async () => {
		const { fetcher, calls } = recordingFetcher({
			'GET /api/host/servers': { body: hostList({ name: 'creative', port: 25567 }) },
			'GET /api/servers/hub/velocity-config': { body: { exists: false } },
			'GET /api/servers/hub/bungee-config': {
				body: { exists: true, servers: {}, priorities: [] }
			},
			'PUT /api/servers/hub/bungee-config': { body: { ok: true } },
			'GET /api/servers/creative/forwarding': { body: forwarding('install-mod') },
			'POST /api/servers/creative/forwarding/install-mod': { body: forwarding('secure') },
			'POST /api/servers/creative/forwarding/secure': { body: forwarding(null) }
		});

		const result = await attachServerToProxy(fetcher, {
			serverName: 'creative',
			proxyName: 'hub'
		});

		expect(result).toEqual({ ok: true });
		expect(calls.map((c) => `${c.method} ${c.path}`)).toEqual([
			'GET /api/host/servers',
			'GET /api/servers/hub/velocity-config',
			'GET /api/servers/hub/bungee-config',
			'PUT /api/servers/hub/bungee-config',
			'GET /api/servers/creative/forwarding',
			'POST /api/servers/creative/forwarding/install-mod',
			'POST /api/servers/creative/forwarding/secure'
		]);
		const putBody = calls[3].body as { servers: Record<string, { address: string }> };
		expect(putBody.servers.creative.address).toBe('localhost:25567');
	});

	it('fails without touching anything when the server has no assigned port', async () => {
		const { fetcher, calls } = recordingFetcher({
			'GET /api/host/servers': { body: hostList({ name: 'creative', port: null }) }
		});

		const result = await attachServerToProxy(fetcher, {
			serverName: 'creative',
			proxyName: 'hub'
		});

		expect(result.ok).toBe(false);
		if (!result.ok) expect(result.error).toContain('port');
		expect(calls).toHaveLength(1);
	});

	it('fails cleanly when neither proxy config can be loaded', async () => {
		const { fetcher, calls } = recordingFetcher({
			'GET /api/host/servers': { body: hostList({ name: 'creative', port: 25567 }) },
			'GET /api/servers/hub/velocity-config': { body: { exists: false } },
			'GET /api/servers/hub/bungee-config': { body: { exists: false } }
		});

		const result = await attachServerToProxy(fetcher, {
			serverName: 'creative',
			proxyName: 'hub'
		});

		expect(result.ok).toBe(false);
		if (!result.ok) expect(result.error).toContain("couldn't load");
		expect(calls).toHaveLength(3);
	});

	it('reports securing failures but keeps the attachment', async () => {
		const { fetcher } = recordingFetcher({
			'GET /api/host/servers': { body: hostList({ name: 'creative', port: 25567 }) },
			'GET /api/servers/hub/velocity-config': { body: velocityFixture() },
			'PUT /api/servers/hub/velocity-config': { body: { ok: true } },
			'GET /api/servers/creative/forwarding': { body: forwarding('secure') },
			'POST /api/servers/creative/forwarding/secure': { status: 500, body: { error: 'boom' } }
		});

		const result = await attachServerToProxy(fetcher, {
			serverName: 'creative',
			proxyName: 'hub'
		});

		expect(result.ok).toBe(false);
		if (!result.ok) expect(result.error).toContain('securing forwarding failed');
	});

	it('warns instead of claiming success when the forwarding check fails', async () => {
		const { fetcher, calls } = recordingFetcher({
			'GET /api/host/servers': { body: hostList({ name: 'creative', port: 25567 }) },
			'GET /api/servers/hub/velocity-config': { body: velocityFixture() },
			'PUT /api/servers/hub/velocity-config': { body: { ok: true } },
			'GET /api/servers/creative/forwarding': { status: 500, body: { error: 'status unavailable' } }
		});

		const result = await attachServerToProxy(fetcher, {
			serverName: 'creative',
			proxyName: 'hub'
		});

		// Registered, but nothing may claim it is secured.
		expect(result.ok).toBe(false);
		if (!result.ok) {
			expect(result.error).toContain("couldn't check forwarding status");
			expect(result.error).toContain('open to impersonation');
		}
		expect(calls.map((c) => `${c.method} ${c.path}`)).toEqual([
			'GET /api/host/servers',
			'GET /api/servers/hub/velocity-config',
			'PUT /api/servers/hub/velocity-config',
			'GET /api/servers/creative/forwarding'
		]);
	});

	it('reports progress through onStep', async () => {
		const { fetcher } = recordingFetcher({
			'GET /api/host/servers': { body: hostList({ name: 'creative', port: 25567 }) },
			'GET /api/servers/hub/velocity-config': { body: velocityFixture() },
			'PUT /api/servers/hub/velocity-config': { body: { ok: true } },
			'GET /api/servers/creative/forwarding': { body: forwarding(null) }
		});
		const steps: string[] = [];

		await attachServerToProxy(fetcher, {
			serverName: 'creative',
			proxyName: 'hub',
			onStep: (label) => steps.push(label)
		});

		expect(steps).toContain('Attaching creative to hub...');
	});
});

describe('detachServerFromProxy', () => {
	it('removes the server from the velocity config', async () => {
		const config: VelocityConfig = velocityFixture();
		const { fetcher, calls } = recordingFetcher({
			'GET /api/servers/hub/velocity-config': { body: config },
			'PUT /api/servers/hub/velocity-config': { body: { ok: true } }
		});

		const result = await detachServerFromProxy(fetcher, {
			serverName: 'lobby',
			proxyName: 'hub'
		});

		expect(result).toEqual({ ok: true });
		const putBody = calls[1].body as { servers: Record<string, string> };
		expect(putBody.servers.lobby).toBeUndefined();
	});

	it('refuses without a write when the server is not attached', async () => {
		const { fetcher, calls } = recordingFetcher({
			'GET /api/servers/hub/velocity-config': { body: velocityFixture() }
		});

		const result = await detachServerFromProxy(fetcher, {
			serverName: 'creative',
			proxyName: 'hub'
		});

		expect(result.ok).toBe(false);
		if (!result.ok) expect(result.error).toContain("isn't attached");
		expect(calls).toHaveLength(1);
	});

	it('removes via the bungee config when velocity is absent', async () => {
		const { fetcher, calls } = recordingFetcher({
			'GET /api/servers/hub/velocity-config': { body: { exists: false } },
			'GET /api/servers/hub/bungee-config': {
				body: { exists: true, servers: { creative: { address: 'localhost:25567', motd: 'c', restricted: false } }, priorities: ['creative'] }
			},
			'PUT /api/servers/hub/bungee-config': { body: { ok: true } }
		});

		const result = await detachServerFromProxy(fetcher, {
			serverName: 'creative',
			proxyName: 'hub'
		});

		expect(result).toEqual({ ok: true });
		const putBody = calls[2].body as { servers: Record<string, unknown> };
		expect(putBody.servers.creative).toBeUndefined();
	});

	it('fails cleanly when neither proxy config can be loaded', async () => {
		const { fetcher } = recordingFetcher({
			'GET /api/servers/hub/velocity-config': { body: { exists: false } },
			'GET /api/servers/hub/bungee-config': { body: { exists: false } }
		});

		const result = await detachServerFromProxy(fetcher, {
			serverName: 'creative',
			proxyName: 'hub'
		});

		expect(result.ok).toBe(false);
		if (!result.ok) expect(result.error).toContain("couldn't load");
	});
});
