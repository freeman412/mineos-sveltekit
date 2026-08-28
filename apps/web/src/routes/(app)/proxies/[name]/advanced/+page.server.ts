import type { PageServerLoad, Actions } from './$types';
import { loadJavaConfig, saveJavaConfig } from '$lib/loads/javaConfig';

export const load: PageServerLoad = (event) => loadJavaConfig(event);

export const actions = {
	default: (event) => saveJavaConfig(event)
} satisfies Actions;
