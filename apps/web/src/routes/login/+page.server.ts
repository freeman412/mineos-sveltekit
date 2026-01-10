import { redirect, fail } from '@sveltejs/kit';
import type { Actions, PageServerLoad } from './$types';

const API_BASE_URL = 'http://127.0.0.1:5078/api/v1';

export const load: PageServerLoad = async ({ cookies }) => {
	const token = cookies.get('auth_token');
	if (token) {
		throw redirect(303, '/servers');
	}
};

export const actions = {
	default: async ({ request, cookies, fetch }) => {
		const data = await request.formData();
		const username = data.get('username')?.toString();
		const password = data.get('password')?.toString();

		if (!username || !password) {
			return fail(400, { error: 'Username and password are required' });
		}

		try {
			const response = await fetch(`${API_BASE_URL}/auth/login`, {
				method: 'POST',
				headers: {
					'Content-Type': 'application/json'
				},
				body: JSON.stringify({ username, password })
			});

			if (!response.ok) {
				if (response.status === 401) {
					return fail(401, { error: 'Invalid username or password' });
				}
				return fail(response.status, { error: 'Login failed. Please try again.' });
			}

			const result = await response.json();

			// Set httpOnly cookie with the JWT token
			cookies.set('auth_token', result.accessToken, {
				httpOnly: true,
				secure: false, // Set to true in production with HTTPS
				sameSite: 'lax',
				maxAge: result.expiresInSeconds,
				path: '/'
			});

			// Set user info cookie for client-side access
			cookies.set(
				'auth_user',
				JSON.stringify({ username: result.username, role: result.role }),
				{
					httpOnly: false, // Client can read this
					secure: false,
					sameSite: 'lax',
					maxAge: result.expiresInSeconds,
					path: '/'
				}
			);

			throw redirect(303, '/servers');
		} catch (err) {
			// SvelteKit's redirect throws a special redirect response, re-throw it
			if (err && typeof err === 'object' && 'status' in err && 'location' in err) {
				throw err;
			}
			console.error('Login error:', err);
			return fail(500, { error: 'An unexpected error occurred' });
		}
	}
} satisfies Actions;
