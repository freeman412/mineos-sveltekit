import type { PageLoad } from './$types';
import { demoLoad } from '$lib/demo/loaders';

export const load: PageLoad = ({ fetch }) =>
	demoLoad(fetch, async (demoFetch) => {
		const response = await demoFetch('/api/auth/me');
		if (!response.ok) {
			return {
				user: null,
				error: 'Unable to load profile details.'
			};
		}

		const user = await response.json();
		return {
			user,
			error: null
		};
	});
