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

/**
 * Generic API fetch with error handling for GET requests that return data.
 */
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

/**
 * Generic API mutation (POST/PUT/DELETE) with error handling.
 * Returns void by default, or can return data if the API returns a response body.
 */
async function apiMutate<T = void>(
	fetcher: Fetcher,
	path: string,
	method: 'POST' | 'PUT' | 'DELETE',
	body?: unknown
): Promise<ApiResult<T>> {
	try {
		const init: RequestInit = { method };
		if (body !== undefined) {
			init.headers = { 'Content-Type': 'application/json' };
			init.body = JSON.stringify(body);
		}
		const res = await fetcher(path, init);
		if (!res.ok) {
			const errorData = await res.json().catch(() => ({}));
			const errorMsg = errorData.error || `Request failed with ${res.status}`;
			return { data: null, error: errorMsg };
		}
		// Try to parse response body, return undefined if empty
		const text = await res.text();
		if (!text) {
			return { data: undefined as T, error: null };
		}
		try {
			return { data: JSON.parse(text) as T, error: null };
		} catch {
			return { data: undefined as T, error: null };
		}
	} catch (err) {
		const message = err instanceof Error ? err.message : 'Unknown error';
		return { data: null, error: message };
	}
}

/** POST request helper */
function apiPost<T = void>(fetcher: Fetcher, path: string, body?: unknown): Promise<ApiResult<T>> {
	return apiMutate<T>(fetcher, path, 'POST', body);
}

/** PUT request helper */
function apiPut<T = void>(fetcher: Fetcher, path: string, body?: unknown): Promise<ApiResult<T>> {
	return apiMutate<T>(fetcher, path, 'PUT', body);
}

/** DELETE request helper */
function apiDelete<T = void>(fetcher: Fetcher, path: string): Promise<ApiResult<T>> {
	return apiMutate<T>(fetcher, path, 'DELETE');
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

export async function getAllServers(
	fetcher: Fetcher
): Promise<ApiResult<import('./types').ServerDetail[]>> {
	return apiFetch(fetcher, '/api/servers/list');
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

export async function cloneServer(
	fetcher: Fetcher,
	name: string,
	request: import('./types').CloneServerRequest
): Promise<ApiResult<import('./types').ServerDetail>> {
	return apiPost(fetcher, `/api/servers/${name}/clone`, request);
}

export async function deleteServer(fetcher: Fetcher, name: string): Promise<ApiResult<void>> {
	return apiDelete(fetcher, `/api/servers/${name}`);
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
	return apiPost(fetcher, `/api/servers/${name}/actions/${action}`);
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
	return apiPut(fetcher, `/api/servers/${name}/server-properties`, properties);
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
	return apiPut(fetcher, `/api/servers/${name}/server-config`, config);
}

export async function acceptEula(fetcher: Fetcher, name: string): Promise<ApiResult<void>> {
	return apiPost(fetcher, `/api/servers/${name}/eula`);
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
	return apiDelete(fetcher, `/api/servers/${serverName}/worlds/${worldName}`);
}

// Player Management
export async function getServerPlayers(
	fetcher: Fetcher,
	serverName: string
): Promise<ApiResult<import('./types').PlayerSummary[]>> {
	const result = await apiFetch<{ data: import('./types').PlayerSummary[] }>(
		fetcher,
		`/api/servers/${serverName}/players`
	);
	if (result.error) {
		return { data: null, error: result.error };
	}
	return { data: result.data?.data ?? null, error: null };
}

export async function whitelistPlayer(
	fetcher: Fetcher,
	serverName: string,
	uuid: string,
	name?: string
): Promise<ApiResult<void>> {
	return apiPost(fetcher, `/api/servers/${serverName}/players/${uuid}/whitelist`, { name: name ?? null });
}

export async function removeWhitelist(
	fetcher: Fetcher,
	serverName: string,
	uuid: string
): Promise<ApiResult<void>> {
	return apiDelete(fetcher, `/api/servers/${serverName}/players/${uuid}/whitelist`);
}

export async function opPlayer(
	fetcher: Fetcher,
	serverName: string,
	uuid: string,
	payload: { name?: string; level?: number; bypassesPlayerLimit?: boolean }
): Promise<ApiResult<void>> {
	return apiPost(fetcher, `/api/servers/${serverName}/players/${uuid}/op`, payload);
}

export async function banPlayer(
	fetcher: Fetcher,
	serverName: string,
	uuid: string,
	payload: { name?: string; reason?: string; bannedBy?: string; expiresAt?: string | null }
): Promise<ApiResult<void>> {
	return apiPost(fetcher, `/api/servers/${serverName}/players/${uuid}/ban`, payload);
}

export async function getPlayerStats(
	fetcher: Fetcher,
	serverName: string,
	uuid: string
): Promise<ApiResult<import('./types').PlayerStats>> {
	const result = await apiFetch<{ data: import('./types').PlayerStats }>(
		fetcher,
		`/api/servers/${serverName}/players/${uuid}/stats`
	);
	if (result.error) {
		return { data: null, error: result.error };
	}
	return { data: result.data?.data ?? null, error: null };
}

export async function deopPlayer(
	fetcher: Fetcher,
	serverName: string,
	uuid: string
): Promise<ApiResult<void>> {
	return apiDelete(fetcher, `/api/servers/${serverName}/players/${uuid}/op`);
}

export async function unbanPlayer(
	fetcher: Fetcher,
	serverName: string,
	uuid: string
): Promise<ApiResult<void>> {
	return apiDelete(fetcher, `/api/servers/${serverName}/players/${uuid}/ban`);
}

// Mojang API
export async function lookupMojangPlayer(
	fetcher: Fetcher,
	username: string
): Promise<ApiResult<import('./types').MojangProfile>> {
	const result = await apiFetch<{ data: import('./types').MojangProfile }>(
		fetcher,
		`/api/mojang/lookup/${encodeURIComponent(username)}`
	);
	if (result.error) {
		return { data: null, error: result.error };
	}
	return { data: result.data?.data ?? null, error: null };
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

export async function getSparkStatus(
	fetcher: Fetcher,
	serverName: string
): Promise<ApiResult<import('./types').SparkStatus>> {
	return apiFetch(fetcher, `/api/servers/${serverName}/performance/spark`);
}

// Forge API
export async function getForgeVersions(
	fetcher: Fetcher
): Promise<ApiResult<import('./types').ForgeVersion[]>> {
	const result = await apiFetch<{ data: import('./types').ForgeVersion[] }>(
		fetcher,
		'/api/forge/versions'
	);
	if (result.error) {
		return { data: null, error: result.error };
	}
	return { data: result.data?.data ?? null, error: null };
}

export async function getForgeVersionsForMinecraft(
	fetcher: Fetcher,
	minecraftVersion: string
): Promise<ApiResult<import('./types').ForgeVersion[]>> {
	const result = await apiFetch<{ data: import('./types').ForgeVersion[] }>(
		fetcher,
		`/api/forge/versions/${encodeURIComponent(minecraftVersion)}`
	);
	if (result.error) {
		return { data: null, error: result.error };
	}
	return { data: result.data?.data ?? null, error: null };
}

export async function installForge(
	fetcher: Fetcher,
	minecraftVersion: string,
	forgeVersion: string,
	serverName: string
): Promise<ApiResult<import('./types').ForgeInstallResult>> {
	const result = await apiFetch<{ data: import('./types').ForgeInstallResult }>(fetcher, '/api/forge/install', {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify({ minecraftVersion, forgeVersion, serverName })
	});
	if (result.error) {
		return { data: null, error: result.error };
	}
	return { data: result.data?.data ?? null, error: null };
}

export async function getForgeInstallStatus(
	fetcher: Fetcher,
	installId: string
): Promise<ApiResult<import('./types').ForgeInstallStatus>> {
	const result = await apiFetch<{ data: import('./types').ForgeInstallStatus }>(
		fetcher,
		`/api/forge/install/${encodeURIComponent(installId)}`
	);
	if (result.error) {
		return { data: null, error: result.error };
	}
	return { data: result.data?.data ?? null, error: null };
}

// Watchdog / Crash Detection
export async function getServerCrashEvents(
	fetcher: Fetcher,
	serverName: string,
	limit?: number
): Promise<ApiResult<import('./types').CrashEvent[]>> {
	const params = limit ? `?limit=${limit}` : '';
	return apiFetch(fetcher, `/api/servers/${serverName}/crashes${params}`);
}

export async function clearServerCrashHistory(
	fetcher: Fetcher,
	serverName: string
): Promise<ApiResult<void>> {
	return apiDelete(fetcher, `/api/servers/${serverName}/crashes`);
}

export async function getServerWatchdogStatus(
	fetcher: Fetcher,
	serverName: string
): Promise<ApiResult<import('./types').WatchdogStatus>> {
	return apiFetch(fetcher, `/api/servers/${serverName}/watchdog`);
}

export async function getAllCrashEvents(
	fetcher: Fetcher,
	limit?: number
): Promise<ApiResult<import('./types').CrashEvent[]>> {
	const params = limit ? `?limit=${limit}` : '';
	return apiFetch(fetcher, `/api/watchdog/crashes${params}`);
}

export async function getAllWatchdogStatus(
	fetcher: Fetcher
): Promise<ApiResult<Record<string, import('./types').WatchdogStatus>>> {
	return apiFetch(fetcher, `/api/watchdog/status`);
}

// Modpack Installation
export async function installModpack(
	fetcher: Fetcher,
	serverName: string,
	modpackId: number,
	modpackName: string,
	fileId?: number,
	modpackVersion?: string,
	logoUrl?: string
): Promise<ApiResult<{ jobId: string; message: string }>> {
	return apiPost(fetcher, `/api/servers/${serverName}/modpacks/install-enhanced`, {
		modpackId,
		fileId: fileId ?? null,
		modpackName,
		modpackVersion: modpackVersion ?? null,
		logoUrl: logoUrl ?? null
	});
}

export function streamModpackInstall(
	serverName: string,
	jobId: string,
	onProgress: (progress: import('./types').ModpackInstallProgress) => void,
	onError: (error: string) => void,
	onComplete: () => void
): () => void {
	const eventSource = new EventSource(`/api/servers/${serverName}/modpacks/install/${jobId}/stream`);

	eventSource.onmessage = (event) => {
		try {
			const progress = JSON.parse(event.data) as import('./types').ModpackInstallProgress;
			onProgress(progress);

			if (progress.status === 'completed' || progress.status === 'failed') {
				eventSource.close();
				if (progress.status === 'completed') {
					onComplete();
				} else {
					onError(progress.error || 'Installation failed');
				}
			}
		} catch (err) {
			console.error('Failed to parse modpack progress:', err);
		}
	};

	eventSource.onerror = () => {
		eventSource.close();
		onError('Connection to server lost');
	};

	return () => eventSource.close();
}

// Player Activity
export async function getRecentActivity(
	fetcher: Fetcher,
	serverName: string,
	limit?: number
): Promise<ApiResult<import('./types').PlayerActivityEvent[]>> {
	const params = limit ? `?limit=${limit}` : '';
	const result = await apiFetch<{ data: import('./types').PlayerActivityEvent[] }>(
		fetcher,
		`/api/servers/${serverName}/players/activity${params}`
	);
	if (result.error) {
		return { data: null, error: result.error };
	}
	return { data: result.data?.data ?? null, error: null };
}

export async function getRecentSessions(
	fetcher: Fetcher,
	serverName: string,
	limit?: number
): Promise<ApiResult<import('./types').PlayerSession[]>> {
	const params = limit ? `?limit=${limit}` : '';
	const result = await apiFetch<{ data: import('./types').PlayerSession[] }>(
		fetcher,
		`/api/servers/${serverName}/players/sessions${params}`
	);
	if (result.error) {
		return { data: null, error: result.error };
	}
	return { data: result.data?.data ?? null, error: null };
}

export async function getPlayerActivity(
	fetcher: Fetcher,
	serverName: string,
	uuid: string,
	limit?: number
): Promise<ApiResult<import('./types').PlayerActivityEvent[]>> {
	const params = limit ? `?limit=${limit}` : '';
	const result = await apiFetch<{ data: import('./types').PlayerActivityEvent[] }>(
		fetcher,
		`/api/servers/${serverName}/players/${uuid}/activity${params}`
	);
	if (result.error) {
		return { data: null, error: result.error };
	}
	return { data: result.data?.data ?? null, error: null };
}

export async function getPlayerSessions(
	fetcher: Fetcher,
	serverName: string,
	uuid: string,
	limit?: number
): Promise<ApiResult<import('./types').PlayerSession[]>> {
	const params = limit ? `?limit=${limit}` : '';
	const result = await apiFetch<{ data: import('./types').PlayerSession[] }>(
		fetcher,
		`/api/servers/${serverName}/players/${uuid}/sessions${params}`
	);
	if (result.error) {
		return { data: null, error: result.error };
	}
	return { data: result.data?.data ?? null, error: null };
}

export async function getPlayerActivityStats(
	fetcher: Fetcher,
	serverName: string,
	uuid: string
): Promise<ApiResult<import('./types').PlayerActivityStats>> {
	const result = await apiFetch<{ data: import('./types').PlayerActivityStats }>(
		fetcher,
		`/api/servers/${serverName}/players/${uuid}/activity-stats`
	);
	if (result.error) {
		return { data: null, error: result.error };
	}
	return { data: result.data?.data ?? null, error: null };
}
