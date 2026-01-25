import { json } from '@sveltejs/kit';
import type { RequestHandler } from './$types';

const clearAuthCookies = (cookies: { delete: (name: string, options: { path: string }) => void }) => {
	cookies.delete('auth_token', { path: '/' });
	cookies.delete('auth_user', { path: '/' });
};

export const POST: RequestHandler = async ({ cookies }) => {
	clearAuthCookies(cookies);
	return json({ success: true });
};

export const GET: RequestHandler = async ({ cookies }) => {
	clearAuthCookies(cookies);
	return json({ success: true });
};
