import { PRIVATE_API_BASE_URL, PRIVATE_API_KEY } from '$env/static/private';
import type { RequestHandler } from './$types';

const baseUrl = PRIVATE_API_BASE_URL || 'http://localhost:5078';
const apiKey = PRIVATE_API_KEY || '';

export const GET: RequestHandler = async ({ params, cookies, url }) => {
	const token = cookies.get('auth_token');
	const headers: HeadersInit = {
		Accept: 'text/event-stream'
	};

	if (apiKey) {
		headers['X-Api-Key'] = apiKey;
	}

	if (token) {
		headers['Authorization'] = `Bearer ${token}`;
	}

	const response = await fetch(
		`${baseUrl}/api/v1/servers/${params.name}/performance/stream${url.search}`,
		{ headers }
	);

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

	const responseHeaders = new Headers(response.headers);
	responseHeaders.set('Content-Type', 'text/event-stream');
	responseHeaders.set('Cache-Control', 'no-cache');
	responseHeaders.set('Connection', 'keep-alive');
	responseHeaders.set('X-Accel-Buffering', 'no');
	responseHeaders.delete('Content-Length');

	return new Response(stream, {
		status: response.status,
		headers: responseHeaders
	});
};
