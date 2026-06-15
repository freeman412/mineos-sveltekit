import type { RequestHandler } from './$types';
import { proxyEventStream } from '$lib/server/streamProxy';

// Dedicated SSE route so loader-install progress streams incrementally.
// Without this, requests fall through to the buffering catch-all proxy
// (api/neoforge/[...path]) and the progress bar appears frozen until the
// install finishes.
export const GET: RequestHandler = (event) =>
	proxyEventStream(
		event,
		`/api/v1/neoforge/install/${encodeURIComponent(event.params.installId)}/stream`
	);
