import { proxyJson } from '$lib/server/proxyJson';
import type { RequestHandler } from './$types';

export const GET: RequestHandler = async (event) => {
	return proxyJson(event, `/api/v1/settings/${encodeURIComponent(event.params.key)}`);
};

export const PUT: RequestHandler = async (event) => {
	const body = await event.request.json();
	return proxyJson(event, `/api/v1/settings/${encodeURIComponent(event.params.key)}`, {
		method: 'PUT',
		body: JSON.stringify(body)
	});
};
