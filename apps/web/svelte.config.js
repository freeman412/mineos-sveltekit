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
		// trustedOrigins: ['*'] disables the built-in check (checkOrigin is
		// deprecated and compiles to the same thing).
		csrf: {
			trustedOrigins: ['*']
		},
		output: {
			// Emit the module preloads as <link> tags in the page head.
			//
			// Under the default 'modulepreload' strategy they exist *only* in the
			// `Link:` response header, which hooks.server.ts has to strip — it grew
			// to ~3.9 KB and blew past the 4 KB proxy_buffer_size that nginx and
			// most reverse proxies ship with, so every full page load came back a
			// 502. Putting them in the head keeps the preloading while the header
			// stays small enough for a proxy nobody had to tune.
			preloadStrategy: 'preload-js'
		}
	}
};

export default config;
