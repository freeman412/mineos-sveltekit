import { createDemoFetch, resolveDemoPath } from './fetch';
import { isDemoMode } from './mode';

export function installDemoRuntime(): void {
	if (!isDemoMode) return;
	if (typeof window === 'undefined') return;
	if ((window as { __mineosDemoInstalled?: boolean }).__mineosDemoInstalled) return;

	const originalFetch = window.fetch.bind(window);
	window.fetch = createDemoFetch(originalFetch) as typeof window.fetch;

	class DemoEventSource {
		url: string;
		readyState = 2;
		onopen: ((event: Event) => void) | null = null;
		onmessage: ((event: MessageEvent) => void) | null = null;
		onerror: ((event: Event) => void) | null = null;
		constructor(url: string) {
			this.url = url;
		}
		close() {}
	}

	window.EventSource = DemoEventSource as typeof EventSource;
	(window as { __mineosDemoInstalled?: boolean }).__mineosDemoInstalled = true;
}

export function getDemoJsonUrl(input: RequestInfo | URL): string | null {
	const url =
		typeof input === 'string'
			? new URL(input, globalThis.location?.origin ?? 'http://localhost')
			: input instanceof URL
				? input
				: new URL(input.url);
	return resolveDemoPath(url);
}
