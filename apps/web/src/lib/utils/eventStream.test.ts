import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createEventStream } from './eventStream';

/**
 * Minimal EventSource stand-in. createEventStream reads EventSource off
 * the global at call time, so swapping it in per-test needs no mocking
 * library and works in any environment.
 */
class FakeEventSource {
	static instances: FakeEventSource[] = [];
	static reset() {
		FakeEventSource.instances = [];
	}

	url: string;
	closed = false;
	onmessage: ((event: { data: string }) => void) | null = null;
	onopen: (() => void) | null = null;
	onerror: ((event: Event) => void) | null = null;

	constructor(url: string) {
		this.url = url;
		FakeEventSource.instances.push(this);
	}

	close() {
		this.closed = true;
	}

	emitMessage(data: unknown) {
		this.onmessage?.({ data: JSON.stringify(data) });
	}

	emitOpen() {
		this.onopen?.();
	}

	emitError() {
		this.onerror?.(new Event('error'));
	}
}

beforeEach(() => {
	FakeEventSource.reset();
	vi.stubGlobal('EventSource', FakeEventSource);
	vi.useFakeTimers();
});

afterEach(() => {
	vi.unstubAllGlobals();
	vi.useRealTimers();
});

describe('createEventStream', () => {
	it('delivers parsed messages to onMessage', () => {
		const seen: string[] = [];
		createEventStream<string>({ url: '/stream', onMessage: (d) => seen.push(d) });

		FakeEventSource.instances[0].emitMessage('hello');

		expect(seen).toEqual(['hello']);
	});

	it('closes on the first error when reconnect was not requested', () => {
		const events: string[] = [];
		createEventStream<string>({
			url: '/stream',
			onMessage: () => {},
			onClose: () => events.push('close')
		});

		FakeEventSource.instances[0].emitError();

		expect(FakeEventSource.instances[0].closed).toBe(true);
		expect(events).toEqual(['close']);
	});

	it('reconnects after the requested delay instead of giving up', () => {
		const events: string[] = [];
		createEventStream<string>({
			url: '/stream',
			onMessage: () => {},
			onClose: () => events.push('close'),
			reconnect: { initialDelayMs: 1000 }
		});

		FakeEventSource.instances[0].emitError();
		expect(events).toEqual([]);
		expect(FakeEventSource.instances).toHaveLength(1);

		vi.advanceTimersByTime(999);
		expect(FakeEventSource.instances).toHaveLength(1);

		vi.advanceTimersByTime(1);
		expect(FakeEventSource.instances).toHaveLength(2);
		expect(FakeEventSource.instances[1].url).toBe('/stream');
		expect(events).toEqual([]);
	});

	it('backs off exponentially across consecutive failures', () => {
		createEventStream<string>({
			url: '/stream',
			onMessage: () => {},
			reconnect: { initialDelayMs: 1000 }
		});

		FakeEventSource.instances[0].emitError();
		vi.advanceTimersByTime(1000);
		expect(FakeEventSource.instances).toHaveLength(2);

		FakeEventSource.instances[1].emitError();
		vi.advanceTimersByTime(1999);
		expect(FakeEventSource.instances).toHaveLength(2);
		vi.advanceTimersByTime(1);
		expect(FakeEventSource.instances).toHaveLength(3);
	});

	it('resets the backoff after a successful connection', () => {
		createEventStream<string>({
			url: '/stream',
			onMessage: () => {},
			reconnect: { initialDelayMs: 1000 }
		});

		FakeEventSource.instances[0].emitError();
		vi.advanceTimersByTime(1000);
		FakeEventSource.instances[1].emitOpen();

		FakeEventSource.instances[1].emitError();
		vi.advanceTimersByTime(999);
		expect(FakeEventSource.instances).toHaveLength(2);
		vi.advanceTimersByTime(1);
		expect(FakeEventSource.instances).toHaveLength(3);
	});

	it('gives up after maxAttempts reconnections and reports closure', () => {
		const events: string[] = [];
		createEventStream<string>({
			url: '/stream',
			onMessage: () => {},
			onClose: () => events.push('close'),
			reconnect: { initialDelayMs: 1000, maxAttempts: 1 }
		});

		FakeEventSource.instances[0].emitError();
		vi.advanceTimersByTime(1000);
		expect(events).toEqual([]);

		FakeEventSource.instances[1].emitError();
		expect(events).toEqual(['close']);

		vi.advanceTimersByTime(60_000);
		expect(FakeEventSource.instances).toHaveLength(2);
	});

	it('a manual close stops pending reconnections and closes once', () => {
		const events: string[] = [];
		const handle = createEventStream<string>({
			url: '/stream',
			onMessage: () => {},
			onClose: () => events.push('close'),
			reconnect: { initialDelayMs: 1000 }
		});

		FakeEventSource.instances[0].emitError();
		handle.close();
		vi.advanceTimersByTime(60_000);

		expect(FakeEventSource.instances).toHaveLength(1);
		expect(events).toEqual(['close']);
	});
});
