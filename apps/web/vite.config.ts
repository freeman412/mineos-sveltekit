import { sveltekit } from '@sveltejs/kit/vite';
import { defineConfig } from 'vite';

const allowedHostsEnv = process.env.VITE_ALLOWED_HOSTS ?? '';
const allowedHosts = allowedHostsEnv
	.split(',')
	.map((host) => host.trim())
	.filter((host) => host.length > 0);
const allowAnyHost =
	process.env.VITE_ALLOW_ANY_HOST === 'true' || process.env.VITE_ALLOW_ANY_HOST === '1';
const proxyTarget =
	process.env.VITE_DEV_PROXY_TARGET ??
	process.env.PRIVATE_API_BASE_URL ??
	process.env.INTERNAL_API_URL ??
	'http://api:5078';

export default defineConfig({
	plugins: [sveltekit()],
	server: {
		host: true,
		allowedHosts: allowAnyHost ? true : allowedHosts.length > 0 ? allowedHosts : undefined,
		proxy: proxyTarget
			? {
					'/api': {
						target: proxyTarget,
						changeOrigin: true,
						ws: true
					}
				}
			: undefined
	}
});
