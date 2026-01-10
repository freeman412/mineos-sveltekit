import { json } from '@sveltejs/kit';
import type { RequestHandler } from './$types';

export const POST: RequestHandler = async ({ cookies }) => {
	cookies.delete('auth_token', { path: '/' });
	cookies.delete('auth_user', { path: '/' });
	return json({ success: true });
};
