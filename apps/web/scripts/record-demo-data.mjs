import { mkdir, writeFile } from 'node:fs/promises';
import { dirname, join } from 'node:path';

const baseUrl = process.env.DEMO_API_BASE_URL || 'http://localhost:5078';
const apiKey = process.env.DEMO_API_KEY || '';
const outputRoot = join(process.cwd(), 'static', 'demo-api');
const serverLimit = Number(process.env.DEMO_SERVER_LIMIT || '0');

const headers = apiKey ? { 'X-Api-Key': apiKey } : {};

const normalizeQuery = (search) => {
	if (!search) return '';
	const params = new URLSearchParams(search);
	const entries = Array.from(params.entries());
	if (entries.length === 0) return '';
	return entries
		.map(([key, value]) => `${key}=${value}`)
		.join('&')
		.replace(/[^a-zA-Z0-9._-]+/g, '_');
};

const toFilePath = (apiPath) => {
	const url = new URL(apiPath, 'http://demo.local');
	const querySuffix = normalizeQuery(url.search);
	const suffix = querySuffix ? `__${querySuffix}` : '';
	const cleanPath = url.pathname.replace(/^\/+/, '');
	return join(outputRoot, `${cleanPath}${suffix}.json`);
};

const redactKeys = ['password', 'secret', 'token', 'apikey', 'connectionstring', 'path'];

const sanitize = (value) => {
	if (Array.isArray(value)) {
		return value.map((item) => sanitize(item));
	}
	if (value && typeof value === 'object') {
		const result = {};
		for (const [key, val] of Object.entries(value)) {
			if (redactKeys.includes(key.toLowerCase())) {
				result[key] = 'REDACTED';
			} else {
				result[key] = sanitize(val);
			}
		}
		return result;
	}
	return value;
};

const fetchJson = async (apiPath) => {
	const res = await fetch(`${baseUrl}${apiPath}`, { headers });
	if (!res.ok) {
		throw new Error(`${apiPath} -> ${res.status}`);
	}
	return res.json();
};

const writeJson = async (apiPath, data) => {
	const filePath = toFilePath(apiPath);
	await mkdir(dirname(filePath), { recursive: true });
	await writeFile(filePath, JSON.stringify(sanitize(data), null, 2), 'utf8');
	console.log(`[demo] ${apiPath} -> ${filePath}`);
};

const record = async (apiPath) => {
	try {
		const data = await fetchJson(apiPath);
		await writeJson(apiPath, data);
	} catch (err) {
		console.warn(`[demo] skipped ${apiPath}: ${err instanceof Error ? err.message : 'error'}`);
	}
};

const hostEndpoints = [
	'/api/host/metrics',
	'/api/host/servers',
	'/api/host/profiles',
	'/api/host/imports',
	'/api/settings',
	'/api/settings/curseforge/status',
	'/api/notifications',
	'/api/jobs',
	'/api/watchdog/status',
	'/api/auth/me',
	'/api/auth/users'
];

for (const endpoint of hostEndpoints) {
	await record(endpoint);
}

const servers = await fetchJson('/api/servers/list').catch(() => []);
await writeJson('/api/servers/list', servers);

const serverNames = Array.isArray(servers)
	? servers.map((server) => server?.name).filter(Boolean)
	: [];
const limitedServers =
	serverLimit > 0 ? serverNames.slice(0, serverLimit) : serverNames;

for (const name of limitedServers) {
	const encoded = encodeURIComponent(name);
	await record(`/api/servers/${encoded}`);
	await record(`/api/servers/${encoded}/status`);
	await record(`/api/servers/${encoded}/server-properties`);
	await record(`/api/servers/${encoded}/server-config`);
	await record(`/api/servers/${encoded}/watchdog`);
	await record(`/api/servers/${encoded}/crashes?limit=25`);
	await record(`/api/servers/${encoded}/worlds`);
	await record(`/api/servers/${encoded}/archives`);
	await record(`/api/servers/${encoded}/backups`);
	await record(`/api/servers/${encoded}/client-packages`);
	await record(`/api/servers/${encoded}/mods/with-modpacks`);
	await record(`/api/servers/${encoded}/plugins`);
	await record(`/api/servers/${encoded}/performance/realtime`);
	await record(`/api/servers/${encoded}/performance/history?minutes=60`);
	await record(`/api/servers/${encoded}/performance/spark`);
	await record(`/api/servers/${encoded}/players`);
}

console.log('[demo] recording complete');
