import type { ApiResult, HostMetrics, ServerSummary } from './types';

type Fetcher = (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>;

async function apiFetch<T>(fetcher: Fetcher, path: string, init?: RequestInit): Promise<ApiResult<T>> {
	try {
		const res = await fetcher(path, init);
		if (!res.ok) {
			const errorData = await res.json().catch(() => ({}));
			const errorMsg = errorData.error || `Request failed with ${res.status}`;
			return { data: null, error: errorMsg };
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

// Server management operations
export async function getServer(
	fetcher: Fetcher,
	name: string
): Promise<ApiResult<import('./types').ServerDetail>> {
	return apiFetch(fetcher, `/api/servers/${name}`);
}

export async function createServer(
	fetcher: Fetcher,
	request: import('./types').CreateServerRequest
): Promise<ApiResult<import('./types').ServerDetail>> {
	return apiFetch(fetcher, '/api/servers', {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(request)
	});
}

export async function deleteServer(fetcher: Fetcher, name: string): Promise<ApiResult<void>> {
	try {
		const res = await fetcher(`/api/servers/${name}`, { method: 'DELETE' });
		if (!res.ok) {
			const errorData = await res.json().catch(() => ({}));
			const errorMsg = errorData.error || `Request failed with ${res.status}`;
			return { data: null, error: errorMsg };
		}
		return { data: undefined, error: null };
	} catch (err) {
		const message = err instanceof Error ? err.message : 'Unknown error';
		return { data: null, error: message };
	}
}

export async function getServerStatus(
	fetcher: Fetcher,
	name: string
): Promise<ApiResult<import('./types').ServerHeartbeat>> {
	return apiFetch(fetcher, `/api/servers/${name}/status`);
}

export async function startServer(fetcher: Fetcher, name: string): Promise<ApiResult<void>> {
	return performServerAction(fetcher, name, 'start');
}

export async function stopServer(fetcher: Fetcher, name: string): Promise<ApiResult<void>> {
	return performServerAction(fetcher, name, 'stop');
}

export async function restartServer(fetcher: Fetcher, name: string): Promise<ApiResult<void>> {
	return performServerAction(fetcher, name, 'restart');
}

export async function killServer(fetcher: Fetcher, name: string): Promise<ApiResult<void>> {
	return performServerAction(fetcher, name, 'kill');
}

async function performServerAction(
	fetcher: Fetcher,
	name: string,
	action: string
): Promise<ApiResult<void>> {
	try {
		const res = await fetcher(`/api/servers/${name}/actions/${action}`, { method: 'POST' });
		if (!res.ok) {
			const errorData = await res.json().catch(() => ({}));
			const errorMsg = errorData.error || `Request failed with ${res.status}`;
			return { data: null, error: errorMsg };
		}
		return { data: undefined, error: null };
	} catch (err) {
		const message = err instanceof Error ? err.message : 'Unknown error';
		return { data: null, error: message };
	}
}

export async function getServerProperties(
	fetcher: Fetcher,
	name: string
): Promise<ApiResult<Record<string, string>>> {
	return apiFetch(fetcher, `/api/servers/${name}/server-properties`);
}

export async function updateServerProperties(
	fetcher: Fetcher,
	name: string,
	properties: Record<string, string>
): Promise<ApiResult<void>> {
	try {
		const res = await fetcher(`/api/servers/${name}/server-properties`, {
			method: 'PUT',
			headers: { 'Content-Type': 'application/json' },
			body: JSON.stringify(properties)
		});
		if (!res.ok) {
			const errorData = await res.json().catch(() => ({}));
			const errorMsg = errorData.error || `Request failed with ${res.status}`;
			return { data: null, error: errorMsg };
		}
		return { data: undefined, error: null };
	} catch (err) {
		const message = err instanceof Error ? err.message : 'Unknown error';
		return { data: null, error: message };
	}
}

export async function getServerConfig(
	fetcher: Fetcher,
	name: string
): Promise<ApiResult<import('./types').ServerConfig>> {
	return apiFetch(fetcher, `/api/servers/${name}/server-config`);
}

export async function updateServerConfig(
	fetcher: Fetcher,
	name: string,
	config: import('./types').ServerConfig
): Promise<ApiResult<void>> {
	try {
		const res = await fetcher(`/api/servers/${name}/server-config`, {
			method: 'PUT',
			headers: { 'Content-Type': 'application/json' },
			body: JSON.stringify(config)
		});
		if (!res.ok) {
			const errorData = await res.json().catch(() => ({}));
			const errorMsg = errorData.error || `Request failed with ${res.status}`;
			return { data: null, error: errorMsg };
		}
		return { data: undefined, error: null };
	} catch (err) {
		const message = err instanceof Error ? err.message : 'Unknown error';
		return { data: null, error: message };
	}
}
