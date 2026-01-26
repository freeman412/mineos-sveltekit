import type { PageLoad } from './$types';
import * as api from '$lib/api/client';
import { demoLoad } from '$lib/demo/loaders';

export const load: PageLoad = ({ fetch }) =>
	demoLoad(fetch, async (demoFetch) => {
		const profiles = await api.listProfiles(demoFetch);
		const servers = await api.getAllServers(demoFetch);
		return { profiles, servers };
	});
