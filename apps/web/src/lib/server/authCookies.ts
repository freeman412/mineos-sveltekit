import type { Cookies } from '@sveltejs/kit';

type CookieOptions = {
	path: string;
	domain?: string;
	secure?: boolean;
	sameSite?: 'lax' | 'strict' | 'none';
};

const baseOptions = (overrides: Partial<CookieOptions>): CookieOptions => ({
	path: '/',
	sameSite: 'lax',
	...overrides
});

const hostCandidates = (hostname: string): Array<Pick<CookieOptions, 'domain'>> => {
	const candidates: Array<Pick<CookieOptions, 'domain'>> = [{}, { domain: hostname }];
	if (
		hostname.includes('.') &&
		hostname !== 'localhost' &&
		!hostname.match(/^\d+\.\d+\.\d+\.\d+$/)
	) {
		candidates.push({ domain: `.${hostname}` });
	}
	return candidates;
};

export const clearAuthCookies = (cookies: Cookies, url: URL) => {
	const secureOptions = url.protocol === 'https:' ? [true, false] : [false];
	const hosts = hostCandidates(url.hostname);

	for (const host of hosts) {
		for (const secure of secureOptions) {
			const opts = baseOptions({ ...host, secure });
			cookies.delete('auth_token', opts);
			cookies.delete('auth_user', opts);
		}
	}
};
