<script lang="ts">
	import type { SystemNotification, JobStatus, ModpackInstallProgress } from '$lib/api/types';
	import { onMount } from 'svelte';
	import { modal } from '$lib/stores/modal';
	import { uploads, type UploadEntry } from '$lib/stores/uploads';

	let notifications = $state<SystemNotification[]>([]);
	let activeJobs = $state<JobStatus[]>([]);
	let activeModpacks = $state<ModpackInstallProgress[]>([]);
	let isOpen = $state(false);
	let loading = $state(false);

	const activeUploads = $derived($uploads.filter((u) => u.status === 'uploading'));
	const activeTaskCount = $derived(activeJobs.length + activeModpacks.length + activeUploads.length);
	const unreadCount = $derived(notifications.filter((n) => !n.isRead && !n.dismissedAt).length);
	const totalBadgeCount = $derived(unreadCount + activeTaskCount);

	onMount(() => {
		loadNotifications();
		loadActiveJobs();
		// Poll for new notifications every 30 seconds
		const notificationInterval = setInterval(loadNotifications, 30000);
		// Poll for active jobs more frequently (every 5 seconds)
		const jobsInterval = setInterval(loadActiveJobs, 5000);
		return () => {
			clearInterval(notificationInterval);
			clearInterval(jobsInterval);
		};
	});

	async function loadNotifications() {
		if (loading) return;
		loading = true;
		try {
			const res = await fetch('/api/notifications?includeDismissed=false');
			if (res.ok) {
				notifications = await res.json();
			}
		} catch (err) {
			console.error('Failed to load notifications:', err);
		} finally {
			loading = false;
		}
	}

	async function loadActiveJobs() {
		try {
			const res = await fetch('/api/jobs');
			if (res.ok) {
				const data = await res.json();
				activeJobs = data.jobs ?? [];
				activeModpacks = data.modpackInstalls ?? [];
			}
		} catch (err) {
			console.error('Failed to load active jobs:', err);
		}
	}

	async function markAsRead(id: number) {
		try {
			const res = await fetch(`/api/notifications/${id}/read`, { method: 'PATCH' });
			if (res.ok) {
				const notification = notifications.find((n) => n.id === id);
				if (notification) {
					notification.isRead = true;
				}
			}
		} catch (err) {
			console.error('Failed to mark as read:', err);
		}
	}

	async function dismiss(id: number) {
		try {
			const res = await fetch(`/api/notifications/${id}/dismiss`, { method: 'PATCH' });
			if (res.ok) {
				notifications = notifications.filter((n) => n.id !== id);
			}
		} catch (err) {
			console.error('Failed to dismiss:', err);
		}
	}

	async function deleteNotification(id: number) {
		const confirmed = await modal.confirm('Delete this notification?', 'Delete Notification');
		if (!confirmed) return;

		try {
			const res = await fetch(`/api/notifications/${id}`, { method: 'DELETE' });
			if (res.ok) {
				notifications = notifications.filter((n) => n.id !== id);
			}
		} catch (err) {
			await modal.error(err instanceof Error ? err.message : 'Delete failed');
		}
	}

	async function dismissAll() {
		const ids = notifications.filter((n) => !n.dismissedAt).map((n) => n.id);
		if (ids.length === 0) return;

		try {
			const res = await fetch('/api/notifications/dismiss', {
				method: 'PATCH',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify(ids)
			});
			if (res.ok) {
				notifications = [];
			}
		} catch (err) {
			console.error('Failed to dismiss all:', err);
		}
	}

	function getIconForType(type: SystemNotification['type']) {
		switch (type) {
			case 'info':
				return 'ℹ️';
			case 'warning':
				return '⚠️';
			case 'error':
				return '❌';
			case 'success':
				return '✅';
			default:
				return '📢';
		}
	}

	function getColorForType(type: SystemNotification['type']) {
		switch (type) {
			case 'info':
				return '#5b9eff';
			case 'warning':
				return '#ffb74d';
			case 'error':
				return '#ff6b6b';
			case 'success':
				return '#6ab04c';
			default:
				return '#9aa2c5';
		}
	}

	function formatTime(dateString: string) {
		const date = new Date(dateString);
		const now = new Date();
		const diff = now.getTime() - date.getTime();
		const minutes = Math.floor(diff / 60000);
		const hours = Math.floor(minutes / 60);
		const days = Math.floor(hours / 24);

		if (minutes < 1) return 'just now';
		if (minutes < 60) return `${minutes}m ago`;
		if (hours < 24) return `${hours}h ago`;
		if (days < 7) return `${days}d ago`;
		return date.toLocaleDateString();
	}

	function getJobTypeLabel(type: string): string {
		switch (type) {
			case 'import':
				return 'Server Import';
			case 'backup':
				return 'Backup';
			case 'restore':
				return 'Restore';
			case 'download':
				return 'Download';
			default:
				return type.charAt(0).toUpperCase() + type.slice(1);
		}
	}

	function toggleMenu() {
		isOpen = !isOpen;
		if (isOpen) {
			loadNotifications();
			loadActiveJobs();
		}
	}

	function handleClickOutside(event: MouseEvent) {
		const target = event.target as HTMLElement;
		if (!target.closest('.notification-menu')) {
			isOpen = false;
		}
	}
</script>

<svelte:window onclick={handleClickOutside} />

<div class="notification-menu">
	<button class="notification-bell" onclick={toggleMenu} aria-label="Notifications">
		<span class="bell-icon">🔔</span>
		{#if totalBadgeCount > 0}
			<span class="badge" class:has-tasks={activeTaskCount > 0}>
				{totalBadgeCount > 9 ? '9+' : totalBadgeCount}
			</span>
		{/if}
	</button>

	{#if isOpen}
		<div class="notification-dropdown">
			{#if activeTaskCount > 0}
				<div class="section-header tasks-header">
					<span class="section-icon">⏳</span>
					<h3>Active Tasks</h3>
				</div>
				<div class="tasks-list">
					{#each activeJobs as job (job.jobId)}
						<div class="task-item">
							<div class="task-info">
								<span class="task-type">{getJobTypeLabel(job.type)}</span>
								<span class="task-server">{job.serverName}</span>
							</div>
							<div class="task-progress">
								<div class="progress-bar">
									<div class="progress-fill" style="width: {job.percentage}%"></div>
								</div>
								<span class="progress-text">{job.percentage}%</span>
							</div>
							{#if job.message}
								<span class="task-message">{job.message}</span>
							{/if}
						</div>
					{/each}
					{#each activeModpacks as modpack (modpack.jobId)}
						<div class="task-item">
							<div class="task-info">
								<span class="task-type">Modpack Install</span>
								<span class="task-server">{modpack.serverName}</span>
							</div>
							<div class="task-progress">
								<div class="progress-bar">
									<div class="progress-fill" style="width: {modpack.percentage}%"></div>
								</div>
								<span class="progress-text">
									{modpack.currentModIndex}/{modpack.totalMods}
								</span>
							</div>
							{#if modpack.currentModName}
								<span class="task-message">{modpack.currentModName}</span>
							{/if}
						</div>
					{/each}
					{#each activeUploads as upload (upload.id)}
						<div class="task-item upload-item">
							<div class="task-info">
								<span class="task-type">Upload</span>
								<span class="task-server">{upload.filename}</span>
							</div>
							<div class="task-progress">
								<div class="progress-bar uploading">
									<div class="progress-fill-animated"></div>
								</div>
								<span class="progress-text">Uploading</span>
							</div>
							<button
								class="cancel-upload"
								onclick={() => uploads.cancel(upload.id)}
								title="Cancel upload"
							>
								✕
							</button>
						</div>
					{/each}
				</div>
			{/if}

			<div class="dropdown-header">
				<h3>Notifications</h3>
				{#if notifications.length > 0}
					<button class="dismiss-all-btn" onclick={dismissAll}>Dismiss All</button>
				{/if}
			</div>

			<div class="notification-list">
				{#if notifications.length === 0}
					<div class="empty-state">
						<p>No notifications</p>
					</div>
				{:else}
					{#each notifications as notification (notification.id)}
						<div
							class="notification-item"
							class:unread={!notification.isRead}
							onclick={() => !notification.isRead && markAsRead(notification.id)}
						>
							<div class="notification-icon" style="color: {getColorForType(notification.type)}">
								{getIconForType(notification.type)}
							</div>
							<div class="notification-content">
								<div class="notification-header">
									<h4>{notification.title}</h4>
									<span class="time">{formatTime(notification.createdAt)}</span>
								</div>
								<p>{notification.message}</p>
								{#if notification.serverName}
									<span class="server-tag">{notification.serverName}</span>
								{/if}
							</div>
							<div class="notification-actions">
								<button
									class="action-btn"
									onclick={(e) => {
										e.stopPropagation();
										dismiss(notification.id);
									}}
									title="Dismiss"
								>
									✓
								</button>
								<button
									class="action-btn danger"
									onclick={(e) => {
										e.stopPropagation();
										deleteNotification(notification.id);
									}}
									title="Delete"
								>
									🗑️
								</button>
							</div>
						</div>
					{/each}
				{/if}
			</div>
		</div>
	{/if}
</div>

<style>
	.notification-menu {
		position: relative;
	}

	.notification-bell {
		position: relative;
		background: none;
		border: none;
		color: #eef0f8;
		font-size: 20px;
		cursor: pointer;
		padding: 8px;
		border-radius: 8px;
		transition: background 0.2s;
	}

	.notification-bell:hover {
		background: rgba(255, 255, 255, 0.1);
	}

	.bell-icon {
		display: block;
	}

	.badge {
		position: absolute;
		top: 4px;
		right: 4px;
		background: #ff6b6b;
		color: white;
		font-size: 10px;
		font-weight: 600;
		padding: 2px 5px;
		border-radius: 10px;
		min-width: 18px;
		text-align: center;
	}

	.notification-dropdown {
		position: absolute;
		top: calc(100% + 8px);
		right: 0;
		width: 400px;
		max-height: 500px;
		background: #141827;
		border-radius: 12px;
		border: 1px solid #2a2f47;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.4);
		overflow: hidden;
		z-index: 1000;
	}

	.dropdown-header {
		display: flex;
		justify-content: space-between;
		align-items: center;
		padding: 16px 20px;
		border-bottom: 1px solid #2a2f47;
	}

	.dropdown-header h3 {
		margin: 0;
		font-size: 18px;
		font-weight: 600;
		color: #eef0f8;
	}

	.dismiss-all-btn {
		background: none;
		border: none;
		color: #9aa2c5;
		font-size: 13px;
		cursor: pointer;
		padding: 4px 8px;
		border-radius: 6px;
		transition: background 0.2s;
	}

	.dismiss-all-btn:hover {
		background: rgba(255, 255, 255, 0.05);
		color: #eef0f8;
	}

	.notification-list {
		max-height: 420px;
		overflow-y: auto;
	}

	.empty-state {
		padding: 40px 20px;
		text-align: center;
		color: #8890b1;
	}

	.empty-state p {
		margin: 0;
	}

	.notification-item {
		display: flex;
		gap: 12px;
		padding: 16px 20px;
		border-bottom: 1px solid #2a2f47;
		cursor: pointer;
		transition: background 0.2s;
	}

	.notification-item:hover {
		background: #1a1f33;
	}

	.notification-item.unread {
		background: rgba(88, 101, 242, 0.05);
	}

	.notification-icon {
		font-size: 20px;
		flex-shrink: 0;
	}

	.notification-content {
		flex: 1;
		min-width: 0;
	}

	.notification-header {
		display: flex;
		justify-content: space-between;
		align-items: flex-start;
		gap: 8px;
		margin-bottom: 4px;
	}

	.notification-header h4 {
		margin: 0;
		font-size: 14px;
		font-weight: 600;
		color: #eef0f8;
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
	}

	.time {
		font-size: 11px;
		color: #8890b1;
		flex-shrink: 0;
	}

	.notification-content p {
		margin: 0 0 8px;
		font-size: 13px;
		color: #9aa2c5;
		line-height: 1.4;
	}

	.server-tag {
		display: inline-block;
		background: #1a1f33;
		border: 1px solid #2a2f47;
		padding: 2px 8px;
		border-radius: 4px;
		font-size: 11px;
		color: #9aa2c5;
	}

	.notification-actions {
		display: flex;
		gap: 4px;
		flex-shrink: 0;
	}

	.action-btn {
		background: none;
		border: none;
		color: #9aa2c5;
		font-size: 14px;
		cursor: pointer;
		padding: 4px 8px;
		border-radius: 6px;
		transition: all 0.2s;
	}

	.action-btn:hover {
		background: rgba(255, 255, 255, 0.05);
		color: #eef0f8;
	}

	.action-btn.danger:hover {
		background: rgba(255, 92, 92, 0.1);
		color: #ff9f9f;
	}

	/* Badge with active tasks indicator */
	.badge.has-tasks {
		background: #5b9eff;
	}

	/* Active Tasks Section */
	.section-header.tasks-header {
		display: flex;
		align-items: center;
		gap: 8px;
		padding: 12px 20px;
		background: rgba(91, 158, 255, 0.08);
		border-bottom: 1px solid #2a2f47;
	}

	.section-header.tasks-header h3 {
		margin: 0;
		font-size: 14px;
		font-weight: 600;
		color: #5b9eff;
	}

	.section-icon {
		font-size: 16px;
	}

	.tasks-list {
		border-bottom: 1px solid #2a2f47;
	}

	.task-item {
		padding: 12px 20px;
		border-bottom: 1px solid rgba(42, 47, 71, 0.5);
	}

	.task-item:last-child {
		border-bottom: none;
	}

	.task-info {
		display: flex;
		align-items: center;
		gap: 8px;
		margin-bottom: 8px;
	}

	.task-type {
		font-size: 13px;
		font-weight: 600;
		color: #eef0f8;
	}

	.task-server {
		font-size: 12px;
		color: #9aa2c5;
		background: #1a1f33;
		padding: 2px 8px;
		border-radius: 4px;
		border: 1px solid #2a2f47;
	}

	.task-progress {
		display: flex;
		align-items: center;
		gap: 10px;
		margin-bottom: 4px;
	}

	.task-progress .progress-bar {
		flex: 1;
		height: 6px;
		background: #2a2f47;
		border-radius: 3px;
		overflow: hidden;
	}

	.task-progress .progress-fill {
		height: 100%;
		background: linear-gradient(90deg, #5b9eff, #79c0ff);
		border-radius: 3px;
		transition: width 0.3s ease;
	}

	.progress-text {
		font-size: 11px;
		color: #9aa2c5;
		min-width: 40px;
		text-align: right;
	}

	.task-message {
		font-size: 11px;
		color: #7c87b2;
		display: block;
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
	}

	/* Upload items */
	.upload-item {
		position: relative;
	}

	.progress-bar.uploading {
		overflow: hidden;
	}

	.progress-fill-animated {
		height: 100%;
		width: 50%;
		background: linear-gradient(90deg, #5b9eff, #79c0ff, #5b9eff);
		background-size: 200% 100%;
		animation: uploadProgress 1.5s ease-in-out infinite;
	}

	@keyframes uploadProgress {
		0% {
			transform: translateX(-100%);
		}
		100% {
			transform: translateX(200%);
		}
	}

	.cancel-upload {
		position: absolute;
		right: 12px;
		top: 50%;
		transform: translateY(-50%);
		background: none;
		border: none;
		color: #ff6b6b;
		font-size: 14px;
		cursor: pointer;
		padding: 4px 8px;
		border-radius: 4px;
		transition: background 0.2s;
	}

	.cancel-upload:hover {
		background: rgba(255, 92, 92, 0.15);
	}
</style>
