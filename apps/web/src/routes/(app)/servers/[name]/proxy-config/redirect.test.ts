import { describe, expect, it } from 'vitest';
import { isRedirect } from '@sveltejs/kit';
import { load } from './+page';

/** Proxy properties moved to /proxies/<name>/proxy-config; old links must follow. */
describe('legacy proxy-config route', () => {
	it('permanently redirects to the proxy section', () => {
		try {
			(load as (e: { params: { name: string } }) => void)({ params: { name: 'hub' } });
			throw new Error('expected a redirect');
		} catch (err) {
			if (!isRedirect(err)) throw err;
			expect(err.status).toBe(308);
			expect(err.location).toBe('/proxies/hub/proxy-config');
		}
	});

	it('encodes names that need it', () => {
		try {
			(load as (e: { params: { name: string } }) => void)({ params: { name: 'my hub' } });
			throw new Error('expected a redirect');
		} catch (err) {
			if (!isRedirect(err)) throw err;
			expect(err.location).toBe('/proxies/my%20hub/proxy-config');
		}
	});
});
