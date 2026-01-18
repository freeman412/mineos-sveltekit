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
	/** Optional callback when connection closes (either by error or manually) */
	onClose?: () => void;
}

/**
 * Result of creating an event stream
 */
export interface EventStreamHandle {
	/** Close the event stream */
	close: () => void;
	/** The underlying EventSource (for advanced use) */
	source: EventSource;
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
 *   onError: () => console.log('Connection lost')
 * });
 *
 * // Later, to cleanup:
 * stream.close();
 * ```
 */
export function createEventStream<T>(options: EventStreamOptions<T>): EventStreamHandle {
	const { url, onMessage, onOpen, onError, onClose } = options;

	const source = new EventSource(url);

	source.onmessage = (event) => {
		try {
			const data = JSON.parse(event.data) as T;
			onMessage(data);
		} catch (err) {
			console.error('Failed to parse SSE message:', err);
		}
	};

	source.onopen = () => {
		onOpen?.();
	};

	source.onerror = (event) => {
		onError?.(event);
		// EventSource auto-reconnects on error, but we close to avoid infinite retries
		source.close();
		onClose?.();
	};

	const close = () => {
		source.close();
		onClose?.();
	};

	return { close, source };
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
