import { createProxyHandlers } from '$lib/server/proxyApi';

export const { GET, POST } =
	createProxyHandlers('/api/v1/fabric');
