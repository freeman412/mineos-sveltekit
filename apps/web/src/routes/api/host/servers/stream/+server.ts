import { PRIVATE_API_BASE_URL, PRIVATE_API_KEY } from '$env/static/private';
import type { RequestHandler } from './$types';

const baseUrl = PRIVATE_API_BASE_URL || 'http://localhost:5078';
const apiKey = PRIVATE_API_KEY || '';

export const GET: RequestHandler = async ({ fetch, url }) => {
	if (!apiKey) {
		return new Response('Missing PRIVATE_API_KEY', { status: 500 });
	}

	const intervalMs = url.searchParams.get('intervalMs');
	const query = intervalMs ? `?intervalMs=${encodeURIComponent(intervalMs)}` : '';

	const response = await fetch(`${baseUrl}/api/v1/host/servers/stream${query}`, {
		headers: {
			'X-Api-Key': apiKey,
			Accept: 'text/event-stream'
		}
	});

	if (!response.body) {
		return new Response('Upstream did not return a body', { status: 502 });
	}

	const reader = response.body.getReader();
	const stream = new ReadableStream<Uint8Array>({
		async pull(controller) {
			try {
				const { done, value } = await reader.read();
				if (done) {
					controller.close();
					return;
				}
				if (value) {
					controller.enqueue(value);
				}
			} catch (err) {
				controller.error(err);
			}
		},
		cancel() {
			reader.releaseLock();
		}
	});

	const headers = new Headers(response.headers);
	headers.set('Content-Type', 'text/event-stream');
	headers.set('Cache-Control', 'no-cache');
	headers.set('Connection', 'keep-alive');
	headers.set('X-Accel-Buffering', 'no');
	headers.delete('Content-Length');

	return new Response(stream, {
		status: response.status,
		headers
	});
};
