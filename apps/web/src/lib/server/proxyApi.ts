import { env } from '$env/dynamic/private';
import type { RequestEvent, RequestHandler } from '@sveltejs/kit';

const baseUrl =
	env.PRIVATE_API_BASE_URL ??
	env.INTERNAL_API_URL ??
	'http://api:5078';

const apiKey = env.PRIVATE_API_KEY ?? '';

type ProxyOptions = {
	prefix: string;
	paramName?: string;          // default "path" for [...path]
	requireApiKey?: boolean;     // default true
	authCookieName?: string;     // default "auth_token"
};

function buildHeaders(request: Request, token?: string | null): HeadersInit {
	const headers: Record<string, string> = {};

	const contentType = request.headers.get('content-type');
	if (contentType) headers['Content-Type'] = contentType;

	const accept = request.headers.get('accept');
	if (accept) headers['Accept'] = accept;

	const contentLength = request.headers.get('content-length');
	if (contentLength) headers['Content-Length'] = contentLength;

	const contentEncoding = request.headers.get('content-encoding');
	if (contentEncoding) headers['Content-Encoding'] = contentEncoding;

	const fileName = request.headers.get('x-file-name');
	if (fileName) headers['X-File-Name'] = fileName;

	const fileSize = request.headers.get('x-file-size');
	if (fileSize) headers['X-File-Size'] = fileSize;

	if (apiKey) headers['X-Api-Key'] = apiKey;
	if (token) headers['Authorization'] = `Bearer ${token}`;

	return headers;
}

function passthroughHeaders(response: Response) {
	const out = new Headers();
	for (const h of ['content-type', 'content-disposition', 'cache-control', 'etag', 'last-modified']) {
		const v = response.headers.get(h);
		if (v) out.set(h, v);
	}
	return out;
}

async function forward(event: RequestEvent, opts: ProxyOptions, method?: string) {
	const paramName = opts.paramName ?? 'path';
	const requireApiKey = opts.requireApiKey ?? true;
	const authCookieName = opts.authCookieName ?? 'auth_token';

	if (requireApiKey && !apiKey) {
		return new Response('Missing PRIVATE_API_KEY', { status: 500 });
	}

	const m = (method ?? event.request.method).toUpperCase();
	const token = event.cookies.get(authCookieName);
	const headers = buildHeaders(event.request, token);

	const hasBody = m !== 'GET' && m !== 'DELETE';
	const body = hasBody ? event.request.body : undefined;

	const path = (event.params as any)[paramName] ?? '';
	const suffix = path ? `/${path}` : '';
	const upstreamUrl = `${baseUrl}${opts.prefix}${suffix}${event.url.search}`;

	const fetchInit: RequestInit & { duplex?: 'half' } = { method: m, headers, body };
	if (hasBody) fetchInit.duplex = 'half';

	const res = await fetch(upstreamUrl, fetchInit);

	return new Response(res.body, {
		status: res.status,
		headers: passthroughHeaders(res)
	});
}

export function createProxyHandlers(
	prefix: string,
	opts: Omit<ProxyOptions, 'prefix'> = {}
): Record<string, RequestHandler> {
	const options: ProxyOptions = { prefix, ...opts };
	return {
		GET: (e) => forward(e, options, 'GET'),
		POST: (e) => forward(e, options, 'POST'),
		PUT: (e) => forward(e, options, 'PUT'),
		PATCH: (e) => forward(e, options, 'PATCH'),
		DELETE: (e) => forward(e, options, 'DELETE')
	};
}
