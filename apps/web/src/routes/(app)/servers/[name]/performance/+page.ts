import type { PageLoad } from './$types';
import * as api from '$lib/api/client';
import { demoLoad } from '$lib/demo/loaders';

export const load: PageLoad = ({ fetch, params }) =>
	demoLoad(fetch, async (demoFetch) => {
		const [history, realtime, spark] = await Promise.all([
			api.getPerformanceHistory(demoFetch, params.name, 60),
			api.getPerformanceRealtime(demoFetch, params.name),
			api.getSparkStatus(demoFetch, params.name)
		]);

		return { history, realtime, spark };
	});
