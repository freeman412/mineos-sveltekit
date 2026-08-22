import { redirect } from '@sveltejs/kit';
import type { PageLoad } from './$types';

// Proxy properties moved to /proxies/<name>/proxy-config when proxies got
// their own section. Kept so links and bookmarks from before the move land
// on the editor instead of a 404.
export const load: PageLoad = ({ params }) => {
	redirect(308, `/proxies/${encodeURIComponent(params.name)}/proxy-config`);
};
