import type { PageServerLoad } from './$types';

export const load: PageServerLoad = async ({ fetch }) => {
	const [usersRes, hostUsersRes, hostGroupsRes] = await Promise.all([
		fetch('/api/auth/users'),
		fetch('/api/host/users'),
		fetch('/api/host/groups')
	]);

	const users = usersRes.ok ? await usersRes.json() : null;
	const hostUsers = hostUsersRes.ok ? await hostUsersRes.json() : null;
	const hostGroups = hostGroupsRes.ok ? await hostGroupsRes.json() : null;

	return {
		users: {
			data: users,
			error: usersRes.ok ? null : `Failed to load users (${usersRes.status})`
		},
		hostUsers: {
			data: hostUsers,
			error: hostUsersRes.ok ? null : `Failed to load host users (${hostUsersRes.status})`
		},
		hostGroups: {
			data: hostGroups,
			error: hostGroupsRes.ok ? null : `Failed to load host groups (${hostGroupsRes.status})`
		}
	};
};
