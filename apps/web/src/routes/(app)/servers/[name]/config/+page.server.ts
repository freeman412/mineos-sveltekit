import type { PageServerLoad, Actions } from './$types';
import { getServerProperties, updateServerProperties } from '$lib/api/client';
import { fail } from '@sveltejs/kit';

export const load: PageServerLoad = async ({ params, fetch }) => {
	const properties = await getServerProperties(fetch, params.name);
	return {
		properties
	};
};

export const actions = {
	default: async ({ request, params, fetch }) => {
		const data = await request.formData();
		const propertiesJson = data.get('properties')?.toString();

		if (!propertiesJson) {
			return fail(400, { error: 'Properties data is required' });
		}

		try {
			const properties = JSON.parse(propertiesJson);
			const result = await updateServerProperties(fetch, params.name, properties);

			if (result.error) {
				return fail(500, { error: result.error });
			}

			return { success: true };
		} catch (err) {
			return fail(500, { error: 'Failed to update properties' });
		}
	}
} satisfies Actions;
