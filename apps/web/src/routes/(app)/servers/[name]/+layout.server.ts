import type { LayoutServerLoad } from './$types';
import { getServer } from '$lib/api/client';
import { proxyDetailPath } from '$lib/utils/proxy';
import { error, redirect } from '@sveltejs/kit';

export const load: LayoutServerLoad = async ({ params, fetch, url }) => {
	const result = await getServer(fetch, params.name);

	if (result.error) {
		throw error(404, result.error);
	}

	// #176 moved proxies to their own section but left this page behind it, so
	// a proxy's console, files and backups were reachable only by guessing the
	// URL. Anything landing here for a proxy — an old bookmark, a stale link —
	// goes to the matching tab under /proxies.
	// 307, not 308: serverType is mutable (Config -> Change Server Type can turn a
	// proxy into a game server and back), and a browser caches a permanent redirect
	// forever — which would strand /servers/<name> on the proxy section after the
	// conversion.
	if (result.data?.serverType === 'proxy') {
		redirect(307, proxyDetailPath(params.name, url.pathname));
	}

	return {
		server: result.data
	};
};
