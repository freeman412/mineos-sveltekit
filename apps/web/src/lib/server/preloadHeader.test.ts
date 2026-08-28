import { describe, expect, it } from 'vitest';
import { stripPreloadHeader } from './preloadHeader';

/** A page response carrying the preload header SvelteKit emits by default. */
function pageResponse(link: string) {
	return new Response('<html></html>', {
		headers: { 'content-type': 'text/html', link }
	});
}

/** What a proxy has to buffer: the response header block, roughly. */
function headerBytes(response: Response): number {
	return [...response.headers].reduce((n, [k, v]) => n + k.length + v.length + 4, 0);
}

describe('stripPreloadHeader', () => {
	it('drops a preload header too big for a default proxy buffer', () => {
		// The real header lists every css/js chunk on the page. On the proxy
		// config page it exceeded nginx's 4 KB proxy_buffer_size, so nginx
		// discarded the response and served a 502 instead of the page.
		const link = Array.from(
			{ length: 70 },
			(_, i) => `</_app/immutable/chunks/chunk-${i}.js>; rel="modulepreload"; nopush`
		).join(', ');
		const before = pageResponse(link);
		expect(headerBytes(before)).toBeGreaterThan(4096);

		const after = stripPreloadHeader(before);

		expect(after.headers.get('link')).toBeNull();
		expect(headerBytes(after)).toBeLessThan(4096);
	});

	it('leaves the body and the other headers alone', async () => {
		const res = stripPreloadHeader(pageResponse('</a.css>; rel="preload"; as="style"'));

		expect(res.status).toBe(200);
		expect(res.headers.get('content-type')).toBe('text/html');
		expect(await res.text()).toBe('<html></html>');
	});

	it('is a no-op on a response that has no preload header', () => {
		const res = stripPreloadHeader(new Response(null, { status: 303 }));

		expect(res.status).toBe(303);
		expect(res.headers.get('link')).toBeNull();
	});
});
