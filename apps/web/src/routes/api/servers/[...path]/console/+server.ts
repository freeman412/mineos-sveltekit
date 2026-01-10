import type { RequestHandler } from './$types';

const API_BASE_URL = 'http://localhost:5078/api/v1';

export const POST: RequestHandler = async ({ request, cookies, params }) => {
	const token = cookies.get('auth_token');
	const headers: HeadersInit = {
		'Content-Type': 'application/json'
	};

	if (token) {
		headers['Authorization'] = `Bearer ${token}`;
	}

	const body = await request.text();

	const response = await fetch(`${API_BASE_URL}/servers/${params.path}/console`, {
		method: 'POST',
		headers,
		body
	});

	const data = await response.json();
	return new Response(JSON.stringify(data), {
		status: response.status,
		headers: { 'Content-Type': 'application/json' }
	});
};

export const GET: RequestHandler = async ({ cookies, params }) => {
	const token = cookies.get('auth_token');
	const headers: HeadersInit = {};

	if (token) {
		headers['Authorization'] = `Bearer ${token}`;
	}

	// Stream SSE from backend
	const response = await fetch(`${API_BASE_URL}/servers/${params.path}/console/stream`, {
		headers
	});

	return new Response(response.body, {
		headers: {
			'Content-Type': 'text/event-stream',
			'Cache-Control': 'no-cache',
			'Connection': 'keep-alive'
		}
	});
};
