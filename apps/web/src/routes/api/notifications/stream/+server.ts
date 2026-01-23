import type { RequestHandler } from './$types';
import { proxyEventStream } from '$lib/server/streamProxy';

export const GET: RequestHandler = (event) =>
	proxyEventStream(event, '/api/v1/notifications/stream');
