import type {
	ApiResult,
	HostMetrics,
	ServerSummary,
	Profile,
	ArchiveEntry,
	CurseForgeSearchResult,
	CurseForgeMod
} from './types';

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

export function getHostProfiles(fetcher: Fetcher) {
	return apiFetch<Profile[]>(fetcher, '/api/host/profiles');
}

export function getHostImports(fetcher: Fetcher) {
	return apiFetch<ArchiveEntry[]>(fetcher, '/api/host/imports');
}

export function searchCurseForge(fetcher: Fetcher, query: string, classId?: number) {
	const params = new URLSearchParams({ query });
	if (classId) {
		params.set('classId', String(classId));
	}
	return apiFetch<CurseForgeSearchResult>(fetcher, `/api/curseforge/search?${params.toString()}`);
}

export function getCurseForgeMod(fetcher: Fetcher, id: number) {
	return apiFetch<CurseForgeMod>(fetcher, `/api/curseforge/mod/${id}`);
}

// Aliases for consistency
export const listServers = getHostServers;
export const listProfiles = getHostProfiles;

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

export async function acceptEula(fetcher: Fetcher, name: string): Promise<ApiResult<void>> {
	try {
		const res = await fetcher(`/api/servers/${name}/eula`, { method: 'POST' });
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

// World Management
export async function getServerWorlds(
	fetcher: Fetcher,
	serverName: string
): Promise<ApiResult<import('./types').World[]>> {
	return apiFetch(fetcher, `/api/servers/${serverName}/worlds`);
}

export async function getWorldInfo(
	fetcher: Fetcher,
	serverName: string,
	worldName: string
): Promise<ApiResult<import('./types').WorldInfo>> {
	return apiFetch(fetcher, `/api/servers/${serverName}/worlds/${worldName}`);
}

export async function downloadWorld(
	fetcher: Fetcher,
	serverName: string,
	worldName: string
): Promise<void> {
	const res = await fetcher(`/api/servers/${serverName}/worlds/${worldName}/download`);
	if (!res.ok) {
		throw new Error(`Failed to download world: ${res.statusText}`);
	}
	const blob = await res.blob();
	const url = URL.createObjectURL(blob);
	const a = document.createElement('a');
	a.href = url;
	a.download = `${serverName}-${worldName}.zip`;
	a.click();
	URL.revokeObjectURL(url);
}

export async function deleteWorld(
	fetcher: Fetcher,
	serverName: string,
	worldName: string
): Promise<ApiResult<void>> {
	try {
		const res = await fetcher(`/api/servers/${serverName}/worlds/${worldName}`, {
			method: 'DELETE'
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

// Player Management
export async function getServerPlayers(
	fetcher: Fetcher,
	serverName: string
): Promise<ApiResult<import('./types').PlayerSummary[]>> {
	return apiFetch(fetcher, `/api/servers/${serverName}/players`);
}

export async function whitelistPlayer(
	fetcher: Fetcher,
	serverName: string,
	uuid: string,
	name?: string
): Promise<ApiResult<void>> {
	try {
		const res = await fetcher(`/api/servers/${serverName}/players/${uuid}/whitelist`, {
			method: 'POST',
			headers: { 'Content-Type': 'application/json' },
			body: JSON.stringify({ name: name ?? null })
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

export async function removeWhitelist(
	fetcher: Fetcher,
	serverName: string,
	uuid: string
): Promise<ApiResult<void>> {
	try {
		const res = await fetcher(`/api/servers/${serverName}/players/${uuid}/whitelist`, {
			method: 'DELETE'
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

export async function opPlayer(
	fetcher: Fetcher,
	serverName: string,
	uuid: string,
	payload: { name?: string; level?: number; bypassesPlayerLimit?: boolean }
): Promise<ApiResult<void>> {
	try {
		const res = await fetcher(`/api/servers/${serverName}/players/${uuid}/op`, {
			method: 'POST',
			headers: { 'Content-Type': 'application/json' },
			body: JSON.stringify(payload)
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

export async function banPlayer(
	fetcher: Fetcher,
	serverName: string,
	uuid: string,
	payload: { name?: string; reason?: string; bannedBy?: string; expiresAt?: string | null }
): Promise<ApiResult<void>> {
	try {
		const res = await fetcher(`/api/servers/${serverName}/players/${uuid}/ban`, {
			method: 'POST',
			headers: { 'Content-Type': 'application/json' },
			body: JSON.stringify(payload)
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

export async function getPlayerStats(
	fetcher: Fetcher,
	serverName: string,
	uuid: string
): Promise<ApiResult<import('./types').PlayerStats>> {
	return apiFetch(fetcher, `/api/servers/${serverName}/players/${uuid}/stats`);
}

// Performance Monitoring
export async function getPerformanceRealtime(
	fetcher: Fetcher,
	serverName: string
): Promise<ApiResult<import('./types').PerformanceSample>> {
	return apiFetch(fetcher, `/api/servers/${serverName}/performance/realtime`);
}

export async function getPerformanceHistory(
	fetcher: Fetcher,
	serverName: string,
	minutes = 60
): Promise<ApiResult<import('./types').PerformanceSample[]>> {
	const params = new URLSearchParams({ minutes: String(minutes) });
	return apiFetch(fetcher, `/api/servers/${serverName}/performance/history?${params.toString()}`);
}
