import type { PageServerLoad } from './$types';
import * as api from '$lib/api/client';

export const load: PageServerLoad = async ({ params, fetch, parent }) => {
	const { server } = await parent();
	const worlds = await api.getServerWorlds(fetch, params.name);
	const serverProperties = await api.getServerProperties(fetch, params.name);

	return {
		server,
		worlds,
		serverProperties
	};
};
