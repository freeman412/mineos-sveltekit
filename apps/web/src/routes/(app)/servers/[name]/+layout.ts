import type { LayoutLoad } from './$types';
import { getServer } from '$lib/api/client';
import { demoLoad } from '$lib/demo/loaders';
import { error } from '@sveltejs/kit';

export const load: LayoutLoad = ({ fetch, params }) =>
	demoLoad(fetch, async (demoFetch) => {
		const result = await getServer(demoFetch, params.name);
		if (result.error) {
			throw error(404, result.error);
		}
		return { server: result.data };
	});
