import type { PageLoad } from './$types';
import { getHostProfiles, getHostServers } from '$lib/api/client';
import { demoLoad } from '$lib/demo/loaders';

export const load: PageLoad = ({ fetch }) =>
	demoLoad(fetch, async (demoFetch) => {
		const profiles = await getHostProfiles(demoFetch);
		const servers = await getHostServers(demoFetch);
		return { profiles, servers };
	});
