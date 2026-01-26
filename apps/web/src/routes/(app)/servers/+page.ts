import type { PageLoad } from './$types';
import { getHostImports, getHostServers } from '$lib/api/client';
import { demoLoad } from '$lib/demo/loaders';

export const load: PageLoad = ({ fetch }) =>
	demoLoad(fetch, async (demoFetch) => {
		const [servers, imports] = await Promise.all([
			getHostServers(demoFetch),
			getHostImports(demoFetch)
		]);
		return { servers, imports };
	});
