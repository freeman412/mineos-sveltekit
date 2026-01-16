import type { RequestHandler } from './$types';
import { proxyJson } from '$lib/server/proxyJson';

export const DELETE: RequestHandler = (event) =>
	proxyJson(
		event,
		`/api/v1/servers/${encodeURIComponent(event.params.name)}/modpacks/${encodeURIComponent(event.params.modpackId)}`,
		{ method: 'DELETE' }
	);
