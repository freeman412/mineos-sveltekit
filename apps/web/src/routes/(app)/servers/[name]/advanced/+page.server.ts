import type { PageServerLoad, Actions } from './$types';
import { getHostProfiles, getServerConfig, updateServerConfig } from '$lib/api/client';
import { fail } from '@sveltejs/kit';

export const load: PageServerLoad = async ({ params, fetch }) => {
	const config = await getServerConfig(fetch, params.name);
	const profiles = await getHostProfiles(fetch);
	let jarFiles: string[] = [];
	let forgeArgFiles: string[] = [];
	let jarFilesError: string | null = null;

	try {
		const rootRes = await fetch(`/api/servers/${encodeURIComponent(params.name)}/files`);
		if (rootRes.ok) {
			const root = await rootRes.json();
			const entries = root.entries ?? [];
			jarFiles = entries
				.filter((entry: { name: string; isDirectory: boolean }) => !entry.isDirectory)
				.map((entry: { name: string }) => entry.name)
				.filter((name: string) => name.toLowerCase().endsWith('.jar') || name.toLowerCase().endsWith('.jar.disabled'))
				.sort((a: string, b: string) => a.localeCompare(b));
		} else {
			jarFilesError = `Failed to load server files (${rootRes.status})`;
		}
	} catch (err) {
		jarFilesError = 'Failed to load server files';
	}

	try {
		const forgeRes = await fetch(
			`/api/servers/${encodeURIComponent(params.name)}/files/libraries/net/minecraftforge/forge`
		);
		if (forgeRes.ok) {
			const forge = await forgeRes.json();
			const entries = forge.entries ?? [];
			forgeArgFiles = entries
				.filter((entry: { name: string; isDirectory: boolean }) => entry.isDirectory)
				.map((entry: { name: string }) => `@user_jvm_args.txt @libraries/net/minecraftforge/forge/${entry.name}/unix_args.txt`)
				.sort((a: string, b: string) => a.localeCompare(b));
		}
	} catch (err) {
		forgeArgFiles = [];
	}

	return {
		config,
		profiles,
		jarFiles: {
			data: jarFiles,
			error: jarFilesError
		},
		forgeArgFiles: {
			data: forgeArgFiles,
			error: null
		}
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
			const result = await updateServerConfig(fetch, params.name, config);

			if (result.error) {
				return fail(500, { error: result.error });
			}

			return { success: true };
		} catch (err) {
			return fail(500, { error: 'Failed to update config' });
		}
	}
} satisfies Actions;
