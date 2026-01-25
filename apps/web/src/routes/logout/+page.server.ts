import { redirect } from '@sveltejs/kit';
import type { PageServerLoad } from './$types';

export const load: PageServerLoad = async ({ cookies }) => {
	cookies.delete('auth_token', { path: '/', sameSite: 'lax' });
	cookies.delete('auth_user', { path: '/', sameSite: 'lax' });
	throw redirect(303, '/login');
};
