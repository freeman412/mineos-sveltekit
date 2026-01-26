import type { LayoutLoad } from './$types';
import * as api from '$lib/api/client';
import { isDemoMode } from '$lib/demo/mode';
import { createDemoFetch } from '$lib/demo/fetch';

export const load: LayoutLoad = async ({ fetch }) => {
	if (!isDemoMode) {
		return {};
	}

	const demoFetch = createDemoFetch(fetch);
	const [servers, profiles] = await Promise.all([
		api.getAllServers(demoFetch),
		api.getHostProfiles(demoFetch)
	]);

	return {
		user: { id: 'demo', username: 'demo', role: 'admin' },
		servers: servers.data ?? [],
		profiles: profiles.data ?? []
	};
};
