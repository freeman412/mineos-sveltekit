import { env } from '$env/dynamic/public';

export const isDemoMode =
	env.PUBLIC_DEMO_MODE === 'true' ||
	env.PUBLIC_DEMO_MODE === '1' ||
	env.PUBLIC_DEMO_MODE === 'yes';
