<script lang="ts">
	import { goto, invalidateAll } from '$app/navigation';
	import type { PageData } from './$types';
	import * as api from '$lib/api/client';

	let { data }: { data: PageData } = $props();

	let step = $state<'name' | 'profile' | 'creating'>('name');
	let serverName = $state('');
	let selectedProfileId = $state('');
	let createError = $state('');
	let downloadingProfile = $state(false);

	const profileGroups = $derived.by(() => {
		if (!data.profiles.data) return {};
		const groups: Record<string, typeof data.profiles.data> = {};
		for (const profile of data.profiles.data) {
			if (!groups[profile.group]) {
				groups[profile.group] = [];
			}
			groups[profile.group].push(profile);
		}
		return groups;
	});

	function formatGroupLabel(group: string) {
		switch (group) {
			case 'vanilla':
				return 'Vanilla';
			case 'paper':
				return 'Paper';
			case 'spigot':
				return 'Spigot';
			case 'craftbukkit':
				return 'CraftBukkit';
			default:
				return group;
		}
	}

	function validateServerName(): boolean {
		if (!serverName.trim()) {
			createError = 'Server name is required';
			return false;
		}
		if (!/^[a-zA-Z0-9_\- ]+$/.test(serverName)) {
			createError = 'Server name can only contain letters, numbers, spaces, hyphens, and underscores';
			return false;
		}
		createError = '';
		return true;
	}

	async function handleNext() {
		if (step === 'name') {
			if (!validateServerName()) return;
			step = 'profile';
		} else if (step === 'profile') {
			await createServer();
		}
	}

	async function createServer() {
		step = 'creating';
		createError = '';

		try {
			// Download profile if selected and not already downloaded
			if (selectedProfileId) {
				const profile = data.profiles.data?.find((p) => p.id === selectedProfileId);
				if (profile && !profile.downloaded) {
					downloadingProfile = true;
					const downloadResult = await fetch(`/api/host/profiles/${selectedProfileId}/download`, {
						method: 'POST'
					});
					downloadingProfile = false;

					if (!downloadResult.ok) {
						const error = await downloadResult
							.json()
							.catch(() => ({ error: 'Failed to download profile' }));
						throw new Error(error.error || 'Failed to download profile');
					}
				}
			}

			// Create the server
			const createResult = await api.createServer(fetch, {
				name: serverName.trim(),
				ownerUid: 1000, // TODO: Get from current user
				ownerGid: 1000
			});

			if (createResult.error) {
				throw new Error(createResult.error);
			}

			// Copy profile to server if selected
			if (selectedProfileId) {
				const copyResult = await fetch(`/api/host/profiles/${selectedProfileId}/copy-to-server`, {
					method: 'POST',
					headers: { 'Content-Type': 'application/json' },
					body: JSON.stringify({ serverName: serverName.trim() })
				});

				if (!copyResult.ok) {
					const error = await copyResult
						.json()
						.catch(() => ({ error: 'Failed to copy profile to server' }));
					console.warn('Profile copy failed:', error);
					// Don't fail the whole creation, just warn
				}
			}

			await invalidateAll();
			goto('/servers');
		} catch (err) {
			createError = err instanceof Error ? err.message : 'Failed to create server';
			step = 'profile';
		}
	}

	function handleSkipProfile() {
		selectedProfileId = '';
		createServer();
	}
</script>

<div class="page-container">
	<div class="page-header">
		<h1>Create New Server</h1>
		<p class="subtitle">Set up a new Minecraft server in just a few steps</p>
	</div>

	<div class="wizard-card">
		<div class="steps-indicator">
			<div class="step" class:active={step === 'name'} class:completed={step !== 'name'}>
				<div class="step-number">1</div>
				<div class="step-label">Server Name</div>
			</div>
			<div class="step-divider"></div>
			<div class="step" class:active={step === 'profile'} class:completed={step === 'creating'}>
				<div class="step-number">2</div>
				<div class="step-label">Choose Profile</div>
			</div>
			<div class="step-divider"></div>
			<div class="step" class:active={step === 'creating'}>
				<div class="step-number">3</div>
				<div class="step-label">Finish</div>
			</div>
		</div>

		<div class="wizard-content">
			{#if step === 'name'}
				<div class="step-content">
					<h2>Choose a name for your server</h2>
					<p class="step-description">
						This will be used to identify your server in the dashboard.
					</p>

					<div class="form-field">
						<label for="server-name">Server Name</label>
						<input
							type="text"
							id="server-name"
							bind:value={serverName}
							placeholder="my-survival-server"
							autofocus
							onkeydown={(e) => e.key === 'Enter' && handleNext()}
						/>
						{#if createError}
							<span class="error-text">{createError}</span>
						{/if}
					</div>
				</div>
			{:else if step === 'profile'}
				<div class="step-content">
					<h2>Choose a server JAR</h2>
					<p class="step-description">
						Select a Minecraft server type and version, or start with an empty server.
					</p>

					{#if data.profiles.error}
						<div class="error-box">Failed to load profiles: {data.profiles.error}</div>
					{:else if data.profiles.data}
						<div class="profile-select">
							<label for="profile-select">Profile</label>
							<select id="profile-select" bind:value={selectedProfileId}>
								<option value="">Select profile</option>
								{#each Object.entries(profileGroups) as [groupName, profiles]}
									<optgroup label={formatGroupLabel(groupName)}>
										{#each profiles as profile}
											<option value={profile.id}>
												{profile.version} ({profile.type}{profile.downloaded ? '' : ' - will download'})
											</option>
										{/each}
									</optgroup>
								{/each}
							</select>
						</div>
					{/if}

					{#if createError}
						<div class="error-box">{createError}</div>
					{/if}
				</div>
			{:else if step === 'creating'}
				<div class="step-content creating">
					<div class="spinner"></div>
					<h2>
						{downloadingProfile ? 'Downloading profile...' : 'Creating your server...'}
					</h2>
					<p class="step-description">This will only take a moment</p>
				</div>
			{/if}
		</div>

		<div class="wizard-actions">
			{#if step === 'name'}
				<a href="/servers" class="btn-secondary">Cancel</a>
				<button class="btn-primary" onclick={handleNext}>Next</button>
			{:else if step === 'profile'}
				<button class="btn-secondary" onclick={() => (step = 'name')}>Back</button>
				<button class="btn-secondary" onclick={handleSkipProfile}>Skip (Empty Server)</button>
				<button class="btn-primary" onclick={handleNext} disabled={!selectedProfileId}>
					Create Server
				</button>
			{/if}
		</div>
	</div>
</div>

<style>
	.page-container {
		max-width: 900px;
		margin: 0 auto;
	}

	.page-header {
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

	.wizard-card {
		background: #1a1e2f;
		border-radius: 16px;
		padding: 32px;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.35);
	}

	.steps-indicator {
		display: flex;
		align-items: center;
		justify-content: center;
		margin-bottom: 40px;
		gap: 0;
	}

	.step {
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 8px;
	}

	.step-number {
		width: 40px;
		height: 40px;
		border-radius: 50%;
		background: #2b2f45;
		color: #9aa2c5;
		display: flex;
		align-items: center;
		justify-content: center;
		font-weight: 600;
		font-size: 16px;
		transition: all 0.3s;
	}

	.step.active .step-number {
		background: #5865f2;
		color: white;
		box-shadow: 0 0 20px rgba(88, 101, 242, 0.4);
	}

	.step.completed .step-number {
		background: rgba(122, 230, 141, 0.2);
		color: #7ae68d;
	}

	.step-label {
		font-size: 13px;
		color: #9aa2c5;
	}

	.step.active .step-label {
		color: #eef0f8;
		font-weight: 500;
	}

	.step-divider {
		width: 60px;
		height: 2px;
		background: #2b2f45;
		margin: 0 12px;
		margin-bottom: 28px;
	}

	.wizard-content {
		min-height: 400px;
	}

	.step-content {
		animation: fadeIn 0.3s;
	}

	.step-content.creating {
		display: flex;
		flex-direction: column;
		align-items: center;
		justify-content: center;
		min-height: 400px;
	}

	@keyframes fadeIn {
		from {
			opacity: 0;
			transform: translateY(10px);
		}
		to {
			opacity: 1;
			transform: translateY(0);
		}
	}

	.step-content h2 {
		margin: 0 0 8px;
		font-size: 24px;
	}

	.step-description {
		margin: 0 0 24px;
		color: #9aa2c5;
		font-size: 14px;
	}

	.form-field {
		display: flex;
		flex-direction: column;
		gap: 8px;
	}

	.form-field label {
		font-size: 14px;
		font-weight: 500;
		color: #aab2d3;
	}

	.form-field input {
		background: #141827;
		border: 1px solid #2a2f47;
		border-radius: 8px;
		padding: 14px 16px;
		color: #eef0f8;
		font-family: inherit;
		font-size: 16px;
		transition: border-color 0.2s;
	}

	.form-field input:focus {
		outline: none;
		border-color: #5865f2;
	}

	.error-text {
		color: #ff9f9f;
		font-size: 13px;
	}

	.error-box {
		background: rgba(255, 92, 92, 0.1);
		border: 1px solid rgba(255, 92, 92, 0.3);
		border-radius: 12px;
		padding: 16px 20px;
		color: #ff9f9f;
		margin-top: 16px;
	}

	.profile-select {
		display: flex;
		flex-direction: column;
		gap: 8px;
		max-width: 420px;
	}

	.profile-select label {
		font-size: 14px;
		font-weight: 500;
		color: #aab2d3;
	}

	.profile-select select {
		background: #141827;
		border: 1px solid #2a2f47;
		border-radius: 8px;
		padding: 12px 14px;
		color: #eef0f8;
		font-family: inherit;
		font-size: 15px;
	}

	.profile-groups {
		display: flex;
		flex-direction: column;
		gap: 24px;
	}

	.profile-group {
		display: flex;
		flex-direction: column;
		gap: 12px;
	}

	.group-name {
		margin: 0;
		font-size: 16px;
		font-weight: 600;
		color: #eef0f8;
	}

	.profile-list {
		display: grid;
		grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
		gap: 12px;
	}

	.profile-option {
		background: #141827;
		border: 2px solid #2a2f47;
		border-radius: 10px;
		padding: 16px;
		cursor: pointer;
		transition: all 0.2s;
		display: flex;
		flex-direction: column;
		align-items: flex-start;
		gap: 8px;
		position: relative;
		font-family: inherit;
		text-align: left;
	}

	.profile-option:hover {
		border-color: #5865f2;
		background: rgba(88, 101, 242, 0.05);
	}

	.profile-option.selected {
		border-color: #5865f2;
		background: rgba(88, 101, 242, 0.1);
	}

	.profile-info {
		display: flex;
		flex-direction: column;
		gap: 4px;
		flex: 1;
	}

	.profile-version {
		font-size: 18px;
		font-weight: 600;
		color: #eef0f8;
	}

	.profile-meta {
		font-size: 12px;
		color: #9aa2c5;
		display: flex;
		align-items: center;
		gap: 8px;
	}

	.download-badge {
		background: rgba(88, 101, 242, 0.2);
		color: #a5b4fc;
		padding: 2px 8px;
		border-radius: 999px;
		font-size: 11px;
	}

	.checkmark {
		position: absolute;
		top: 12px;
		right: 12px;
		color: #5865f2;
		font-size: 20px;
		font-weight: bold;
	}

	.spinner {
		width: 48px;
		height: 48px;
		border: 4px solid #2b2f45;
		border-top-color: #5865f2;
		border-radius: 50%;
		animation: spin 0.8s linear infinite;
		margin-bottom: 24px;
	}

	@keyframes spin {
		to {
			transform: rotate(360deg);
		}
	}

	.wizard-actions {
		display: flex;
		gap: 12px;
		justify-content: flex-end;
		margin-top: 32px;
		padding-top: 24px;
		border-top: 1px solid #2a2f47;
	}

	.btn-primary {
		background: #5865f2;
		color: white;
		border: none;
		border-radius: 8px;
		padding: 12px 28px;
		font-family: inherit;
		font-size: 15px;
		font-weight: 600;
		cursor: pointer;
		transition: background 0.2s;
	}

	.btn-primary:hover:not(:disabled) {
		background: #4752c4;
	}

	.btn-primary:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.btn-secondary {
		background: #2b2f45;
		color: #d4d9f1;
		border: none;
		border-radius: 8px;
		padding: 12px 24px;
		font-family: inherit;
		font-size: 15px;
		font-weight: 500;
		cursor: pointer;
		transition: background 0.2s;
		text-decoration: none;
		display: inline-block;
	}

	.btn-secondary:hover {
		background: #3a3f5a;
	}

	@media (max-width: 640px) {
		.wizard-card {
			padding: 24px 20px;
		}

		.steps-indicator {
			gap: 0;
		}

		.step-divider {
			width: 40px;
		}

		.step-label {
			font-size: 11px;
		}

		.profile-list {
			grid-template-columns: 1fr;
		}

		.wizard-actions {
			flex-direction: column-reverse;
		}
	}
</style>
