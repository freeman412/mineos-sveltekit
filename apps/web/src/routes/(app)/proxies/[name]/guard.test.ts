import { describe, expect, it, vi } from 'vitest';
import { isRedirect } from '@sveltejs/kit';

vi.mock('$lib/api/client', () => ({
	getServer: vi.fn()
}));

import { getServer } from '$lib/api/client';
import { load as proxyLoad } from './+layout.server';
import { load as serverLoad } from '../../servers/[name]/+layout.server';

const asMock = getServer as unknown as ReturnType<typeof vi.fn>;
const run = (load: unknown, name: string, pathname: string) =>
	(load as (e: unknown) => unknown)({
		params: { name },
		fetch: (() => {}) as unknown,
		url: new URL(`http://localhost${pathname}`)
	});

const serverOfType = (serverType: string) => ({ data: { name: 'x', serverType }, error: null });

describe('section guards', () => {
	it('sends a proxy reached through /servers to the matching proxy tab', async () => {
		asMock.mockResolvedValue(serverOfType('proxy'));
		await expect(run(serverLoad, 'hub', '/servers/hub/files')).rejects.toSatisfy((err: unknown) => {
			if (!isRedirect(err)) return false;
			expect(err.location).toBe('/proxies/hub/files');
			return true;
		});
	});

	it('sends a game server reached through /proxies back to /servers', async () => {
		asMock.mockResolvedValue(serverOfType('java'));
		await expect(run(proxyLoad, 'freemancraft', '/proxies/freemancraft')).rejects.toSatisfy(
			(err: unknown) => {
				if (!isRedirect(err)) return false;
				expect(err.location).toBe('/servers/freemancraft');
				return true;
			}
		);
	});

	/**
	 * Change Server Type can turn a proxy into a game server and back, so these
	 * redirects must never be cached as permanent — a 308 would strand the old
	 * URL on the wrong section for the life of the browser's cache.
	 */
	it('redirects temporarily, because serverType can change', async () => {
		asMock.mockResolvedValue(serverOfType('proxy'));
		await expect(run(serverLoad, 'hub', '/servers/hub')).rejects.toSatisfy((err: unknown) => {
			if (!isRedirect(err)) return false;
			expect(err.status).toBe(307);
			return true;
		});

		asMock.mockResolvedValue(serverOfType('java'));
		await expect(run(proxyLoad, 'gs', '/proxies/gs')).rejects.toSatisfy((err: unknown) => {
			if (!isRedirect(err)) return false;
			expect(err.status).toBe(307);
			return true;
		});
	});

	it('lets a game server through /servers untouched', async () => {
		asMock.mockResolvedValue(serverOfType('java'));
		await expect(run(serverLoad, 'gs', '/servers/gs')).resolves.toEqual({
			server: { name: 'x', serverType: 'java' }
		});
	});
});
