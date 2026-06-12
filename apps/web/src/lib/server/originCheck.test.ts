import { describe, it, expect } from 'vitest';
import { isFormContentType, isTrustedOrigin } from './originCheck';

describe('isFormContentType', () => {
	it('matches the three form-capable content types', () => {
		expect(isFormContentType('application/x-www-form-urlencoded')).toBe(true);
		expect(isFormContentType('multipart/form-data; boundary=----x')).toBe(true);
		expect(isFormContentType('text/plain')).toBe(true);
	});

	it('ignores JSON and missing content types', () => {
		expect(isFormContentType('application/json')).toBe(false);
		expect(isFormContentType(null)).toBe(false);
	});

	it('is case-insensitive and ignores parameters', () => {
		expect(isFormContentType('Application/X-WWW-Form-Urlencoded; charset=utf-8')).toBe(true);
	});
});

describe('isTrustedOrigin', () => {
	const base = { origin: null, host: null, forwardedHost: null, configuredOrigin: null };

	it('passes when Origin host:port equals Host header', () => {
		expect(
			isTrustedOrigin({ ...base, origin: 'http://192.168.1.50:3000', host: '192.168.1.50:3000' })
		).toBe(true);
	});

	it('ignores protocol differences (TLS-terminating proxy)', () => {
		expect(
			isTrustedOrigin({ ...base, origin: 'https://mineos.example.com', host: 'mineos.example.com' })
		).toBe(true);
	});

	it('fails when hosts differ', () => {
		expect(
			isTrustedOrigin({ ...base, origin: 'http://evil.example', host: '192.168.1.50:3000' })
		).toBe(false);
	});

	it('fails when ports differ', () => {
		expect(
			isTrustedOrigin({ ...base, origin: 'http://192.168.1.50:3001', host: '192.168.1.50:3000' })
		).toBe(false);
	});

	it('passes when Origin matches X-Forwarded-Host', () => {
		expect(
			isTrustedOrigin({
				...base,
				origin: 'https://mineos.example.com',
				host: 'mineos-web:3000',
				forwardedHost: 'mineos.example.com'
			})
		).toBe(true);
	});

	it('uses only the first value of a comma-separated X-Forwarded-Host', () => {
		expect(
			isTrustedOrigin({
				...base,
				origin: 'https://mineos.example.com',
				host: 'mineos-web:3000',
				forwardedHost: 'mineos.example.com, internal-lb'
			})
		).toBe(true);
	});

	it('passes when Origin matches the configured ORIGIN env (normalized)', () => {
		expect(
			isTrustedOrigin({
				...base,
				origin: 'http://myserver:3000',
				host: 'something-else:9999',
				configuredOrigin: 'http://myserver:3000/'
			})
		).toBe(true);
	});

	it('ignores an unparseable configured ORIGIN', () => {
		expect(
			isTrustedOrigin({
				...base,
				origin: 'http://a:1',
				host: 'b:2',
				configuredOrigin: 'not a url'
			})
		).toBe(false);
	});

	it('fails on missing Origin', () => {
		expect(isTrustedOrigin({ ...base, host: '192.168.1.50:3000' })).toBe(false);
	});

	it('fails on garbage Origin without throwing', () => {
		expect(isTrustedOrigin({ ...base, origin: '::::garbage', host: '192.168.1.50:3000' })).toBe(false);
	});

	it('rejects a userinfo-bearing Origin', () => {
		expect(
			isTrustedOrigin({ ...base, origin: 'http://user@192.168.1.50:3000', host: '192.168.1.50:3000' })
		).toBe(false);
	});

	it('rejects a userinfo-bearing configured ORIGIN', () => {
		expect(
			isTrustedOrigin({
				...base,
				origin: 'http://trusted.example',
				host: 'other.example',
				configuredOrigin: 'http://user@trusted.example'
			})
		).toBe(false);
	});

	it('documents the explicit-default-port edge: URL parsing strips :80 from Origin', () => {
		// WHATWG URL normalizes http://myserver:80 -> host "myserver", while a raw
		// Host header could read "myserver:80". In practice browsers omit default
		// ports from both Origin and Host, so the normalized forms match; this
		// test pins the parser behavior so a regression is visible.
		expect(isTrustedOrigin({ ...base, origin: 'http://myserver:80', host: 'myserver' })).toBe(true);
		expect(isTrustedOrigin({ ...base, origin: 'http://myserver:80', host: 'myserver:80' })).toBe(false);
	});
});
