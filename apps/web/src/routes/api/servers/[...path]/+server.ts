import { PRIVATE_API_BASE_URL, PRIVATE_API_KEY } from '$env/static/private';
import type { RequestHandler } from './$types';

const baseUrl = PRIVATE_API_BASE_URL || 'http://localhost:5078';
const apiKey = PRIVATE_API_KEY || '';

export const GET: RequestHandler = async ({ params, url, request, cookies }) => {
	const token = cookies.get('auth_token');
	const headers: HeadersInit = {};
	const contentType = request.headers.get('content-type');
	const accept = request.headers.get('accept') ?? '';
	const isStream = accept.includes('text/event-stream') || params.path.endsWith('/stream');
	if (contentType) {
		headers['Content-Type'] = contentType;
	}

	if (apiKey) {
		headers['X-Api-Key'] = apiKey;
	}

	if (token) {
		headers['Authorization'] = `Bearer ${token}`;
	}

	if (isStream) {
		headers['Accept'] = 'text/event-stream';
	}

	const response = await fetch(`${baseUrl}/api/v1/servers/${params.path}${url.search}`, {
		method: 'GET',
		headers
	});

	if (isStream) {
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
	}

	return new Response(response.body, {
		status: response.status,
		headers: {
			'Content-Type': response.headers.get('content-type') ?? 'application/json'
		}
	});
};

export const POST: RequestHandler = async ({ params, url, request, cookies }) => {
	const token = cookies.get('auth_token');
	const headers: HeadersInit = {};
	const contentType = request.headers.get('content-type');
	if (contentType) {
		headers['Content-Type'] = contentType;
	}

	if (apiKey) {
		headers['X-Api-Key'] = apiKey;
	}

	if (token) {
		headers['Authorization'] = `Bearer ${token}`;
	}

	const body = await request.arrayBuffer();

	const response = await fetch(`${baseUrl}/api/v1/servers/${params.path}${url.search}`, {
		method: 'POST',
		headers,
		body
	});

	return new Response(response.body, {
		status: response.status,
		headers: {
			'Content-Type': response.headers.get('content-type') ?? 'application/json'
		}
	});
};

export const PUT: RequestHandler = async ({ params, url, request, cookies }) => {
	const token = cookies.get('auth_token');
	const headers: HeadersInit = {};
	const contentType = request.headers.get('content-type');
	if (contentType) {
		headers['Content-Type'] = contentType;
	}

	if (apiKey) {
		headers['X-Api-Key'] = apiKey;
	}

	if (token) {
		headers['Authorization'] = `Bearer ${token}`;
	}

	const body = await request.arrayBuffer();

	const response = await fetch(`${baseUrl}/api/v1/servers/${params.path}${url.search}`, {
		method: 'PUT',
		headers,
		body
	});

	return new Response(response.body, {
		status: response.status,
		headers: {
			'Content-Type': response.headers.get('content-type') ?? 'application/json'
		}
	});
};

export const DELETE: RequestHandler = async ({ params, url, request, cookies }) => {
	const token = cookies.get('auth_token');
	const headers: HeadersInit = {};
	const contentType = request.headers.get('content-type');
	if (contentType) {
		headers['Content-Type'] = contentType;
	}

	if (apiKey) {
		headers['X-Api-Key'] = apiKey;
	}

	if (token) {
		headers['Authorization'] = `Bearer ${token}`;
	}

	const response = await fetch(`${baseUrl}/api/v1/servers/${params.path}${url.search}`, {
		method: 'DELETE',
		headers
	});

	return new Response(response.body, {
		status: response.status,
		headers: {
			'Content-Type': response.headers.get('content-type') ?? 'application/json'
		}
	});
};
