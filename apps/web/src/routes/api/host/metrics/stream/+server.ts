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

	const response = await fetch(`${baseUrl}/api/v1/host/metrics/stream${query}`, {
		headers: {
			'X-Api-Key': apiKey,
			Accept: 'text/event-stream'
		}
	});

	return new Response(response.body, {
		status: response.status,
		headers: {
			'Content-Type': 'text/event-stream',
			'Cache-Control': 'no-cache'
		}
	});
};
