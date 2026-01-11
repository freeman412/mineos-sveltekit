import type { PageServerLoad } from './$types';
import * as api from '$lib/api/client';

export const load: PageServerLoad = async ({ params, fetch }) => {
	const players = await api.getServerPlayers(fetch, params.name);

	return {
		players
	};
};
