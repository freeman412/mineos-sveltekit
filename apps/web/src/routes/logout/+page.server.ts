import { redirect } from '@sveltejs/kit';
import { clearAuthCookies } from '$lib/server/authCookies';
import type { Actions } from './$types';

// Use form action instead of load to ensure logout happens via POST
export const actions = {
	default: async ({ cookies, url }) => {
		clearAuthCookies(cookies, url);
		throw redirect(303, '/login');
	}
} satisfies Actions;
