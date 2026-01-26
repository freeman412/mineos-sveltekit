import type { PageLoad } from './$types';
import * as api from '$lib/api/client';
import { demoLoad } from '$lib/demo/loaders';

export const load: PageLoad = ({ fetch, params }) =>
	demoLoad(fetch, async (demoFetch) => {
		const players = await api.getServerPlayers(demoFetch, params.name);
		return { players };
	});
