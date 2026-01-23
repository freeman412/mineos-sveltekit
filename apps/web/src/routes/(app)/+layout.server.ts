import { redirect } from '@sveltejs/kit';
import type { LayoutServerLoad } from './$types';
import * as api from '$lib/api/client';

export const load: LayoutServerLoad = async ({ cookies, fetch, url }) => {
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

	if (url.pathname.startsWith('/profiles/buildtools')) {
		console.info('[layout] buildtools load', url.pathname, url.search);
	}

	// Load servers and profiles for search
	const [servers, profiles] = await Promise.all([
		api.getAllServers(fetch),
		api.getHostProfiles(fetch)
	]);

	return {
		user,
		servers: servers.data ?? [],
		profiles: profiles.data ?? []
	};
};
