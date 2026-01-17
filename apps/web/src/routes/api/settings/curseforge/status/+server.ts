import { proxyJson } from '$lib/server/proxyJson';
import type { RequestHandler } from './$types';

export const GET: RequestHandler = async (event) => {
	return proxyJson(event, '/api/v1/settings/curseforge/status');
};
