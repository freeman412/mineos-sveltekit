// Plain JS, and deliberately not under src/: server.js imports this at runtime in
// the production image, which ships only build/, package.json and server.js.

/**
 * Close codes that may be *sent* on the wire.
 *
 * A WebSocket 'close' event reports codes that a close frame may never carry.
 * 1006 (abnormal closure) is the common one: `ws` raises it locally whenever a
 * connection drops without a close frame, which is exactly what happens when
 * someone closes the Admin Shell.
 *
 * Handing that straight back to close() is fatal. ws validates the argument and
 * throws `TypeError: First argument must be a valid error code number`, the throw
 * happens inside a 'close' event handler where nothing catches it, and an uncaught
 * exception on the event loop ends the process — so closing the Admin Shell took
 * the whole mineos-web container down with it, dropping every other session and
 * every open page. See #156.
 *
 * Mirrors ws's own isValidStatusCode so the check cannot disagree with the code
 * that does the throwing.
 *
 * @param {unknown} code
 * @returns {number | undefined}
 */
export function sanitizeCloseCode(code) {
	if (typeof code !== 'number' || !Number.isInteger(code)) return undefined;

	// 1004, 1005 and 1006 are reserved: they describe how a connection ended and
	// must never be put in a frame.
	if (code >= 1000 && code <= 1014 && code !== 1004 && code !== 1005 && code !== 1006) {
		return code;
	}
	if (code >= 3000 && code <= 4999) return code;

	return undefined;
}

/**
 * A close reason is capped at 123 bytes by the protocol, and ws throws a
 * RangeError past that — the same uncaught-in-a-close-handler death as above,
 * just a rarer trigger. Truncation is on bytes, not characters, and steps back off
 * a partial UTF-8 sequence rather than emitting a broken one.
 *
 * @param {unknown} reason
 * @returns {Buffer | undefined}
 */
export function sanitizeCloseReason(reason) {
	if (reason === undefined || reason === null) return undefined;

	const buffer = Buffer.isBuffer(reason) ? reason : Buffer.from(String(reason), 'utf8');
	if (buffer.length === 0) return undefined;
	if (buffer.length <= 123) return buffer;

	let end = 123;
	while (end > 0 && (buffer[end] & 0xc0) === 0x80) end--;
	return buffer.subarray(0, end);
}

/**
 * Close one end of the proxy without ever taking the process down with it.
 *
 * The try/catch is not redundant with the sanitizers: this runs in a close/error
 * handler, the one place where a throw is unrecoverable, and the socket may
 * already be closing underneath us. Nothing about tearing down one shell session
 * is worth the process.
 *
 * @param {{ close: (code?: number, reason?: Buffer) => void }} socket
 * @param {unknown} [code]
 * @param {unknown} [reason]
 * @param {(message: string) => void} [log]
 */
export function closeSafely(socket, code, reason, log) {
	try {
		socket.close(sanitizeCloseCode(code), sanitizeCloseReason(reason));
	} catch (error) {
		const message = error instanceof Error ? error.message : String(error);
		log?.(`[WS Proxy] Ignoring close failure: ${message}`);
	}
}
