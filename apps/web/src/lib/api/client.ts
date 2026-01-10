import type { ApiResult, HostMetrics, ServerSummary } from './types';

type Fetcher = (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>;

async function apiFetch<T>(fetcher: Fetcher, path: string, init?: RequestInit): Promise<ApiResult<T>> {
	try {
		const res = await fetcher(path, init);
		if (!res.ok) {
			return { data: null, error: `Request failed with ${res.status}` };
		}
		const data = (await res.json()) as T;
		return { data, error: null };
	} catch (err) {
		const message = err instanceof Error ? err.message : 'Unknown error';
		return { data: null, error: message };
	}
}

export function getHostMetrics(fetcher: Fetcher) {
	return apiFetch<HostMetrics>(fetcher, '/api/host/metrics');
}

export function getHostServers(fetcher: Fetcher) {
	return apiFetch<ServerSummary[]>(fetcher, '/api/host/servers');
}
