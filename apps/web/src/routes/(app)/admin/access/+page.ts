import type { PageLoad } from './$types';
import { demoLoad } from '$lib/demo/loaders';

export const load: PageLoad = ({ fetch }) =>
	demoLoad(fetch, async (demoFetch) => {
		const usersRes = await demoFetch('/api/auth/users');
		const users = usersRes.ok ? await usersRes.json() : null;
		const serversRes = await demoFetch('/api/servers/list');
		const servers = serversRes.ok ? await serversRes.json() : null;

		return {
			users: {
				data: users,
				error: usersRes.ok ? null : `Failed to load users (${usersRes.status})`
			},
			servers: {
				data: servers,
				error: serversRes.ok ? null : `Failed to load servers (${serversRes.status})`
			}
		};
	});
