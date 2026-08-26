/**
 * Keeps a proxied event stream from going silent long enough for a reverse
 * proxy to hang up on it.
 *
 * nginx (and most other proxies) close an upstream connection after
 * `proxy_read_timeout` — 60 seconds by default — with nothing read on it. Some
 * MineOS streams only write when their subject changes: the notifications and
 * jobs streams compare each poll against the last payload and skip the write
 * when it matches, and the console stream writes only when the server logs a
 * line. A quiet server, or a page left open, therefore produces no traffic at
 * all, and the proxy kills a stream that is working exactly as intended.
 *
 * The symptom is not obvious from the browser: EventSource reconnects on its
 * own, so the console and live status kept working while silently dropping and
 * re-establishing every minute, and the only trace was a steady run of
 * "upstream timed out ... while reading upstream" in the proxy's error log.
 *
 * A comment line — a chunk beginning with ':' — is traffic on the connection
 * but is discarded by the EventSource parser, so it resets the proxy's read
 * timer without reaching page code. Emitting one whenever the upstream has
 * been quiet costs a few bytes a minute per open stream.
 */
const HEARTBEAT = new TextEncoder().encode(': keep-alive\n\n');

/**
 * Well under the 60s `proxy_read_timeout` that nginx, Apache and Traefik
 * default to, so a stream survives a proxy nobody had to tune first.
 */
export const HEARTBEAT_INTERVAL_MS = 20_000;

/**
 * Wrap an upstream event-stream body so it emits a comment line whenever the
 * upstream has been idle for `intervalMs`. Upstream chunks pass through
 * untouched and are never delayed by the heartbeat.
 *
 * `onCancel` runs when the client goes away, so the caller can abort the
 * upstream request rather than leaving it running with nobody listening.
 */
export function withHeartbeat(
	body: ReadableStream<Uint8Array>,
	onCancel: () => void,
	intervalMs: number = HEARTBEAT_INTERVAL_MS
): ReadableStream<Uint8Array> {
	const reader = body.getReader();

	// Held across pulls: when the idle timer wins the race the upstream read is
	// still outstanding, and starting a second one would consume chunks out of
	// order.
	let pending: Promise<ReadableStreamReadResult<Uint8Array>> | null = null;

	return new ReadableStream<Uint8Array>({
		async pull(controller) {
			pending ??= reader.read();

			let timer: ReturnType<typeof setTimeout> | undefined;
			const idle = new Promise<'idle'>((resolve) => {
				timer = setTimeout(() => resolve('idle'), intervalMs);
			});

			try {
				const result = await Promise.race([pending, idle]);

				if (result === 'idle') {
					controller.enqueue(HEARTBEAT);
					return;
				}

				pending = null;

				if (result.done) {
					controller.close();
					return;
				}
				if (result.value) controller.enqueue(result.value);
			} finally {
				clearTimeout(timer);
			}
		},
		cancel(reason) {
			onCancel();
			// cancel(), not releaseLock(): a read is usually still outstanding
			// here, and releasing the lock under one throws.
			reader.cancel(reason).catch(() => {});
		}
	});
}
