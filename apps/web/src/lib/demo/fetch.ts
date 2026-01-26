import { isDemoMode } from './mode';

export type DemoFetcher = (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>;

const DEMO_PREFIX = '/demo-api';

function normalizeQuery(search: string): string {
	if (!search) return '';
	const params = new URLSearchParams(search);
	const entries = Array.from(params.entries());
	if (entries.length === 0) return '';
	return entries
		.map(([key, value]) => `${key}=${value}`)
		.join('&')
		.replace(/[^a-zA-Z0-9._-]+/g, '_');
}

export function resolveDemoPath(url: URL): string | null {
	if (!url.pathname.startsWith('/api/')) {
		return null;
	}
	const querySuffix = normalizeQuery(url.search);
	const suffix = querySuffix ? `__${querySuffix}` : '';
	return `${DEMO_PREFIX}${url.pathname}${suffix}.json`;
}

export function createDemoFetch(fetcher: DemoFetcher): DemoFetcher {
	return async (input, init) => {
		if (!isDemoMode) {
			return fetcher(input, init);
		}

		const method = init?.method?.toUpperCase() ?? 'GET';
		if (method !== 'GET') {
			return new Response(
				JSON.stringify({ demo: true, message: 'Demo mode: changes are disabled.' }),
				{ status: 200, headers: { 'Content-Type': 'application/json' } }
			);
		}

		const url =
			typeof input === 'string'
				? new URL(input, globalThis.location?.origin ?? 'http://localhost')
				: input instanceof URL
					? input
					: new URL(input.url);

		const demoPath = resolveDemoPath(url);
		if (!demoPath) {
			return fetcher(input, init);
		}

		return fetcher(demoPath, init);
	};
}
