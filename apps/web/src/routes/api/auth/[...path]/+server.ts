import { PRIVATE_API_BASE_URL, PRIVATE_API_KEY } from '$env/static/private';
import type { RequestHandler } from './$types';

const baseUrl = PRIVATE_API_BASE_URL || 'http://localhost:5078';
const apiKey = PRIVATE_API_KEY || '';

const buildHeaders = (request: Request, token?: string | null) => {
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

	return headers;
};

const forward = async (method: string, params: { params: { path: string }; url: URL; request: Request; cookies: any }) => {
	if (!apiKey) {
		return new Response('Missing PRIVATE_API_KEY', { status: 500 });
	}

	const token = params.cookies.get('auth_token');
	const headers = buildHeaders(params.request, token);
	const body = method === 'GET' || method === 'DELETE' ? undefined : await params.request.arrayBuffer();

	const response = await fetch(`${baseUrl}/api/v1/auth/${params.params.path}${params.url.search}`, {
		method,
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

export const GET: RequestHandler = (event) => forward('GET', event);
export const POST: RequestHandler = (event) => forward('POST', event);
export const PATCH: RequestHandler = (event) => forward('PATCH', event);
export const PUT: RequestHandler = (event) => forward('PUT', event);
export const DELETE: RequestHandler = (event) => forward('DELETE', event);
