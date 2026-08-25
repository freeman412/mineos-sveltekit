import * as api from '$lib/api/client';
import type { Fetcher } from '$lib/api/client';
import {
	addBackendToBungee,
	addBackendToVelocity,
	backendAddress,
	removeBackendFromBungee,
	removeBackendFromVelocity
} from './proxy';

/**
 * Result of linking a game server to/from a proxy. `ok: false` carries a
 * human-facing explanation; partial progress is described in the message
 * (e.g. attached but forwarding not secured).
 */
export type ProxyLinkResult = { ok: true } | { ok: false; error: string };

export interface ProxyLinkOptions {
	serverName: string;
	proxyName: string;
	/** Progress captions for long-running UIs (the create wizard's step text). */
	onStep?: (label: string) => void;
}

/**
 * The name a backend is registered under in a proxy's config.
 *
 * This is player-facing — it is what someone types in Velocity's `/server <name>` — so
 * it comes from the display label rather than the directory. Directories are slugs with
 * a random suffix now, and `/server server-loco-7f3a` is not something to ask a player
 * to type. Falls back to the directory name when a label has nothing usable in it.
 */
function backendKeyFor(serverName: string, displayName: string | null | undefined): string {
	const slug = (displayName ?? '')
		.trim()
		.toLowerCase()
		.replace(/[^a-z0-9]+/g, '-')
		.replace(/^-+|-+$/g, '');
	return slug.length > 0 ? slug : serverName;
}

/**
 * A key that does not collide with an existing entry pointing somewhere else. Two
 * servers can carry the same label — it is only a label — but two backends cannot share
 * a key.
 */
function uniqueBackendKey(desired: string, taken: Record<string, string>, address: string): string {
	if (!(desired in taken) || taken[desired] === address) return desired;
	for (let n = 2; ; n++) {
		const candidate = `${desired}-${n}`;
		if (!(candidate in taken) || taken[candidate] === address) return candidate;
	}
}

/**
 * The key an already-attached server sits under. Matched by address rather than by name:
 * the key is derived from a label that can change after attaching, so re-deriving it
 * would miss a renamed server. Falls back to the directory name for entries written
 * before keys were labels.
 */
function existingBackendKey(
	servers: Record<string, string>,
	address: string | null,
	serverName: string
): string | null {
	if (address) {
		const byAddress = Object.keys(servers).find((key) => servers[key] === address);
		if (byAddress) return byAddress;
	}
	return serverName in servers ? serverName : null;
}

async function findBackend(
	fetcher: Fetcher,
	serverName: string
): Promise<{ address: string | null; displayName: string | null }> {
	const host = await api.getHostServers(fetcher);
	const summaryRow = (host.data ?? []).find((s) => s.name === serverName);
	return {
		address: backendAddress(summaryRow?.port),
		displayName: summaryRow?.displayName ?? null
	};
}

/**
 * Register a game server with a proxy (Velocity's structured config first,
 * BungeeCord's as fallback), then let the proxy vouch for its players:
 * installing the forwarding mod and securing identity where the backend
 * supports it.
 */
export async function attachServerToProxy(
	fetcher: Fetcher,
	options: ProxyLinkOptions
): Promise<ProxyLinkResult> {
	const { serverName, proxyName, onStep } = options;
	onStep?.(`Attaching ${serverName} to ${proxyName}...`);

	const { address, displayName } = await findBackend(fetcher, serverName);
	if (!address) {
		return { ok: false, error: `Couldn't find an assigned port for ${serverName}, so it wasn't attached.` };
	}

	// Prefer Velocity's structured config; fall back to BungeeCord's.
	// Both endpoints replace the whole config, so this read-modify-write loses
	// any edit saved from the properties editor between the GET and the PUT.
	// Acceptable while attaching is a deliberate, one-at-a-time action.
	let configError: string | null = null;
	const velocity = await api.getVelocityConfig(fetcher, proxyName);
	if (velocity.data?.exists) {
		const { error } = await api.updateVelocityConfig(
			fetcher,
			proxyName,
			addBackendToVelocity(
				velocity.data,
				uniqueBackendKey(backendKeyFor(serverName, displayName), velocity.data.servers, address),
				address
			)
		);
		configError = error;
	} else {
		const bungee = await api.getBungeeConfig(fetcher, proxyName);
		if (bungee.data?.exists) {
			const { error } = await api.updateBungeeConfig(
				fetcher,
				proxyName,
				addBackendToBungee(bungee.data, serverName, address)
			);
			configError = error;
		} else {
			configError = `couldn't load ${proxyName}'s proxy config`;
		}
	}
	if (configError) {
		return { ok: false, error: `Couldn't update ${proxyName}'s config: ${configError}` };
	}

	// Where the backend supports it, let the proxy vouch for players.
	onStep?.('Setting up verified forwarding...');
	const statusResult = await api.getForwardingStatus(fetcher, serverName);
	if (statusResult.error || !statusResult.data) {
		// Registered but unverified: without the status we cannot tell whether
		// this backend still needs securing, and silently reporting success
		// would leave it open to impersonation with nothing said.
		return {
			ok: false,
			error: `Attached, but couldn't check forwarding status: ${statusResult.error ?? 'no response'}. ${serverName} may still be open to impersonation.`
		};
	}
	const remediation = statusResult.data.remediationAction;
	if (remediation === 'install-mod') {
		const modResult = await api.installForwardingMod(fetcher, serverName);
		if (modResult.error) {
			return { ok: false, error: `Attached, but installing the forwarding mod failed: ${modResult.error}` };
		}
	}
	if (remediation === 'secure' || remediation === 'install-mod') {
		const secureResult = await api.secureBackend(fetcher, serverName);
		if (secureResult.error) {
			return { ok: false, error: `Attached, but securing forwarding failed: ${secureResult.error}` };
		}
	}
	return { ok: true };
}

/**
 * Remove a game server from a proxy's backend list. Only edits the proxy's
 * config; any installed forwarding mod or secured secret stays in place on
 * the backend, where it does no harm unattached.
 */
export async function detachServerFromProxy(
	fetcher: Fetcher,
	options: ProxyLinkOptions
): Promise<ProxyLinkResult> {
	const { serverName, proxyName } = options;

	const velocity = await api.getVelocityConfig(fetcher, proxyName);
	if (velocity.data?.exists) {
		// Fast path: entries written before keys were labels sit under the directory
		// name, and cost no extra request to find.
		let key: string | null = serverName in velocity.data.servers ? serverName : null;
		if (key === null) {
			const { address } = await findBackend(fetcher, serverName);
			key = existingBackendKey(velocity.data.servers, address, serverName);
		}
		if (key === null) {
			return { ok: false, error: `${serverName} isn't attached to ${proxyName}.` };
		}
		const { error } = await api.updateVelocityConfig(
			fetcher,
			proxyName,
			removeBackendFromVelocity(velocity.data, key)
		);
		if (error) return { ok: false, error: `Couldn't update ${proxyName}'s config: ${error}` };
		return { ok: true };
	}

	const bungee = await api.getBungeeConfig(fetcher, proxyName);
	if (bungee.data?.exists) {
		if (!(serverName in bungee.data.servers)) {
			return { ok: false, error: `${serverName} isn't attached to ${proxyName}.` };
		}
		const { error } = await api.updateBungeeConfig(
			fetcher,
			proxyName,
			removeBackendFromBungee(bungee.data, serverName)
		);
		if (error) return { ok: false, error: `Couldn't update ${proxyName}'s config: ${error}` };
		return { ok: true };
	}

	return { ok: false, error: `couldn't load ${proxyName}'s proxy config` };
}
