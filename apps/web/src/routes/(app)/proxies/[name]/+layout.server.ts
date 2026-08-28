import type { LayoutServerLoad } from './$types';
import { getServer } from '$lib/api/client';
import { error, redirect } from '@sveltejs/kit';

export const load: LayoutServerLoad = async ({ params, fetch }) => {
	const result = await getServer(fetch, params.name);

	if (result.error) {
		throw error(404, result.error);
	}

	// This section is for proxies. A game server reached through it belongs in
	// /servers, and sending it there beats rendering proxy tabs over it.
	// 307 for the same reason as the mirror guard in /servers: this depends on
	// serverType, which can change under us.
	if (result.data?.serverType !== 'proxy') {
		redirect(307, `/servers/${encodeURIComponent(params.name)}`);
	}

	return {
		server: result.data
	};
};
