import type { ServerDetail } from '$lib/api/types';

/**
 * What every server/proxy detail panel receives.
 *
 * The panels used to live under routes/(app)/servers/[name]/ and type this
 * via ./$types. They are shared components now — /servers/[name] and
 * /proxies/[name] both render them — so the shape is declared here instead
 * of being inferred from one route's generated types.
 */
export type ServerPanelData = {
	server: ServerDetail | null;
};
