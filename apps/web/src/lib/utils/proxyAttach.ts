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

async function findBackendAddress(
	fetcher: Fetcher,
	serverName: string
): Promise<string | null> {
	const host = await api.getHostServers(fetcher);
	const summaryRow = (host.data ?? []).find((s) => s.name === serverName);
	return backendAddress(summaryRow?.port);
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

	const address = await findBackendAddress(fetcher, serverName);
	if (!address) {
		return { ok: false, error: `Couldn't find an assigned port for ${serverName}, so it wasn't attached.` };
	}

	// Prefer Velocity's structured config; fall back to BungeeCord's.
	let configError: string | null = null;
	const velocity = await api.getVelocityConfig(fetcher, proxyName);
	if (velocity.data?.exists) {
		const { error } = await api.updateVelocityConfig(
			fetcher,
			proxyName,
			addBackendToVelocity(velocity.data, serverName, address)
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
	const remediation = statusResult.data?.remediationAction;
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
		if (!(serverName in velocity.data.servers)) {
			return { ok: false, error: `${serverName} isn't attached to ${proxyName}.` };
		}
		const { error } = await api.updateVelocityConfig(
			fetcher,
			proxyName,
			removeBackendFromVelocity(velocity.data, serverName)
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
