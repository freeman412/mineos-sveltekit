import { isDemoMode } from '$lib/demo/mode';

export const ssr = !isDemoMode;
