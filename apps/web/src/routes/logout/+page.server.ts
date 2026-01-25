import { redirect } from '@sveltejs/kit';
import { clearAuthCookies } from '$lib/server/authCookies';
import type { PageServerLoad } from './$types';

export const load: PageServerLoad = async ({ cookies, url }) => {
	clearAuthCookies(cookies, url);
	throw redirect(303, '/login');
};
