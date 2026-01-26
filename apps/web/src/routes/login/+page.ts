import { redirect } from '@sveltejs/kit';
import { isDemoMode } from '$lib/demo/mode';
import { withBase } from '$lib/utils/paths';

export const load = () => {
	if (isDemoMode) {
		throw redirect(302, withBase('/dashboard'));
	}
	return {};
};
