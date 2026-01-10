import { PRIVATE_API_BASE_URL, PRIVATE_API_KEY } from '$env/static/private';
import type { RequestHandler } from './$types';

const baseUrl = PRIVATE_API_BASE_URL || 'http://localhost:5078';
const apiKey = PRIVATE_API_KEY || '';

export const GET: RequestHandler = async ({ fetch }) => {
	if (!apiKey) {
		return new Response('Missing PRIVATE_API_KEY', { status: 500 });
	}

	const response = await fetch(`${baseUrl}/api/v1/host/metrics`, {
		headers: {
			'X-Api-Key': apiKey
		}
	});

	return new Response(await response.text(), {
		status: response.status,
		headers: {
			'Content-Type': response.headers.get('content-type') ?? 'application/json'
		}
	});
};
