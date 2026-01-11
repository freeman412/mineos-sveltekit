import type { PageServerLoad } from './$types';
import * as api from '$lib/api/client';

export const load: PageServerLoad = async ({ params, fetch }) => {
	const history = await api.getPerformanceHistory(fetch, params.name, 60);
	const realtime = await api.getPerformanceRealtime(fetch, params.name);

	return {
		history,
		realtime
	};
};
