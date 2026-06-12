// Standalone HTML (no app dependencies) returned when the same-origin
// CSRF check rejects a form submission. Shows the observed values so the
// user can fix their setup without reading server logs.

export interface OriginErrorContext {
	origin: string | null;
	host: string | null;
	forwardedHost: string | null;
}

function esc(value: string | null): string {
	if (!value) return '(not sent)';
	return value
		.replaceAll('&', '&amp;')
		.replaceAll('<', '&lt;')
		.replaceAll('>', '&gt;')
		.replaceAll('"', '&quot;');
}

export function renderOriginErrorPage(ctx: OriginErrorContext): string {
	return `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width, initial-scale=1" />
<title>MineOS — Request Blocked (403)</title>
<style>
	body { font-family: system-ui, sans-serif; background: #0d1117; color: #e6edf3; margin: 0; padding: 2rem; }
	main { max-width: 46rem; margin: 0 auto; }
	h1 { color: #f85149; font-size: 1.4rem; }
	code, pre { background: #161b22; border: 1px solid #30363d; border-radius: 6px; }
	code { padding: 0.1rem 0.4rem; }
	pre { padding: 0.8rem; overflow-x: auto; }
	table { border-collapse: collapse; margin: 1rem 0; }
	td { border: 1px solid #30363d; padding: 0.4rem 0.8rem; }
	td:first-child { color: #8b949e; }
	a { color: #58a6ff; }
</style>
</head>
<body>
<main>
	<h1>Request blocked: browser address doesn't match the server's</h1>
	<p>MineOS blocked this form submission because the address your browser
	says it's on (<strong>Origin</strong>) doesn't match the address this
	request arrived at (<strong>Host</strong>). This protects you against
	cross-site request forgery.</p>
	<table>
		<tr><td>Your browser (Origin)</td><td><code>${esc(ctx.origin)}</code></td></tr>
		<tr><td>Server received (Host)</td><td><code>${esc(ctx.host)}</code></td></tr>
		<tr><td>X-Forwarded-Host</td><td><code>${esc(ctx.forwardedHost)}</code></td></tr>
	</table>
	<h2>How to fix it</h2>
	<p><strong>Using a reverse proxy (nginx, Apache, etc.)?</strong> Make sure it
	forwards the original <code>Host</code> header. For nginx, add this inside your
	<code>location</code> block:</p>
	<pre>proxy_set_header Host $host;</pre>
	<p>Caddy and Traefik already do this by default.</p>
	<p><strong>Not using a proxy?</strong> Reload the page and try again — and see the
	<a href="https://github.com/freeman412/mineos-sveltekit/blob/master/docs/TROUBLESHOOTING.md">troubleshooting guide</a>
	if it keeps happening.</p>
</main>
</body>
</html>`;
}
