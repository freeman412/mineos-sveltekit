import { describe, expect, it, vi } from 'vitest';
import { HEARTBEAT_INTERVAL_MS, withHeartbeat } from './sseHeartbeat';

const decoder = new TextDecoder();

/** An upstream stream driven by hand, so a test can decide when it speaks. */
function controllable() {
	let controller!: ReadableStreamDefaultController<Uint8Array>;
	const stream = new ReadableStream<Uint8Array>({
		start(c) {
			controller = c;
		}
	});
	const encoder = new TextEncoder();
	return {
		stream,
		send: (text: string) => controller.enqueue(encoder.encode(text)),
		close: () => controller.close()
	};
}

async function readChunks(stream: ReadableStream<Uint8Array>, count: number): Promise<string[]> {
	const reader = stream.getReader();
	const out: string[] = [];
	for (let i = 0; i < count; i++) {
		const { done, value } = await reader.read();
		if (done) break;
		out.push(decoder.decode(value));
	}
	reader.releaseLock();
	return out;
}

describe('withHeartbeat', () => {
	it('emits a comment line when the upstream says nothing', async () => {
		// The failure this prevents: a quiet stream reads as a dead connection to
		// nginx, which closes it after proxy_read_timeout.
		const upstream = controllable();
		const stream = withHeartbeat(upstream.stream, () => {}, 5);

		const chunks = await readChunks(stream, 2);

		expect(chunks).toEqual([': keep-alive\n\n', ': keep-alive\n\n']);
	});

	it('emits nothing but a comment, so EventSource ignores it', async () => {
		const upstream = controllable();
		const stream = withHeartbeat(upstream.stream, () => {}, 5);

		const [beat] = await readChunks(stream, 1);

		// The EventSource parser discards any line starting with ':'.
		expect(beat.startsWith(':')).toBe(true);
		expect(beat).not.toContain('data:');
	});

	it('passes upstream chunks through untouched', async () => {
		const upstream = controllable();
		const stream = withHeartbeat(upstream.stream, () => {}, 10_000);
		upstream.send('data: {"a":1}\n\n');

		const chunks = await readChunks(stream, 1);

		expect(chunks).toEqual(['data: {"a":1}\n\n']);
	});

	it('does not lose or reorder a chunk that arrives after a heartbeat', async () => {
		// The read outstanding when the idle timer fires has to be kept, not
		// restarted, or its chunk is consumed by a read nobody is waiting on.
		const upstream = controllable();
		const stream = withHeartbeat(upstream.stream, () => {}, 5);
		const reader = stream.getReader();

		expect(decoder.decode((await reader.read()).value)).toBe(': keep-alive\n\n');

		upstream.send('data: first\n\n');
		upstream.send('data: second\n\n');

		const seen: string[] = [];
		while (seen.length < 2) {
			const { value } = await reader.read();
			const text = decoder.decode(value);
			if (!text.startsWith(':')) seen.push(text);
		}

		expect(seen).toEqual(['data: first\n\n', 'data: second\n\n']);
	});

	it('closes when the upstream ends', async () => {
		const upstream = controllable();
		const stream = withHeartbeat(upstream.stream, () => {}, 10_000);
		upstream.close();

		const reader = stream.getReader();

		expect(await reader.read()).toEqual({ done: true, value: undefined });
	});

	it('aborts the upstream when the client goes away', async () => {
		const onCancel = vi.fn();
		const upstream = controllable();
		const stream = withHeartbeat(upstream.stream, onCancel, 10_000);

		await stream.cancel('client gone');

		expect(onCancel).toHaveBeenCalledOnce();
	});

	it('beats well inside the 60s timeout proxies default to', () => {
		expect(HEARTBEAT_INTERVAL_MS).toBeLessThan(60_000 / 2);
	});
});
