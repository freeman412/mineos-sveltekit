import type { RequestHandler } from './$types';
import { proxyJson } from '$lib/server/proxyJson';

export const POST: RequestHandler = (event) =>
	proxyJson(
		event,
		`/api/v1/servers/${encodeURIComponent(event.params.name)}/modpacks/install`,
		{ method: 'POST' }
	);
