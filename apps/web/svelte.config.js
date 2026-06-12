import adapter from '@sveltejs/adapter-node';
import { vitePreprocess } from '@sveltejs/vite-plugin-svelte';

/** @type {import('@sveltejs/kit').Config} */
const config = {
	// Consult https://svelte.dev/docs/kit/integrations
	// for more information about preprocessors
	preprocess: vitePreprocess(),

	kit: {
		adapter: adapter(),
		// Origin validation is handled dynamically in src/hooks.server.ts
		// (compares Origin against the request's own Host header) so MineOS
		// works from any IP/DNS name without configuring ORIGIN. See #100.
		csrf: {
			checkOrigin: false
		}
	}
};

export default config;
