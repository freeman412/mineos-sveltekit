import type { PageLoad } from './$types';
import * as api from '$lib/api/client';
import { demoLoad } from '$lib/demo/loaders';

export const load: PageLoad = ({ fetch, params, parent }) =>
	demoLoad(fetch, async (demoFetch) => {
		const { server } = await parent();
		const worlds = await api.getServerWorlds(demoFetch, params.name);
		const serverProperties = await api.getServerProperties(demoFetch, params.name);

		return {
			server,
			worlds,
			serverProperties
		};
	});
