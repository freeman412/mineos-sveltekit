import { describe, expect, it } from 'vitest';
import { buildTabs, type TabContext } from './serverTabs';

const base: TabContext = {
	name: 'hub',
	serverType: 'java',
	isModded: false,
	isPluginServer: false
};

const labels = (ctx: TabContext) => buildTabs(ctx).map((t) => t.label);
const enabled = (ctx: TabContext) => buildTabs(ctx).filter((t) => !t.disabled).map((t) => t.label);
const hrefFor = (ctx: TabContext, label: string) =>
	buildTabs(ctx).find((t) => t.label === label)?.href;

describe('buildTabs — game servers', () => {
	it('keeps the existing tab set', () => {
		expect(labels(base)).toEqual([
			'Dashboard',
			'Properties',
			'Config',
			'Logs',
			'Backups',
			'Archives',
			'Files',
			'Performance',
			'Worlds',
			'Players',
			'Mods',
			'Plugins',
			'Cron Jobs'
		]);
	});

	it('points at the /servers section', () => {
		expect(hrefFor(base, 'Dashboard')).toBe('/servers/hub');
		expect(hrefFor(base, 'Properties')).toBe('/servers/hub/config');
		expect(hrefFor(base, 'Logs')).toBe('/servers/hub/console');
	});

	it('disables mods until the server is actually modded', () => {
		expect(enabled(base)).not.toContain('Mods');
		expect(enabled({ ...base, isModded: true })).toContain('Mods');
	});

	it('disables plugins until the server is a plugin server', () => {
		expect(enabled(base)).not.toContain('Plugins');
		expect(enabled({ ...base, isPluginServer: true })).toContain('Plugins');
	});

	it('strips java-only tabs for bedrock', () => {
		const bedrock = enabled({ ...base, serverType: 'bedrock' });
		expect(bedrock).not.toContain('Config');
		expect(bedrock).not.toContain('Players');
		expect(bedrock).not.toContain('Mods');
		expect(bedrock).not.toContain('Plugins');
	});
});

/**
 * A proxy is a config file and a routing table: no world, no server.properties,
 * no player data. It gets the tabs that mean something and none that don't —
 * every tab here is enabled, because a greyed-out tab is just clutter.
 */
describe('buildTabs — proxies', () => {
	const proxy: TabContext = { ...base, serverType: 'proxy' };

	it('offers exactly the tabs a proxy can use', () => {
		expect(labels(proxy)).toEqual([
			'Overview',
			'Properties',
			'Plugins',
			'Files',
			'Logs',
			'Config',
			'Performance',
			'Cron Jobs',
			'Backups'
		]);
	});

	it('leaves nothing disabled', () => {
		expect(buildTabs(proxy).every((t) => !t.disabled)).toBe(true);
	});

	it('lives entirely under /proxies', () => {
		expect(buildTabs(proxy).every((t) => t.href.startsWith('/proxies/hub'))).toBe(true);
	});

	it('sends Properties to the velocity/bungee editor, not server.properties', () => {
		expect(hrefFor(proxy, 'Properties')).toBe('/proxies/hub/proxy-config');
	});

	it('drops worlds, players, mods and archives entirely', () => {
		const shown = labels(proxy);
		for (const gone of ['Worlds', 'Players', 'Mods', 'Archives']) {
			expect(shown).not.toContain(gone);
		}
	});

	it('encodes names that need it', () => {
		expect(hrefFor({ ...proxy, name: 'my hub' }, 'Files')).toBe('/proxies/my%20hub/files');
	});
});
