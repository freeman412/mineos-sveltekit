import { redirect } from '@sveltejs/kit';
import type { LayoutServerLoad } from './$types';

export const load: LayoutServerLoad = async ({ cookies }) => {
	const token = cookies.get('auth_token');
	const userJson = cookies.get('auth_user');

	if (!token) {
		throw redirect(303, '/login');
	}

	let user = null;
	if (userJson) {
		try {
			user = JSON.parse(userJson);
		} catch {
			// Invalid user data, force re-login
			throw redirect(303, '/login');
		}
	}

	return {
		user
	};
};
