import type { LayoutServerLoad } from './$types';
import { getServer } from '$lib/api/client';
import { error } from '@sveltejs/kit';

export const load: LayoutServerLoad = async ({ params, fetch }) => {
	const result = await getServer(fetch, params.name);

	if (result.error) {
		throw error(404, result.error);
	}

	return {
		server: result.data
	};
};
