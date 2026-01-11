<script lang="ts">
	import { invalidateAll } from '$app/navigation';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();
	let creating = $state(false);
	let formError = $state('');
	let username = $state('');
	let password = $state('');
	let role = $state('user');

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
				body: JSON.stringify({ username: username.trim(), password: password.trim(), role })
			});

			if (!res.ok) {
				const error = await res.json().catch(() => ({ error: 'Failed to create user' }));
				formError = error.error || 'Failed to create user';
			} else {
				username = '';
				password = '';
				role = 'user';
				await invalidateAll();
			}
		} finally {
			creating = false;
		}
	}
</script>

<div class="page-header">
	<div>
		<h1>Access Control</h1>
		<p class="subtitle">Manage application users and view host accounts.</p>
	</div>
</div>

<div class="access-grid">
	<section class="panel">
		<h2>Application Users</h2>
		<form
			class="create-form"
			onsubmit={(event) => {
				event.preventDefault();
				createUser();
			}}
		>
			<label>
				Username
				<input type="text" bind:value={username} placeholder="new-user" />
			</label>
			<label>
				Password
				<input type="password" bind:value={password} placeholder="password" />
			</label>
			<label>
				Role
				<select bind:value={role}>
					<option value="user">User</option>
					<option value="admin">Admin</option>
				</select>
			</label>
			<button class="btn-primary" type="submit" disabled={creating}>
				{creating ? 'Creating...' : 'Create User'}
			</button>
			{#if formError}
				<p class="error">{formError}</p>
			{/if}
		</form>

		{#if data.users.error}
			<p class="error">{data.users.error}</p>
		{:else if data.users.data && data.users.data.length > 0}
			<div class="list">
				{#each data.users.data as user}
					<div class="list-item">
						<div>
							<div class="list-title">{user.username}</div>
							<div class="list-meta">{user.role} · {user.isActive ? 'active' : 'disabled'}</div>
						</div>
						<div class="list-meta">created {new Date(user.createdAt).toLocaleDateString()}</div>
					</div>
				{/each}
			</div>
		{:else}
			<p class="empty">No users found.</p>
		{/if}
	</section>

	<section class="panel">
		<h2>Host Users</h2>
		{#if data.hostUsers.error}
			<p class="error">{data.hostUsers.error}</p>
		{:else if data.hostUsers.data && data.hostUsers.data.length > 0}
			<div class="list">
				{#each data.hostUsers.data as user}
					<div class="list-item">
						<div>
							<div class="list-title">{user.username}</div>
							<div class="list-meta">UID {user.uid} · GID {user.gid}</div>
						</div>
						<div class="list-meta">{user.home}</div>
					</div>
				{/each}
			</div>
		{:else}
			<p class="empty">No host users detected.</p>
		{/if}
	</section>

	<section class="panel">
		<h2>Host Groups</h2>
		{#if data.hostGroups.error}
			<p class="error">{data.hostGroups.error}</p>
		{:else if data.hostGroups.data && data.hostGroups.data.length > 0}
			<div class="list">
				{#each data.hostGroups.data as group}
					<div class="list-item">
						<div class="list-title">{group.groupName}</div>
						<div class="list-meta">GID {group.gid}</div>
					</div>
				{/each}
			</div>
		{:else}
			<p class="empty">No host groups detected.</p>
		{/if}
	</section>
</div>

<style>
	.page-header {
		display: flex;
		justify-content: space-between;
		align-items: center;
		margin-bottom: 24px;
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

	.access-grid {
		display: grid;
		grid-template-columns: repeat(auto-fit, minmax(280px, 1fr));
		gap: 20px;
	}

	.panel {
		background: #1a1e2f;
		border-radius: 16px;
		padding: 20px;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.35);
		border: 1px solid rgba(106, 176, 76, 0.12);
	}

	.panel h2 {
		margin: 0 0 16px;
		font-size: 18px;
	}

	.create-form {
		display: grid;
		gap: 12px;
		margin-bottom: 16px;
	}

	label {
		display: flex;
		flex-direction: column;
		gap: 6px;
		font-size: 13px;
		color: #aab2d3;
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
	}

	.btn-primary:disabled {
		opacity: 0.6;
		cursor: not-allowed;
	}

	.list {
		display: grid;
		gap: 10px;
	}

	.list-item {
		background: #141827;
		border-radius: 12px;
		padding: 12px;
		display: flex;
		justify-content: space-between;
		gap: 12px;
		border: 1px solid rgba(42, 47, 71, 0.8);
	}

	.list-title {
		font-weight: 600;
		color: #eef0f8;
	}

	.list-meta {
		font-size: 12px;
		color: #9aa2c5;
	}

	.error {
		color: #ff9f9f;
		font-size: 13px;
	}

	.empty {
		color: #9aa2c5;
		font-size: 13px;
	}
</style>
