import type { RequestHandler } from './$types';

const API_BASE_URL = 'http://127.0.0.1:5078/api/v1';

export const POST: RequestHandler = async ({ request, cookies }) => {
	const token = cookies.get('auth_token');
	const headers: HeadersInit = {
		'Content-Type': 'application/json'
	};

	if (token) {
		headers['Authorization'] = `Bearer ${token}`;
	}

	const body = await request.text();

	const response = await fetch(`${API_BASE_URL}/servers`, {
		method: 'POST',
		headers,
		body
	});

	const data = await response.text();
	return new Response(data, {
		status: response.status,
		headers: { 'Content-Type': 'application/json' }
	});
};
