import { json } from '@sveltejs/kit';
import { clearAuthCookies } from '$lib/server/authCookies';
import type { RequestHandler } from './$types';

export const POST: RequestHandler = async ({ cookies, url }) => {
	clearAuthCookies(cookies, url);
	return json({ success: true });
};

export const GET: RequestHandler = async ({ cookies, url }) => {
	clearAuthCookies(cookies, url);
	return json({ success: true });
};
