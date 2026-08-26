import { describe, expect, it, vi } from 'vitest';
// Lives beside server.js rather than under src/: the production image ships only
// build/, package.json and server.js, and server.js imports it at runtime.
import { closeSafely, sanitizeCloseCode, sanitizeCloseReason } from '../../../wsCloseCode.js';

/**
 * Stands in for a ws socket, with ws's own validation. Getting this wrong is what
 * killed the process: the throw lands in a 'close' handler where nothing catches it.
 */
function fakeSocket() {
	const calls: Array<{ code: unknown; reason: unknown }> = [];
	return {
		calls,
		close(code?: number, reason?: unknown) {
			if (code !== undefined) {
				const valid =
					Number.isInteger(code) &&
					((code >= 1000 && code <= 1014 && code !== 1004 && code !== 1005 && code !== 1006) ||
						(code >= 3000 && code <= 4999));
				if (!valid) throw new TypeError('First argument must be a valid error code number');
			}
			if (reason !== undefined && Buffer.byteLength(reason as string) > 123) {
				throw new RangeError('The message must not be greater than 123 bytes');
			}
			calls.push({ code, reason });
		}
	};
}

describe('sanitizeCloseCode', () => {
	it.each([1006, 1005, 1004, 1015, 999, 5000, 0, -1])('drops the reserved code %i', (code) => {
		// 1006 is the one that shipped: ws raises it locally whenever a connection
		// drops without a close frame, which is what closing the Admin Shell does.
		expect(sanitizeCloseCode(code)).toBeUndefined();
	});

	it.each([1000, 1001, 1011, 1014, 3000, 4999])('passes the sendable code %i through', (code) => {
		expect(sanitizeCloseCode(code)).toBe(code);
	});

	it('drops a non-integer code', () => {
		expect(sanitizeCloseCode(1000.5)).toBeUndefined();
		expect(sanitizeCloseCode(undefined)).toBeUndefined();
		expect(sanitizeCloseCode('1000')).toBeUndefined();
	});
});

describe('sanitizeCloseReason', () => {
	it('passes a short reason through', () => {
		expect(sanitizeCloseReason('bye')?.toString()).toBe('bye');
	});

	it('truncates past the 123 byte protocol cap', () => {
		// Over the cap ws throws a RangeError — the same uncaught-in-a-close-handler
		// death as a bad code, just a rarer trigger.
		const reason = sanitizeCloseReason('x'.repeat(500));

		expect(reason?.length).toBeLessThanOrEqual(123);
	});

	it('does not cut a multi-byte character in half', () => {
		const reason = sanitizeCloseReason('☃'.repeat(100));

		expect(reason?.length).toBeLessThanOrEqual(123);
		// A truncation mid-sequence would decode to a replacement character.
		expect(reason?.toString('utf8')).not.toContain('�');
	});

	it('treats an empty reason as none', () => {
		expect(sanitizeCloseReason('')).toBeUndefined();
		expect(sanitizeCloseReason(undefined)).toBeUndefined();
	});
});

describe('closeSafely', () => {
	it('does not throw on the code that took the process down', () => {
		const socket = fakeSocket();

		expect(() => closeSafely(socket, 1006, 'connection lost')).not.toThrow();
		expect(socket.calls).toEqual([{ code: undefined, reason: Buffer.from('connection lost') }]);
	});

	it('forwards a legitimate close code untouched', () => {
		const socket = fakeSocket();

		closeSafely(socket, 1000, 'done');

		expect(socket.calls[0].code).toBe(1000);
	});

	it('swallows a throw from a socket already tearing down', () => {
		// Belt and braces: this runs in a close handler, the one place a throw is
		// unrecoverable. Nothing about ending one shell session is worth the process.
		const log = vi.fn();
		const exploding = {
			close() {
				throw new Error('already closed');
			}
		};

		expect(() => closeSafely(exploding, 1000, undefined, log)).not.toThrow();
		expect(log).toHaveBeenCalledOnce();
	});
});
