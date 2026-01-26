import type { PageLoad } from './$types';
import { getHostProfiles, getServerConfig } from '$lib/api/client';
import { demoLoad } from '$lib/demo/loaders';

export const load: PageLoad = ({ fetch, params }) =>
	demoLoad(fetch, async (demoFetch) => {
		const config = await getServerConfig(demoFetch, params.name);
		const profiles = await getHostProfiles(demoFetch);

		return {
			config,
			profiles,
			serverName: params.name,
			jarFiles: { data: [], error: null },
			forgeArgFiles: { data: [], error: null }
		};
	});
