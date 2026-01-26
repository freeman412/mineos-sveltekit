import type { PageLoad } from './$types';
import * as api from '$lib/api/client';
import { demoLoad } from '$lib/demo/loaders';

export const load: PageLoad = ({ fetch, url }) =>
	demoLoad(fetch, async (demoFetch) => {
		const [servers, profiles] = await Promise.all([
			api.listServers(demoFetch),
			api.listProfiles(demoFetch)
		]);

		return {
			servers,
			profiles,
			query: url.searchParams.get('q') ?? ''
		};
	});
