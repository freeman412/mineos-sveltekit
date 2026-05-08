import type { PageServerLoad, Actions } from './$types';
import { getVelocityConfig, updateVelocityConfig } from '$lib/api/client';
import { fail } from '@sveltejs/kit';

export const load: PageServerLoad = async ({ params, fetch }) => {
	const config = await getVelocityConfig(fetch, params.name);
	return {
		config,
		serverName: params.name
	};
};

export const actions = {
	default: async ({ request, params, fetch }) => {
		const data = await request.formData();
		const configJson = data.get('config')?.toString();

		if (!configJson) {
			return fail(400, { error: 'Config data is required' });
		}

		try {
			const config = JSON.parse(configJson);
			const result = await updateVelocityConfig(fetch, params.name, config);

			if (result.error) {
				return fail(500, { error: result.error });
			}

			return { success: true };
		} catch {
			return fail(500, { error: 'Failed to update Velocity config' });
		}
	}
} satisfies Actions;
