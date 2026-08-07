import type { PageServerLoad, Actions } from './$types';
import {
	getVelocityConfig,
	updateVelocityConfig,
	getBungeeConfig,
	updateBungeeConfig,
	getServer
} from '$lib/api/client';
import { fail } from '@sveltejs/kit';

// Detect which proxy implementation is installed by inspecting the jar name.
// Used by the page to decide which config editor to render and which API to hit.
// Falls back to "velocity" so an empty proxy server keeps showing the existing
// editor (matches behavior before BungeeCord support landed).
function detectProxyKind(jar: string | null): 'velocity' | 'bungeecord' {
	const j = (jar ?? '').toLowerCase();
	if (j.includes('bungeecord')) return 'bungeecord';
	return 'velocity';
}

export const load: PageServerLoad = async ({ params, fetch }) => {
	const server = await getServer(fetch, params.name);
	const jar = server.data?.config?.java?.jarFile ?? null;
	const proxyKind = detectProxyKind(jar);

	if (proxyKind === 'velocity') {
		const config = await getVelocityConfig(fetch, params.name);
		return { proxyKind, velocityConfig: config, bungeeConfig: null, serverName: params.name };
	}

	const config = await getBungeeConfig(fetch, params.name);
	return { proxyKind, velocityConfig: null, bungeeConfig: config, serverName: params.name };
};

export const actions = {
	default: async ({ request, params, fetch }) => {
		const data = await request.formData();
		const configJson = data.get('config')?.toString();
		const kind = data.get('proxyKind')?.toString() ?? 'velocity';

		if (!configJson) {
			return fail(400, { error: 'Config data is required' });
		}

		try {
			const config = JSON.parse(configJson);
			const result =
				kind === 'velocity'
					? await updateVelocityConfig(fetch, params.name, config)
					: await updateBungeeConfig(fetch, params.name, config);

			if (result.error) {
				return fail(500, { error: result.error });
			}

			return { success: true };
		} catch {
			return fail(500, { error: 'Failed to update proxy config' });
		}
	}
} satisfies Actions;
