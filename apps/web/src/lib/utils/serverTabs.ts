export type Tab = {
	href: string;
	label: string;
	exact?: boolean;
	disabled?: boolean;
	tooltip?: string;
};

export type TabContext = {
	name: string;
	serverType: string | undefined;
	isModded: boolean;
	isPluginServer: boolean;
};

/**
 * The tab strip for a server's detail page.
 *
 * Game servers keep the historical set, greyed-out entries and all: a vanilla
 * server showing a disabled Mods tab tells you mods exist and why you can't
 * use them. Proxies get a different list rather than the same one with holes
 * punched in it — a proxy has no world, no server.properties and no player
 * data, so those tabs were never anything but dead weight.
 */
export function buildTabs(ctx: TabContext): Tab[] {
	const s = encodeURIComponent(ctx.name);
	const isProxy = ctx.serverType === 'proxy';
	const isBedrock = ctx.serverType === 'bedrock';

	if (isProxy) {
		const base = `/proxies/${s}`;
		return [
			{ href: base, label: 'Overview', exact: true },
			// A proxy's properties are velocity.toml / config.yml, not server.properties.
			{ href: `${base}/proxy-config`, label: 'Properties' },
			{ href: `${base}/plugins`, label: 'Plugins' },
			{ href: `${base}/files`, label: 'Files' },
			{ href: `${base}/logs`, label: 'Logs' },
			{ href: `${base}/advanced`, label: 'Config' },
			{ href: `${base}/performance`, label: 'Performance' },
			{ href: `${base}/cron`, label: 'Cron Jobs' },
			// Worth keeping for one file: losing forwarding.secret silently breaks
			// forwarding on every attached backend, and there is no other copy.
			{ href: `${base}/backups`, label: 'Backups' }
		];
	}

	const base = `/servers/${s}`;
	return [
		{ href: base, label: 'Dashboard', exact: true },
		{ href: `${base}/config`, label: 'Properties' },
		{
			href: `${base}/advanced`,
			label: 'Config',
			disabled: isBedrock,
			tooltip: 'Bedrock servers do not use Java configuration'
		},
		{ href: `${base}/console`, label: 'Logs' },
		{ href: `${base}/backups`, label: 'Backups' },
		{ href: `${base}/archives`, label: 'Archives' },
		{ href: `${base}/files`, label: 'Files' },
		{ href: `${base}/performance`, label: 'Performance' },
		{ href: `${base}/worlds`, label: 'Worlds' },
		{
			href: `${base}/players`,
			label: 'Players',
			disabled: isBedrock,
			tooltip: isBedrock ? 'Player management is not available for Bedrock servers' : undefined
		},
		{
			href: `${base}/mods`,
			label: 'Mods',
			disabled: isBedrock || !ctx.isModded,
			tooltip: isBedrock
				? 'Bedrock servers do not support Java mods'
				: 'Mods require a modded server (Forge, Fabric, NeoForge, or Quilt)'
		},
		{
			href: `${base}/plugins`,
			label: 'Plugins',
			disabled: isBedrock || !ctx.isPluginServer,
			tooltip: isBedrock
				? 'Bedrock servers do not support Java plugins'
				: 'Plugins require a plugin server (Paper, Spigot, Purpur, or Bukkit)'
		},
		{ href: `${base}/cron`, label: 'Cron Jobs' }
	];
}
