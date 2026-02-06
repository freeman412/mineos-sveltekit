<script lang="ts">
	import { invalidateAll } from '$app/navigation';
	import { modal } from '$lib/stores/modal';
	import { formatDateOnly } from '$lib/utils/formatting';
	import StatusBadge from '$lib/components/StatusBadge.svelte';
	import type { PageData } from './$types';

	interface User {
		id: number;
		username: string;
		role: string;
		isActive: boolean;
		createdAt: string;
		minecraftUsername?: string | null;
		minecraftUuid?: string | null;
		serverAccesses?: ServerAccess[];
	}

	interface ServerAccess {
		serverName: string;
		canView: boolean;
		canControl: boolean;
		canConsole: boolean;
	}

	interface ServerSummary {
		name: string;
	}

	let { data }: { data: PageData } = $props();
	let creating = $state(false);
	let formError = $state('');
	let username = $state('');
	let password = $state('');
	let role = $state('user');
	let minecraftUsername = $state('');
	let createAccesses = $state<ServerAccess[]>([]);

	let editingUser = $state<User | null>(null);
	let editPassword = $state('');
	let editRole = $state('');
	let editActive = $state(true);
	let editMinecraftUsername = $state('');
	let editAccesses = $state<ServerAccess[]>([]);
	let saving = $state(false);
	let deleting = $state<number | null>(null);

	$effect(() => {
		createAccesses = buildAccessDefaults();
	});

	function buildAccessDefaults(existing: ServerAccess[] = []) {
		const servers = (data.servers?.data as ServerSummary[] | null) ?? [];
		return servers.map((server) => {
			const match = existing.find((access) => access.serverName === server.name);
			return {
				serverName: server.name,
				canView: match?.canView ?? false,
				canControl: match?.canControl ?? false,
				canConsole: match?.canConsole ?? false
			};
		});
	}

	function normalizeAccesses(accesses: ServerAccess[]) {
		return accesses
			.map((access) => ({
				...access,
				canView: access.canView || access.canControl || access.canConsole
			}))
			.filter((access) => access.canView || access.canControl || access.canConsole);
	}

	async function createUser() {
		formError = '';
		if (!username.trim() || !password.trim()) {
			formError = 'Username and password are required.';
			return;
		}

		creating = true;
		try {
			const res = await fetch('/api/auth/users', {
				method: 'POST',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify({
					username: username.trim(),
					password: password.trim(),
					role,
					minecraftUsername: minecraftUsername.trim() || null,
					serverAccesses: normalizeAccesses(createAccesses)
				})
			});

			if (!res.ok) {
				const error = await res.json().catch(() => ({ error: 'Failed to create user' }));
				formError = error.error || 'Failed to create user';
			} else {
				username = '';
				password = '';
				role = 'user';
				minecraftUsername = '';
				createAccesses = buildAccessDefaults();
				await invalidateAll();
			}
		} finally {
			creating = false;
		}
	}

	function startEdit(user: User) {
		editingUser = user;
		editPassword = '';
		editRole = user.role;
		editActive = user.isActive;
		editMinecraftUsername = user.minecraftUsername ?? '';
		editAccesses = buildAccessDefaults(user.serverAccesses ?? []);
	}

	function cancelEdit() {
		editingUser = null;
		editPassword = '';
		editRole = '';
		editActive = true;
		editMinecraftUsername = '';
		editAccesses = [];
	}

	async function saveEdit() {
		if (!editingUser) return;

		saving = true;
		try {
			const payload: {
				password?: string;
				role?: string;
				isActive?: boolean;
				minecraftUsername?: string | null;
				serverAccesses?: ServerAccess[];
			} = {};

			if (editPassword.trim()) {
				payload.password = editPassword.trim();
			}
			if (editRole !== editingUser.role) {
				payload.role = editRole;
			}
			if (editActive !== editingUser.isActive) {
				payload.isActive = editActive;
			}
			const trimmedMinecraft = editMinecraftUsername.trim();
			const currentMinecraft = editingUser.minecraftUsername ?? '';
			if (trimmedMinecraft !== currentMinecraft) {
				payload.minecraftUsername = trimmedMinecraft || null;
			}

			payload.serverAccesses = normalizeAccesses(editAccesses);

			const res = await fetch(`/api/auth/users/${editingUser.id}`, {
				method: 'PATCH',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify(payload)
			});

			if (!res.ok) {
				const error = await res.json().catch(() => ({ error: 'Failed to update user' }));
				await modal.error(error.error || 'Failed to update user');
			} else {
				cancelEdit();
				await invalidateAll();
			}
		} finally {
			saving = false;
		}
	}

	async function toggleActive(user: User) {
		try {
			const res = await fetch(`/api/auth/users/${user.id}`, {
				method: 'PATCH',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify({ isActive: !user.isActive })
			});

			if (!res.ok) {
				const error = await res.json().catch(() => ({ error: 'Failed to update user' }));
				await modal.error(error.error || 'Failed to update user');
			} else {
				await invalidateAll();
			}
		} catch (err) {
			await modal.error('Failed to update user');
		}
	}

	async function deleteUser(user: User) {
		const confirmed = await modal.confirm(
			`Are you sure you want to delete user "${user.username}"? This action cannot be undone.`,
			'Delete User'
		);
		if (!confirmed) return;

		deleting = user.id;
		try {
			const res = await fetch(`/api/auth/users/${user.id}`, {
				method: 'DELETE'
			});

			if (!res.ok) {
				const error = await res.json().catch(() => ({ error: 'Failed to delete user' }));
				await modal.error(error.error || 'Failed to delete user');
			} else {
				await invalidateAll();
			}
		} finally {
			deleting = null;
		}
	}

</script>

<div class="page-header">
	<div>
		<h1>User Management</h1>
		<p class="subtitle">Manage application users and access control</p>
	</div>
</div>

<div class="users-layout">
	<section class="panel create-panel">
		<h2>Create New User</h2>
		<form
			class="create-form"
			onsubmit={(event) => {
				event.preventDefault();
				createUser();
			}}
		>
			<label>
				<span class="label-text">Username</span>
				<input type="text" bind:value={username} placeholder="Enter username" />
			</label>
			<label>
				<span class="label-text">Password</span>
				<input type="password" bind:value={password} placeholder="Enter password" />
			</label>
			<label>
				<span class="label-text">Minecraft Username (optional)</span>
				<input
					type="text"
					bind:value={minecraftUsername}
					placeholder="Link to Mojang username"
				/>
			</label>
			<label>
				<span class="label-text">Role</span>
				<select bind:value={role}>
					<option value="user">User</option>
					<option value="manager">Manager</option>
					<option value="admin">Admin</option>
				</select>
			</label>
			<div class="access-panel">
				<span class="label-text">Server Access</span>
				{#if data.servers?.error}
					<p class="error">{data.servers.error}</p>
				{:else if (data.servers?.data ?? []).length === 0}
					<p class="muted">No servers available yet.</p>
				{:else}
					<div class="access-grid">
						{#each createAccesses as access}
							<div class="access-row">
								<span class="access-name">{access.serverName}</span>
								<label class="access-toggle">
									<input type="checkbox" bind:checked={access.canView} />
									<span>View</span>
								</label>
								<label class="access-toggle">
									<input type="checkbox" bind:checked={access.canControl} />
									<span>Control</span>
								</label>
								<label class="access-toggle">
									<input type="checkbox" bind:checked={access.canConsole} />
									<span>Console</span>
								</label>
							</div>
						{/each}
					</div>
				{/if}
			</div>
			<button class="btn-primary" type="submit" disabled={creating}>
				{creating ? 'Creating...' : 'Create User'}
			</button>
			{#if formError}
				<p class="error">{formError}</p>
			{/if}
		</form>
	</section>

	<section class="panel users-panel">
		<h2>Users</h2>
		{#if data.users.error}
			<div class="error-box">
				<p>{data.users.error}</p>
			</div>
		{:else if data.users.data && data.users.data.length > 0}
			<div class="users-list">
				{#each data.users.data as user (user.id)}
					<div class="user-card" class:inactive={!user.isActive}>
						{#if editingUser?.id === user.id}
							<div class="user-edit-form">
								<div class="edit-header">
									<span class="username">{user.username}</span>
									<span class="edit-badge">Editing</span>
								</div>

								<label>
									<span class="label-text">New Password</span>
									<input
										type="password"
										bind:value={editPassword}
										placeholder="Leave blank to keep current"
									/>
								</label>
								<label>
									<span class="label-text">Minecraft Username</span>
									<input
										type="text"
										bind:value={editMinecraftUsername}
										placeholder="Link to Mojang username"
									/>
								</label>

								<label>
									<span class="label-text">Role</span>
									<select bind:value={editRole}>
										<option value="user">User</option>
										<option value="manager">Manager</option>
										<option value="admin">Admin</option>
									</select>
								</label>

								<label class="checkbox-label">
									<input type="checkbox" bind:checked={editActive} />
									<span>Active</span>
								</label>
								<div class="access-panel">
									<span class="label-text">Server Access</span>
									{#if data.servers?.error}
										<p class="error">{data.servers.error}</p>
									{:else if (data.servers?.data ?? []).length === 0}
										<p class="muted">No servers available yet.</p>
									{:else}
										<div class="access-grid">
											{#each editAccesses as access}
												<div class="access-row">
													<span class="access-name">{access.serverName}</span>
													<label class="access-toggle">
														<input type="checkbox" bind:checked={access.canView} />
														<span>View</span>
													</label>
													<label class="access-toggle">
														<input type="checkbox" bind:checked={access.canControl} />
														<span>Control</span>
													</label>
													<label class="access-toggle">
														<input type="checkbox" bind:checked={access.canConsole} />
														<span>Console</span>
													</label>
												</div>
											{/each}
										</div>
									{/if}
								</div>

								<div class="edit-actions">
									<button class="btn-primary" onclick={saveEdit} disabled={saving}>
										{saving ? 'Saving...' : 'Save'}
									</button>
									<button class="btn-secondary" onclick={cancelEdit} disabled={saving}>
										Cancel
									</button>
								</div>
							</div>
						{:else}
							<div class="user-info">
								<div class="user-main">
									<span class="username">{user.username}</span>
									<span
										class="role-badge"
										class:admin={user.role === 'admin'}
										class:manager={user.role === 'manager'}
									>
										{user.role}
									</span>
									{#if !user.isActive}
										<StatusBadge variant="error" size="sm">Disabled</StatusBadge>
									{/if}
								</div>
								<div class="user-meta">
									Created {formatDateOnly(user.createdAt)}
									{#if user.minecraftUsername}
										<span class="meta-item">MC: {user.minecraftUsername}</span>
									{/if}
								</div>
							</div>
							<div class="user-actions">
								<button
									class="btn-icon"
									title={user.isActive ? 'Disable user' : 'Enable user'}
									onclick={() => toggleActive(user)}
								>
									{user.isActive ? '🔒' : '🔓'}
								</button>
								<button
									class="btn-icon"
									title="Edit user"
									onclick={() => startEdit(user)}
								>
									✏️
								</button>
								<button
									class="btn-icon danger"
									title="Delete user"
									onclick={() => deleteUser(user)}
									disabled={deleting === user.id}
								>
									{deleting === user.id ? '⏳' : '🗑️'}
								</button>
							</div>
						{/if}
					</div>
				{/each}
			</div>
		{:else}
			<div class="empty-state">
				<p>No users found. Create your first user above.</p>
			</div>
		{/if}
	</section>
</div>

<div class="info-section">
	<div class="info-card">
		<h3>Role Permissions</h3>
		<ul>
			<li><strong>Admin:</strong> Full access to all features including user management, settings, and admin shell</li>
			<li><strong>Manager:</strong> Access only to assigned servers with view/control/console permissions</li>
			<li><strong>User:</strong> Basic access; use server assignments to grant permissions</li>
		</ul>
	</div>
</div>

<style>
	.page-header {
		display: flex;
		justify-content: space-between;
		align-items: center;
		margin-bottom: 32px;
	}

	h1 {
		margin: 0 0 8px;
		font-size: 32px;
		font-weight: 600;
	}

	.subtitle {
		margin: 0;
		color: #aab2d3;
		font-size: 15px;
	}

	.users-layout {
		display: grid;
		grid-template-columns: 320px 1fr;
		gap: 24px;
		margin-bottom: 32px;
	}

	.panel {
		background: linear-gradient(135deg, #1a1e2f 0%, #141827 100%);
		border-radius: 16px;
		padding: 24px;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.35);
		border: 1px solid #2a2f47;
	}

	.panel h2 {
		margin: 0 0 20px;
		font-size: 18px;
		color: #eef0f8;
	}

	.create-form {
		display: flex;
		flex-direction: column;
		gap: 16px;
	}

	label {
		display: flex;
		flex-direction: column;
		gap: 6px;
	}

	.label-text {
		font-size: 13px;
		color: #9aa2c5;
		font-weight: 500;
	}

	input,
	select {
		background: #141827;
		border: 1px solid #2a2f47;
		border-radius: 8px;
		padding: 10px 12px;
		color: #eef0f8;
		font-family: inherit;
		font-size: 14px;
		transition: border-color 0.2s;
	}

	input:focus,
	select:focus {
		outline: none;
		border-color: rgba(106, 176, 76, 0.5);
	}

	.btn-primary {
		background: var(--mc-grass);
		color: #fff;
		border: none;
		border-radius: 8px;
		padding: 12px 20px;
		font-size: 14px;
		font-weight: 600;
		cursor: pointer;
		transition: background 0.2s;
	}

	.btn-primary:hover:not(:disabled) {
		background: var(--mc-grass-dark);
	}

	.btn-primary:disabled {
		opacity: 0.6;
		cursor: not-allowed;
	}

	.btn-secondary {
		background: #2a2f47;
		color: #eef0f8;
		border: none;
		border-radius: 8px;
		padding: 12px 20px;
		font-size: 14px;
		font-weight: 600;
		cursor: pointer;
		transition: background 0.2s;
	}

	.btn-secondary:hover:not(:disabled) {
		background: #3a3f5a;
	}

	.users-list {
		display: flex;
		flex-direction: column;
		gap: 12px;
	}

	.user-card {
		background: rgba(20, 24, 39, 0.6);
		border-radius: 12px;
		padding: 16px;
		display: flex;
		justify-content: space-between;
		align-items: center;
		gap: 16px;
		border: 1px solid #2a2f47;
		transition: all 0.2s;
	}

	.user-card:hover {
		border-color: rgba(106, 176, 76, 0.3);
	}

	.user-card.inactive {
		opacity: 0.6;
	}

	.user-info {
		flex: 1;
	}

	.user-main {
		display: flex;
		align-items: center;
		gap: 10px;
		margin-bottom: 4px;
	}

	.username {
		font-weight: 600;
		color: #eef0f8;
		font-size: 15px;
	}

	.role-badge {
		display: inline-block;
		padding: 3px 10px;
		border-radius: 20px;
		font-size: 11px;
		font-weight: 600;
		text-transform: uppercase;
		background: rgba(88, 101, 242, 0.15);
		color: #9aa2c5;
		border: 1px solid rgba(88, 101, 242, 0.3);
	}

	.role-badge.admin {
		background: rgba(106, 176, 76, 0.15);
		color: #b7f5a2;
		border-color: rgba(106, 176, 76, 0.3);
	}

	.role-badge.manager {
		background: rgba(76, 164, 230, 0.15);
		color: #b7dbf5;
		border-color: rgba(76, 164, 230, 0.35);
	}

	.user-meta {
		font-size: 12px;
		color: #7c87b2;
		display: flex;
		gap: 12px;
		flex-wrap: wrap;
	}

	.meta-item {
		color: #9aa2c5;
	}

	.user-actions {
		display: flex;
		gap: 8px;
	}

	.btn-icon {
		background: rgba(255, 255, 255, 0.05);
		border: none;
		border-radius: 8px;
		padding: 8px 10px;
		font-size: 16px;
		cursor: pointer;
		transition: all 0.2s;
	}

	.btn-icon:hover:not(:disabled) {
		background: rgba(255, 255, 255, 0.1);
	}

	.btn-icon.danger:hover:not(:disabled) {
		background: rgba(255, 92, 92, 0.2);
	}

	.btn-icon:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.user-edit-form {
		width: 100%;
		display: flex;
		flex-direction: column;
		gap: 16px;
	}

	.edit-header {
		display: flex;
		align-items: center;
		gap: 12px;
	}

	.edit-badge {
		background: rgba(88, 101, 242, 0.2);
		color: #a5b4fc;
		padding: 4px 10px;
		border-radius: 6px;
		font-size: 12px;
		font-weight: 500;
	}

	.checkbox-label {
		flex-direction: row !important;
		align-items: center;
		gap: 10px;
	}

	.checkbox-label input[type="checkbox"] {
		width: 18px;
		height: 18px;
		cursor: pointer;
	}

	.checkbox-label span {
		font-size: 14px;
		color: #eef0f8;
	}

	.edit-actions {
		display: flex;
		gap: 10px;
		margin-top: 8px;
	}

	.error {
		color: #ff9f9f;
		font-size: 13px;
		margin: 0;
	}

	.muted {
		color: #8890b1;
		font-size: 13px;
		margin: 0;
	}

	.access-panel {
		display: flex;
		flex-direction: column;
		gap: 10px;
	}

	.access-grid {
		display: flex;
		flex-direction: column;
		gap: 10px;
	}

	.access-row {
		display: grid;
		grid-template-columns: 1fr repeat(3, auto);
		align-items: center;
		gap: 12px;
		background: #141827;
		border: 1px solid #2a2f47;
		border-radius: 10px;
		padding: 10px 12px;
	}

	.access-name {
		font-size: 13px;
		color: #eef0f8;
	}

	.access-toggle {
		display: inline-flex;
		align-items: center;
		gap: 6px;
		font-size: 12px;
		color: #aab2d3;
		white-space: nowrap;
	}

	.access-toggle input {
		width: 16px;
		height: 16px;
	}

	.error-box {
		background: rgba(255, 92, 92, 0.1);
		border: 1px solid rgba(255, 92, 92, 0.3);
		border-radius: 10px;
		padding: 16px;
	}

	.error-box p {
		margin: 0;
		color: #ff9f9f;
	}

	.empty-state {
		text-align: center;
		padding: 40px 20px;
		color: #7c87b2;
	}

	.info-section {
		margin-top: 24px;
	}

	.info-card {
		background: rgba(88, 101, 242, 0.08);
		border: 1px solid rgba(88, 101, 242, 0.2);
		border-radius: 12px;
		padding: 20px 24px;
	}

	.info-card h3 {
		margin: 0 0 12px;
		font-size: 16px;
		color: #a5b4fc;
	}

	.info-card ul {
		margin: 0;
		padding-left: 20px;
		color: #9aa2c5;
		font-size: 14px;
		line-height: 1.8;
	}

	.info-card strong {
		color: #c4cff5;
	}

	@media (max-width: 900px) {
		.users-layout {
			grid-template-columns: 1fr;
		}
	}

	@media (max-width: 768px) {
		.page-header {
			margin-bottom: 20px;
		}

		h1 {
			font-size: 24px;
		}

		.users-layout {
			gap: 16px;
			margin-bottom: 20px;
		}

		.panel {
			padding: 16px;
			border-radius: 12px;
		}

		.user-card {
			flex-direction: column;
			align-items: stretch;
			gap: 12px;
		}

		.user-actions {
			justify-content: flex-end;
		}

		.access-row {
			grid-template-columns: 1fr;
			gap: 8px;
		}

		.access-name {
			font-weight: 600;
			margin-bottom: 2px;
		}

		.access-toggle {
			justify-content: flex-start;
		}

		.edit-actions {
			flex-direction: column;
		}

		.edit-actions button {
			width: 100%;
		}

		.info-card {
			padding: 16px;
		}
	}

	@media (max-width: 480px) {
		.panel {
			padding: 14px;
		}

		.user-main {
			flex-wrap: wrap;
			gap: 6px;
		}

		.btn-primary,
		.btn-secondary {
			padding: 10px 16px;
			font-size: 13px;
		}
	}
</style>
