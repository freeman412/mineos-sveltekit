// Dynamic same-origin CSRF validation, replacing SvelteKit's fixed-ORIGIN check.
// See docs/superpowers/specs/2026-06-12-origin-403-fix-design.md

const FORM_CONTENT_TYPES = new Set([
	'application/x-www-form-urlencoded',
	'multipart/form-data',
	'text/plain'
]);

/** Content types a browser can send cross-site via plain <form> submission. */
export function isFormContentType(contentType: string | null): boolean {
	if (!contentType) return false;
	const type = contentType.split(';')[0].trim().toLowerCase();
	return FORM_CONTENT_TYPES.has(type);
}

export interface OriginCheckInput {
	origin: string | null;
	host: string | null;
	forwardedHost: string | null;
	configuredOrigin: string | null;
}

/**
 * A request's Origin is trusted when its host:port matches any of:
 * - the Host header (direct access via any IP/DNS name "just works"),
 * - the first X-Forwarded-Host value (proxies that rewrite Host),
 * - the legacy ORIGIN env var (backward compatibility).
 * Protocol is deliberately ignored: behind a TLS-terminating proxy the
 * browser sends https:// while the internal request is http://, and host
 * equality is what actually defends against CSRF.
 */
export function isTrustedOrigin(input: OriginCheckInput): boolean {
	const originHost = hostOf(input.origin);
	if (!originHost) return false;

	if (input.host && originHost === input.host.trim().toLowerCase()) return true;

	const forwarded = input.forwardedHost?.split(',')[0].trim().toLowerCase();
	if (forwarded && originHost === forwarded) return true;

	const configuredHost = hostOf(input.configuredOrigin);
	if (configuredHost && originHost === configuredHost) return true;

	return false;
}

function hostOf(url: string | null): string | null {
	if (!url) return null;
	try {
		const parsed = new URL(url);
		// Browsers never send userinfo in Origin; a userinfo-bearing value is
		// either garbage or an attempt to confuse the parser. Reject it.
		if (parsed.username || parsed.password) return null;
		return parsed.host.toLowerCase();
	} catch {
		return null;
	}
}
