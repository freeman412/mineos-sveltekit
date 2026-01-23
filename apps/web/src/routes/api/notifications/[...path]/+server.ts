import { createProxyHandlers } from '$lib/server/proxyApi';

export const { GET, POST, PUT, PATCH, DELETE } = createProxyHandlers('/api/v1/notifications');
