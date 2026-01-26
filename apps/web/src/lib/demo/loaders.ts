import { isDemoMode } from './mode';
import { createDemoFetch } from './fetch';

type Fetcher = (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>;

export async function demoLoad<T>(
	fetch: Fetcher,
	loader: (demoFetch: Fetcher) => Promise<T>
): Promise<T | Record<string, never>> {
	if (!isDemoMode) {
		return {};
	}
	const demoFetch = createDemoFetch(fetch);
	return loader(demoFetch);
}
