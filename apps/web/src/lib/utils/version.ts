/**
 * Minecraft version parsing and comparison utilities
 */

export interface ParsedVersion {
	major: number;
	minor: number;
	patch: number;
	isSnapshot: boolean;
	snapshotYear?: number;
	snapshotWeek?: number;
	snapshotLetter?: string;
	original: string;
}

/**
 * Parse a Minecraft version string into components
 * Supports formats like: 1.20.1, 1.19, 23w31a (snapshots), etc.
 */
export function parseMinecraftVersion(version: string): ParsedVersion | null {
	if (!version) return null;

	// Clean up the version string
	const cleaned = version.trim().toLowerCase();

	// Check for snapshot format (e.g., 23w31a, 24w12a)
	const snapshotMatch = cleaned.match(/^(\d{2})w(\d{2})([a-z])$/);
	if (snapshotMatch) {
		return {
			major: 0,
			minor: 0,
			patch: 0,
			isSnapshot: true,
			snapshotYear: parseInt(snapshotMatch[1], 10),
			snapshotWeek: parseInt(snapshotMatch[2], 10),
			snapshotLetter: snapshotMatch[3],
			original: version
		};
	}

	// Parse standard version format (1.20.1, 1.19, etc.)
	const versionMatch = cleaned.match(/^(\d+)\.(\d+)(?:\.(\d+))?/);
	if (versionMatch) {
		return {
			major: parseInt(versionMatch[1], 10),
			minor: parseInt(versionMatch[2], 10),
			patch: versionMatch[3] ? parseInt(versionMatch[3], 10) : 0,
			isSnapshot: false,
			original: version
		};
	}

	return null;
}

/**
 * Compare two Minecraft versions
 * Returns: -1 if a < b, 0 if a === b, 1 if a > b
 */
export function compareVersions(a: string, b: string): number {
	const parsedA = parseMinecraftVersion(a);
	const parsedB = parseMinecraftVersion(b);

	if (!parsedA || !parsedB) return 0;

	// Snapshots are considered newer than releases for the same version
	if (!parsedA.isSnapshot && !parsedB.isSnapshot) {
		// Compare release versions
		if (parsedA.major !== parsedB.major) return parsedA.major - parsedB.major;
		if (parsedA.minor !== parsedB.minor) return parsedA.minor - parsedB.minor;
		return parsedA.patch - parsedB.patch;
	}

	if (parsedA.isSnapshot && parsedB.isSnapshot) {
		// Compare snapshots
		if (parsedA.snapshotYear !== parsedB.snapshotYear) {
			return (parsedA.snapshotYear || 0) - (parsedB.snapshotYear || 0);
		}
		if (parsedA.snapshotWeek !== parsedB.snapshotWeek) {
			return (parsedA.snapshotWeek || 0) - (parsedB.snapshotWeek || 0);
		}
		// Compare letters (a < b < c)
		if (parsedA.snapshotLetter && parsedB.snapshotLetter) {
			return parsedA.snapshotLetter.localeCompare(parsedB.snapshotLetter);
		}
		return 0;
	}

	// One is snapshot, one is release - this is complex, just consider them equal
	return 0;
}

/**
 * Check if a mod version is compatible with a server version
 */
export function isVersionCompatible(
	serverVersion: string,
	modGameVersions: string[]
): boolean {
	if (!serverVersion || !modGameVersions || modGameVersions.length === 0) {
		return true; // Can't determine, assume compatible
	}

	const parsed = parseMinecraftVersion(serverVersion);
	if (!parsed) return true;

	// Check if any of the mod's supported versions match the server version
	return modGameVersions.some((modVersion) => {
		const modParsed = parseMinecraftVersion(modVersion);
		if (!modParsed) return false;

		// Exact match
		if (
			parsed.major === modParsed.major &&
			parsed.minor === modParsed.minor &&
			parsed.patch === modParsed.patch
		) {
			return true;
		}

		// Some mods support version ranges (e.g., "1.20.x")
		// If patch is 0 in mod version, it might support all patches
		if (
			modParsed.patch === 0 &&
			parsed.major === modParsed.major &&
			parsed.minor === modParsed.minor
		) {
			return true;
		}

		return false;
	});
}

/**
 * Extract the most likely Minecraft version from various sources
 */
export function extractMinecraftVersion(
	serverVersion?: string | null,
	profile?: string | null,
	jarFile?: string | null
): string | null {
	// Try server version from ping/status
	if (serverVersion) {
		const parsed = parseMinecraftVersion(serverVersion);
		if (parsed) return serverVersion;
	}

	// Try to extract from profile name
	if (profile) {
		const versionMatch = profile.match(/(\d+\.\d+(?:\.\d+)?)/);
		if (versionMatch) return versionMatch[1];
	}

	// Try to extract from jar file
	if (jarFile) {
		const versionMatch = jarFile.match(/(\d+\.\d+(?:\.\d+)?)/);
		if (versionMatch) return versionMatch[1];
	}

	return null;
}

/**
 * Get compatibility status badge info
 */
export function getCompatibilityBadge(
	serverVersion: string,
	modGameVersions: string[]
): { variant: 'success' | 'warning' | 'error'; label: string } {
	const compatible = isVersionCompatible(serverVersion, modGameVersions);

	if (compatible) {
		return { variant: 'success', label: 'Compatible' };
	}

	return { variant: 'warning', label: 'May not work' };
}

/**
 * Filter and sort versions by compatibility with server version
 */
export function sortVersionsByCompatibility(
	versions: string[],
	serverVersion: string
): string[] {
	const parsed = parseMinecraftVersion(serverVersion);
	if (!parsed) return versions;

	return [...versions].sort((a, b) => {
		const aCompatible = isVersionCompatible(serverVersion, [a]);
		const bCompatible = isVersionCompatible(serverVersion, [b]);

		// Compatible versions first
		if (aCompatible && !bCompatible) return -1;
		if (!aCompatible && bCompatible) return 1;

		// Then sort by version number (newest first)
		return compareVersions(b, a);
	});
}
