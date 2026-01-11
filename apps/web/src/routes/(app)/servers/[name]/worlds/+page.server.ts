import type { PageServerLoad } from './$types';
import * as api from '$lib/api/client';

export const load: PageServerLoad = async ({ params, fetch }) => {
	const worlds = await api.getServerWorlds(fetch, params.name);

	return {
		worlds
	};
};
