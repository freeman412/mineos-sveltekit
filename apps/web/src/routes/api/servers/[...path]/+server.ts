import type { RequestHandler } from './$types';
import { createProxyHandlers } from '$lib/server/proxyApi';
import { proxyEventStream } from '$lib/server/streamProxy';

const handlers = createProxyHandlers('/api/v1/servers');

export const GET: RequestHandler = (event) => {
	const accept = event.request.headers.get('accept') ?? '';
	const path = event.params.path ?? '';
	const segments = path.split('/');
	const isClientPackageDownload =
		segments.length >= 3 &&
		segments[1] === 'client-packages' &&
		segments[segments.length - 1] === 'download';
	const raw = event.url.searchParams.get('raw') === '1';

	if (isClientPackageDownload && !raw) {
		const serverName = segments[0] ?? '';
		const filename = segments[segments.length - 2] ?? '';
		const target = `/client-packages/${encodeURIComponent(serverName)}/${encodeURIComponent(filename)}`;
		return new Response(null, {
			status: 302,
			headers: {
				Location: target
			}
		});
	}

	const isStream =
		accept.includes('text/event-stream') ||
		path.endsWith('/stream') ||
		path.endsWith('/streaming');

	if (isStream) {
		return proxyEventStream(event, `/api/v1/servers/${path}`);
	}

	return handlers.GET(event);
};

export const POST = handlers.POST;
export const PUT = handlers.PUT;
export const PATCH = handlers.PATCH;
export const DELETE = handlers.DELETE;
