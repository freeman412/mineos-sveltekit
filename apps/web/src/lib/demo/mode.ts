import { PUBLIC_DEMO_MODE } from '$env/static/public';

export const isDemoMode =
	PUBLIC_DEMO_MODE === 'true' || PUBLIC_DEMO_MODE === '1' || PUBLIC_DEMO_MODE === 'yes';
