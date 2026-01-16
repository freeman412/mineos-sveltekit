import type { RequestHandler } from './$types';
import { proxyJson } from '$lib/server/proxyJson';

export const GET: RequestHandler = (event) =>
	proxyJson(
		event,
		`/api/v1/servers/${encodeURIComponent(event.params.name)}/modpacks`
	);

export const POST: RequestHandler = (event) =>
	proxyJson(
		event,
		`/api/v1/servers/${encodeURIComponent(event.params.name)}/modpacks/install-enhanced`,
		{ method: 'POST' }
	);
