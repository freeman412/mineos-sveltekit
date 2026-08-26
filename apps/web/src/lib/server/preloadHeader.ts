/**
 * SvelteKit mirrors every preload <link> on a page into a `Link:` response
 * header. On MineOS pages that single header runs to roughly 3.9 KB.
 *
 * nginx — and most other reverse proxies — buffer the upstream's response
 * header block into 4 KB by default (`proxy_buffer_size`). A block that does
 * not fit is not truncated: the proxy logs "upstream sent too big header",
 * discards the response, and serves a 502.
 *
 * It only ever broke full page loads. A client-side navigation fetches
 * __data.json, which carries no preload header, so the same page reached by
 * clicking a link worked and the same page reached by hitting refresh did not.
 *
 * svelte.config.js sets `output.preloadStrategy: 'preload-js'` so the preloads
 * are emitted as <link> tags in the page head, where they still do their job.
 * This header is left over for Early Hints and HTTP/2 push, which browsers no
 * longer implement, so dropping it costs nothing.
 */
export function stripPreloadHeader(response: Response): Response {
	response.headers.delete('link');
	return response;
}
