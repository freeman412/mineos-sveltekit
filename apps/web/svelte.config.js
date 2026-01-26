import adapterNode from '@sveltejs/adapter-node';
import adapterStatic from '@sveltejs/adapter-static';
import { vitePreprocess } from '@sveltejs/vite-plugin-svelte';

/** @type {import('@sveltejs/kit').Config} */
const config = {
	// Consult https://svelte.dev/docs/kit/integrations
	// for more information about preprocessors
	preprocess: vitePreprocess(),

	kit: {
		adapter:
			process.env.SVELTE_ADAPTER === 'static'
				? adapterStatic({ fallback: 'index.html' })
				: adapterNode(),
		paths: {
			base: process.env.SVELTE_BASE_PATH || ''
		}
	}
};

export default config;
