import type { PageServerLoad } from './$types';
import { loadPerformance } from '$lib/loads/performance';

export const load: PageServerLoad = (event) => loadPerformance(event);
