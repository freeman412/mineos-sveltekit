import { createProxyHandlers } from '$lib/server/proxyApi';

const createHandlers = createProxyHandlers('/api/v1/servers');
const listHandlers = createProxyHandlers('/api/v1/servers/list');

export const GET = listHandlers.GET;
export const POST = createHandlers.POST;
