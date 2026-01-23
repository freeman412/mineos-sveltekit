import type { PageServerLoad } from './$types';
import * as api from '$lib/api/client';

export const load: PageServerLoad = async ({ params, fetch }) => {
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
};
