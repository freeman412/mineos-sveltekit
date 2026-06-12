import type { Handle } from '@sveltejs/kit';
import { env } from '$env/dynamic/private';
import { isFormContentType, isTrustedOrigin } from '$lib/server/originCheck';
import { renderOriginErrorPage } from '$lib/server/originErrorPage';

// Replaces SvelteKit's csrf.checkOrigin (disabled in svelte.config.js) with a
// dynamic same-origin check so any IP/DNS name pointing at this server works
// without configuring ORIGIN. Scope mirrors SvelteKit's own check: only
// state-changing methods with form-capable content types are validated.
// Trusting X-Forwarded-Host is CSRF-safe: a cross-site attacker cannot attach
// custom headers to a form submission (doing so via fetch forces a CORS
// preflight, which fails), so the header can only come from our own proxy.
const MUTATING_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);

export const handle: Handle = async ({ event, resolve }) => {
	const { request } = event;

	if (
		MUTATING_METHODS.has(request.method) &&
		isFormContentType(request.headers.get('content-type'))
	) {
		const ctx = {
			origin: request.headers.get('origin'),
			host: request.headers.get('host'),
			forwardedHost: request.headers.get('x-forwarded-host')
		};

		if (!isTrustedOrigin({ ...ctx, configuredOrigin: env.ORIGIN ?? null })) {
			console.warn(
				`[csrf] Blocked ${request.method} ${event.url.pathname}: ` +
					`Origin="${ctx.origin ?? ''}" Host="${ctx.host ?? ''}" ` +
					`X-Forwarded-Host="${ctx.forwardedHost ?? ''}"`
			);
			return new Response(renderOriginErrorPage(ctx), {
				status: 403,
				headers: { 'content-type': 'text/html; charset=utf-8' }
			});
		}
	}

	return resolve(event);
};
