import { describe, it, expect } from 'vitest';
import { renderOriginErrorPage } from './originErrorPage';

describe('renderOriginErrorPage', () => {
	it('shows the observed Origin and Host values', () => {
		const html = renderOriginErrorPage({
			origin: 'http://192.168.1.50:3000',
			host: 'localhost:3000',
			forwardedHost: null
		});
		expect(html).toContain('http://192.168.1.50:3000');
		expect(html).toContain('localhost:3000');
		expect(html).toContain('proxy_set_header Host');
	});

	it('renders placeholders for missing headers', () => {
		const html = renderOriginErrorPage({ origin: null, host: null, forwardedHost: null });
		expect(html).toContain('(not sent)');
	});

	it('escapes HTML in header values', () => {
		const html = renderOriginErrorPage({
			origin: '<script>alert(1)</script>',
			host: null,
			forwardedHost: null
		});
		expect(html).not.toContain('<script>alert(1)</script>');
		expect(html).toContain('&lt;script&gt;');
	});
});
