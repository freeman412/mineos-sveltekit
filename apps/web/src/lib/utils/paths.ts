import { base } from '$app/paths';

export function withBase(path: string): string {
	if (!path.startsWith('/')) {
		return `${base}/${path}`.replace(/\/{2,}/g, '/');
	}
	return `${base}${path}`.replace(/\/{2,}/g, '/');
}
