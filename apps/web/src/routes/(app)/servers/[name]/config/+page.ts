import type { PageLoad } from './$types';
import { getServerProperties } from '$lib/api/client';
import { demoLoad } from '$lib/demo/loaders';

export const load: PageLoad = ({ fetch, params }) =>
	demoLoad(fetch, async (demoFetch) => {
		const properties = await getServerProperties(demoFetch, params.name);
		return {
			properties,
			serverName: params.name
		};
	});
