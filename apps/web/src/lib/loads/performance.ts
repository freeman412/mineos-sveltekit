import * as api from '$lib/api/client';

type LoadEvent = { params: { name: string }; fetch: typeof globalThis.fetch };

/**
 * Performance data for a server's Performance tab. Shared by /servers/[name]
 * and /proxies/[name] — both address the server by name, so the same load
 * serves both sections.
 */
export async function loadPerformance({ params, fetch }: LoadEvent) {
	const [history, realtime, spark] = await Promise.all([
		api.getPerformanceHistory(fetch, params.name, 60),
		api.getPerformanceRealtime(fetch, params.name),
		api.getSparkStatus(fetch, params.name)
	]);

	return {
		history,
		realtime,
		spark
	};
}
