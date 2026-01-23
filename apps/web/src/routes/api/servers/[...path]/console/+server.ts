import type { RequestHandler } from './$types';
import { proxyJson } from '$lib/server/proxyJson';
import { proxyEventStream } from '$lib/server/streamProxy';

export const POST: RequestHandler = (event) =>
	proxyJson(
		event,
		`/api/v1/servers/${encodeURIComponent(event.params.path)}/console`,
		{ method: 'POST' }
	);

export const GET: RequestHandler = (event) =>
	proxyEventStream(
		event,
		`/api/v1/servers/${encodeURIComponent(event.params.path)}/console/stream`
	);

export const DELETE: RequestHandler = (event) =>
	proxyJson(
		event,
		`/api/v1/servers/${encodeURIComponent(event.params.path)}/console`,
		{ method: 'DELETE' }
	);
