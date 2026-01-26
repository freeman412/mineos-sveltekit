import type { PageLoad } from './$types';
import * as api from '$lib/api/client';
import { demoLoad } from '$lib/demo/loaders';

export const load: PageLoad = ({ fetch }) =>
	demoLoad(fetch, async (demoFetch) => {
		const [servers, hostMetrics] = await Promise.all([
			api.listServers(demoFetch),
			api.getHostMetrics(demoFetch)
		]);

		return {
			servers,
			hostMetrics
		};
	});
