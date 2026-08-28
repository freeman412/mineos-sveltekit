import type { PageServerLoad } from './$types';
import { loadOverview } from '$lib/loads/overview';

export const load: PageServerLoad = (event) => loadOverview(event);
