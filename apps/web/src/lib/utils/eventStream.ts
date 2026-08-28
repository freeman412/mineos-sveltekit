/**
 * Options for creating an event stream
 */
export interface EventStreamOptions<T> {
	/** URL of the SSE endpoint */
	url: string;
	/** Callback when data is received (already parsed as JSON) */
	onMessage: (data: T) => void;
	/** Optional callback when connection opens */
	onOpen?: () => void;
	/** Optional callback when an error occurs */
	onError?: (error: Event) => void;
	/** Optional callback when connection closes for good (given up or manually) */
	onClose?: () => void;
	/**
	 * Reconnect instead of giving up on the first error. Without it a single
	 * dropped stream silently stops delivering updates until the next reload.
	 */
	reconnect?: {
		/** Delay before the first retry in ms; doubles per consecutive failure. Default 1000. */
		initialDelayMs?: number;
		/** Reconnection attempts before giving up and calling onClose. Default 5. */
		maxAttempts?: number;
	};
}

/**
 * Result of creating an event stream
 */
export interface EventStreamHandle {
	/** Close the event stream */
	close: () => void;
	/** The underlying EventSource (for advanced use); tracks reconnections */
	readonly source: EventSource;
}

/**
 * Creates a managed EventSource connection with automatic JSON parsing.
 *
 * @example
 * ```ts
 * const stream = createEventStream<ServerSummary[]>({
 *   url: '/api/host/servers/stream',
 *   onMessage: (servers) => {
 *     // servers is already parsed JSON
 *     myServers = servers;
 *   },
 *   reconnect: {},
 *   onClose: () => console.log('Stream gave up')
 * });
 *
 * // Later, to cleanup:
 * stream.close();
 * ```
 */
export function createEventStream<T>(options: EventStreamOptions<T>): EventStreamHandle {
	const { url, onMessage, onOpen, onError, onClose, reconnect } = options;
	const initialDelayMs = reconnect?.initialDelayMs ?? 1000;
	const maxAttempts = reconnect?.maxAttempts ?? 5;

	let closedManually = false;
	let attempts = 0;
	let source = connect();
	let reconnectTimer: ReturnType<typeof setTimeout> | null = null;

	function connect(): EventSource {
		const s = new EventSource(url);

		s.onmessage = (event) => {
			try {
				const data = JSON.parse(event.data) as T;
				onMessage(data);
			} catch (err) {
				console.error('Failed to parse SSE message:', err);
			}
		};

		s.onopen = () => {
			attempts = 0;
			onOpen?.();
		};

		s.onerror = (event) => {
			onError?.(event);
			s.close();
			if (closedManually) return;

			if (!reconnect) {
				// EventSource auto-reconnects natively; we close to avoid unbounded retries.
				onClose?.();
				return;
			}
			if (attempts >= maxAttempts) {
				onClose?.();
				return;
			}
			attempts += 1;
			reconnectTimer = setTimeout(() => {
				reconnectTimer = null;
				source = connect();
			}, initialDelayMs * 2 ** (attempts - 1));
		};

		return s;
	}

	return {
		get source() {
			return source;
		},
		close() {
			closedManually = true;
			if (reconnectTimer) {
				clearTimeout(reconnectTimer);
				reconnectTimer = null;
			}
			source.close();
			onClose?.();
		}
	};
}

/**
 * Creates an event stream that automatically closes after a terminal status.
 * Useful for job progress streams that complete or fail.
 *
 * @example
 * ```ts
 * const stream = createJobStream<JobProgress>({
 *   url: `/api/jobs/${jobId}/stream`,
 *   onMessage: (progress) => {
 *     jobStatus = progress;
 *   },
 *   isComplete: (progress) => progress.status === 'completed' || progress.status === 'failed',
 *   onComplete: () => loadBackups()
 * });
 * ```
 */
export function createJobStream<T>(
	options: EventStreamOptions<T> & {
		/** Function to determine if the stream should close */
		isComplete: (data: T) => boolean;
		/** Optional callback when job completes */
		onComplete?: (data: T) => void;
	}
): EventStreamHandle {
	const { isComplete, onComplete, onMessage, ...rest } = options;

	let handle: EventStreamHandle;

	handle = createEventStream<T>({
		...rest,
		onMessage: (data) => {
			onMessage(data);
			if (isComplete(data)) {
				onComplete?.(data);
				handle.close();
			}
		}
	});

	return handle;
}
