import type { PageServerLoad, Actions } from './$types';
import {
	getVelocityConfig,
	updateVelocityConfig,
	getBungeeConfig,
	updateBungeeConfig,
	getServer
} from '$lib/api/client';
import { fail } from '@sveltejs/kit';

// Which proxy implementation this server actually runs. The config file on disk
// is authoritative: the jar name is not, because an imported, renamed, or
// not-yet-downloaded jar sends a BungeeCord server down the Velocity branch,
// and saving there writes a stray velocity.toml that
// GetServerListenEndpointAsync then *prefers* over config.yml — so the dashboard
// silently reads the wrong port from that point on.
//
// The jar name is kept only as a tiebreak for a proxy with neither file yet,
// and "velocity" remains the final fallback so an empty proxy server keeps
// showing the existing editor (matches behavior before BungeeCord landed).
function detectProxyKindFromJar(jar: string | null): 'velocity' | 'bungeecord' {
	const j = (jar ?? '').toLowerCase();
	if (j.includes('bungeecord')) return 'bungeecord';
	return 'velocity';
}

export const load: PageServerLoad = async ({ params, fetch }) => {
	// Both endpoints report `exists` for their own config file, so this resolves
	// the kind without a new API surface.
	const [velocityConfig, bungeeConfig] = await Promise.all([
		getVelocityConfig(fetch, params.name),
		getBungeeConfig(fetch, params.name)
	]);

	const hasVelocity = velocityConfig.data?.exists ?? false;
	const hasBungee = bungeeConfig.data?.exists ?? false;

	let proxyKind: 'velocity' | 'bungeecord';
	if (hasVelocity !== hasBungee) {
		proxyKind = hasVelocity ? 'velocity' : 'bungeecord';
	} else {
		// Neither file exists yet, or (after a past mis-detection) both do —
		// fall back to the jar name.
		const server = await getServer(fetch, params.name);
		proxyKind = detectProxyKindFromJar(server.data?.config?.java?.jarFile ?? null);
	}

	return {
		proxyKind,
		velocityConfig: proxyKind === 'velocity' ? velocityConfig : null,
		bungeeConfig: proxyKind === 'bungeecord' ? bungeeConfig : null,
		serverName: params.name
	};
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
