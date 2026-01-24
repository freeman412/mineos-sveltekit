// src/lib/server/streamProxy.ts
import { env } from '$env/dynamic/private';
import type { RequestEvent } from '@sveltejs/kit';

const baseUrl =
	env.PRIVATE_API_BASE_URL ??
	env.INTERNAL_API_URL ??
	'http://api:5078';

const apiKey = env.PRIVATE_API_KEY ?? '';

type StreamProxyOptions = {
	requireApiKey?: boolean;
	fallbackPath?: string;
};

export async function proxyEventStream(
	event: RequestEvent,
	upstreamPath: string,
	options: StreamProxyOptions = {}
) {
	const requireApiKey = options.requireApiKey ?? true;
	if (requireApiKey && !apiKey) {
		return new Response('Missing PRIVATE_API_KEY', { status: 500 });
	}

	const url = `${baseUrl}${upstreamPath}${event.url.search}`;
	const token = event.cookies.get('auth_token');

	const requestHeaders: Record<string, string> = {
		Accept: 'text/event-stream'
	};
	if (apiKey) requestHeaders['X-Api-Key'] = apiKey;
	if (token) requestHeaders['Authorization'] = `Bearer ${token}`;

	const abortController = new AbortController();
	if (event.request.signal) {
		if (event.request.signal.aborted) {
			abortController.abort();
		} else {
			event.request.signal.addEventListener('abort', () => abortController.abort());
		}
	}

	let response: Response;
	try {
		response = await fetch(url, { headers: requestHeaders, signal: abortController.signal });
	} catch (error) {
		if (abortController.signal.aborted) {
			return new Response(null, { status: 204 });
		}
		return new Response('Upstream stream unavailable', { status: 502 });
	}
	if (response.status === 404 && options.fallbackPath) {
		response.body?.cancel();
		const fallbackUrl = `${baseUrl}${options.fallbackPath}${event.url.search}`;
		try {
			response = await fetch(fallbackUrl, { headers: requestHeaders, signal: abortController.signal });
		} catch (error) {
			if (abortController.signal.aborted) {
				return new Response(null, { status: 204 });
			}
			return new Response('Upstream stream unavailable', { status: 502 });
		}
	}

	if (!response.body) {
		return new Response('Upstream did not return a body', { status: 502 });
	}

	// Pipe the stream directly
	const reader = response.body.getReader();

	const stream = new ReadableStream<Uint8Array>({
		async pull(controller) {
			const { done, value } = await reader.read();
			if (done) {
				controller.close();
				return;
			}
			if (value) controller.enqueue(value);
		},
		cancel() {
			abortController.abort();
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
}
