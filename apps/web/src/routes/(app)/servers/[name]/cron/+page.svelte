<script lang="ts">
	import { modal } from '$lib/stores/modal';
	import type { LayoutData } from '../$types';

	let { data }: { data: { server: LayoutData['server'] } } = $props();

	interface CronJob {
		hash: string;
		source: string;
		action: string;
		msg: string | null;
		enabled: boolean;
	}

	let jobs = $state<CronJob[]>([]);
	let loading = $state(true);
	let creating = $state(false);
	let formError = $state('');

	// Form state
	let schedulePreset = $state('daily');
	let customCron = $state('0 3 * * *');
	let action = $state('backup');
	let message = $state('');

	const presets: Record<string, { label: string; cron: string; description: string }> = {
		hourly: { label: 'Every Hour', cron: '0 * * * *', description: 'Runs at the start of every hour' },
		'every-6h': {
			label: 'Every 6 Hours',
			cron: '0 */6 * * *',
			description: 'Runs every 6 hours (midnight, 6am, noon, 6pm)'
		},
		'every-12h': {
			label: 'Every 12 Hours',
			cron: '0 0,12 * * *',
			description: 'Runs at midnight and noon'
		},
		daily: { label: 'Daily', cron: '0 3 * * *', description: 'Runs daily at 3:00 AM UTC' },
		weekly: { label: 'Weekly', cron: '0 3 * * 0', description: 'Runs every Sunday at 3:00 AM UTC' },
		custom: { label: 'Custom', cron: '', description: 'Enter a custom cron expression' }
	};

	const actions: Record<string, { label: string; description: string }> = {
		backup: { label: 'Backup', description: 'Create an incremental server backup' },
		restart: { label: 'Restart', description: 'Stop and restart the server' }
	};

	let effectiveCron = $derived(
		schedulePreset === 'custom' ? customCron : presets[schedulePreset]?.cron ?? ''
	);
	let presetDescription = $derived(
		schedulePreset === 'custom'
			? describeCron(customCron)
			: presets[schedulePreset]?.description ?? ''
	);

	$effect(() => {
		loadJobs();
	});

	async function loadJobs() {
		if (!data.server) return;
		loading = true;
		try {
			const res = await fetch(`/api/servers/${data.server.name}/cron`);
			if (res.ok) {
				jobs = await res.json();
			}
		} catch (err) {
			console.error('Failed to load cron jobs:', err);
		} finally {
			loading = false;
		}
	}

	async function createJob() {
		if (!data.server) return;
		formError = '';

		const cron = effectiveCron.trim();
		if (!cron) {
			formError = 'Please enter a valid cron expression.';
			return;
		}

		const parts = cron.split(/\s+/);
		if (parts.length < 5) {
			formError = 'Cron expression must have 5 fields: minute hour day month weekday';
			return;
		}

		creating = true;
		try {
			const res = await fetch(`/api/servers/${data.server.name}/cron`, {
				method: 'POST',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify({
					source: cron,
					action,
					msg: message.trim() || null
				})
			});

			if (!res.ok) {
				const error = await res.json().catch(() => ({ error: 'Failed to create scheduled task' }));
				formError = error.error || error.detail || 'Failed to create scheduled task';
			} else {
				message = '';
				await loadJobs();
			}
		} finally {
			creating = false;
		}
	}

	async function toggleJob(job: CronJob) {
		if (!data.server) return;
		try {
			const res = await fetch(`/api/servers/${data.server.name}/cron/${job.hash}`, {
				method: 'PATCH',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify({ enabled: !job.enabled })
			});
			if (res.ok) {
				await loadJobs();
			} else {
				await modal.error('Failed to update scheduled task');
			}
		} catch {
			await modal.error('Failed to update scheduled task');
		}
	}

	async function deleteJob(job: CronJob) {
		if (!data.server) return;
		const confirmed = await modal.confirm(
			`Delete this scheduled ${job.action}? This cannot be undone.`,
			'Delete Scheduled Task'
		);
		if (!confirmed) return;

		try {
			const res = await fetch(`/api/servers/${data.server.name}/cron/${job.hash}`, {
				method: 'DELETE'
			});
			if (res.ok) {
				await loadJobs();
			} else {
				await modal.error('Failed to delete scheduled task');
			}
		} catch {
			await modal.error('Failed to delete scheduled task');
		}
	}

	function describeCron(expr: string): string {
		const parts = expr.trim().split(/\s+/);
		if (parts.length < 5) return 'Invalid expression';

		const [min, hour, dom, mon, dow] = parts;

		if (min === '0' && hour === '*') return 'Every hour';
		if (min === '0' && hour.startsWith('*/'))
			return `Every ${hour.slice(2)} hours`;
		if (min !== '*' && hour !== '*' && dow === '*' && dom === '*' && mon === '*')
			return `Daily at ${hour.padStart(2, '0')}:${min.padStart(2, '0')} UTC`;
		if (min !== '*' && hour !== '*' && dow !== '*')
			return `${describeDow(dow)} at ${hour.padStart(2, '0')}:${min.padStart(2, '0')} UTC`;
		return expr;
	}

	function describeDow(dow: string): string {
		const names = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
		const idx = parseInt(dow);
		if (!isNaN(idx) && idx >= 0 && idx <= 6) return `Every ${names[idx]}`;
		return `Day ${dow}`;
	}
</script>

<div class="page-header">
	<div>
		<h1>Scheduled Tasks</h1>
		<p class="subtitle">Automate backups, restarts, and more with cron schedules</p>
	</div>
</div>

<div class="cron-layout">
	<section class="panel create-panel">
		<h2>New Scheduled Task</h2>
		<form
			class="create-form"
			onsubmit={(e) => {
				e.preventDefault();
				createJob();
			}}
		>
			<label>
				<span class="label-text">Action</span>
				<div class="action-options">
					{#each Object.entries(actions) as [key, info] (key)}
						<button
							type="button"
							class="action-btn"
							class:selected={action === key}
							onclick={() => (action = key)}
						>
							<span class="action-icon">{key === 'backup' ? '💾' : '🔄'}</span>
							<span class="action-label">{info.label}</span>
						</button>
					{/each}
				</div>
				<p class="help-text">{actions[action]?.description}</p>
			</label>

			<label>
				<span class="label-text">Schedule</span>
				<select bind:value={schedulePreset}>
					{#each Object.entries(presets) as [key, info] (key)}
						<option value={key}>{info.label}</option>
					{/each}
				</select>
			</label>

			{#if schedulePreset === 'custom'}
				<label>
					<span class="label-text">Cron Expression</span>
					<input
						type="text"
						bind:value={customCron}
						placeholder="0 3 * * *"
						spellcheck="false"
					/>
					<p class="help-text">Format: minute hour day-of-month month day-of-week</p>
				</label>
			{/if}

			<div class="schedule-preview">
				<span class="preview-label">Schedule:</span>
				<span class="preview-value">{presetDescription}</span>
				<code class="preview-cron">{effectiveCron}</code>
			</div>

			<label>
				<span class="label-text">Note (optional)</span>
				<input type="text" bind:value={message} placeholder="e.g. Nightly backup" />
			</label>

			<button class="btn-primary" type="submit" disabled={creating}>
				{creating ? 'Creating...' : 'Create Schedule'}
			</button>

			{#if formError}
				<p class="error">{formError}</p>
			{/if}
		</form>
	</section>

	<section class="panel jobs-panel">
		<h2>Active Schedules</h2>

		{#if loading}
			<div class="empty-state">
				<p>Loading...</p>
			</div>
		{:else if jobs.length === 0}
			<div class="empty-state">
				<div class="empty-icon">⏰</div>
				<p>No scheduled tasks yet.</p>
				<p class="hint">Create a schedule to automate backups or restarts.</p>
			</div>
		{:else}
			<div class="jobs-list">
				{#each jobs as job (job.hash)}
					<div class="job-card" class:disabled={!job.enabled}>
						<div class="job-info">
							<div class="job-main">
								<span class="job-action" class:backup={job.action === 'backup'}
									class:restart={job.action === 'restart'}>
									{job.action === 'backup' ? '💾' : '🔄'}
									{job.action}
								</span>
								<code class="job-cron">{job.source}</code>
								{#if !job.enabled}
									<span class="job-badge disabled-badge">Paused</span>
								{/if}
							</div>
							<div class="job-meta">
								<span class="job-schedule">{describeCron(job.source)}</span>
								{#if job.msg}
									<span class="job-msg">{job.msg}</span>
								{/if}
							</div>
						</div>
						<div class="job-actions">
							<label class="toggle" title={job.enabled ? 'Pause' : 'Resume'}>
								<input
									type="checkbox"
									checked={job.enabled}
									onchange={() => toggleJob(job)}
								/>
								<span class="toggle-slider"></span>
							</label>
							<button
								class="btn-icon danger"
								title="Delete"
								onclick={() => deleteJob(job)}
							>
								🗑️
							</button>
						</div>
					</div>
				{/each}
			</div>
		{/if}
	</section>
</div>

<div class="info-section">
	<div class="info-card">
		<h3>How Cron Schedules Work</h3>
		<ul>
			<li><strong>Backup:</strong> Creates an incremental backup using rdiff-backup. Safe to run while the server is online.</li>
			<li><strong>Restart:</strong> Gracefully stops the server, waits 5 seconds, then starts it again.</li>
			<li><strong>Schedule:</strong> Tasks are checked every minute. Use presets or enter a standard 5-field cron expression.</li>
		</ul>
	</div>
	<div class="info-card">
		<h3>Cron Expression Reference</h3>
		<div class="cron-ref">
			<code>┌───── minute (0-59)</code>
			<code>│ ┌───── hour (0-23)</code>
			<code>│ │ ┌───── day of month (1-31)</code>
			<code>│ │ │ ┌───── month (1-12)</code>
			<code>│ │ │ │ ┌───── day of week (0-6, Sun=0)</code>
			<code>* * * * *</code>
		</div>
		<p class="help-text">Examples: <code>0 */6 * * *</code> = every 6h, <code>30 2 * * 1-5</code> = weekdays at 2:30 AM</p>
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

	.cron-layout {
		display: grid;
		grid-template-columns: 360px 1fr;
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

	.help-text {
		font-size: 12px;
		color: #7c87b2;
		margin: 2px 0 0;
	}

	.help-text code {
		background: rgba(20, 24, 39, 0.8);
		padding: 1px 5px;
		border-radius: 4px;
		font-size: 11px;
		color: #9aa2c5;
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

	.action-options {
		display: flex;
		gap: 10px;
	}

	.action-btn {
		flex: 1;
		display: flex;
		align-items: center;
		justify-content: center;
		gap: 8px;
		padding: 12px;
		border-radius: 10px;
		background: #141827;
		border: 1px solid #2a2f47;
		color: #9aa2c5;
		cursor: pointer;
		transition: all 0.2s;
		font-size: 14px;
		font-family: inherit;
	}

	.action-btn:hover {
		border-color: rgba(106, 176, 76, 0.3);
		color: #eef0f8;
	}

	.action-btn.selected {
		background: rgba(106, 176, 76, 0.12);
		border-color: rgba(106, 176, 76, 0.5);
		color: #eef0f8;
	}

	.action-icon {
		font-size: 18px;
	}

	.schedule-preview {
		display: flex;
		flex-wrap: wrap;
		align-items: center;
		gap: 8px;
		padding: 10px 12px;
		background: rgba(88, 101, 242, 0.08);
		border: 1px solid rgba(88, 101, 242, 0.2);
		border-radius: 8px;
		font-size: 13px;
	}

	.preview-label {
		color: #7c87b2;
		font-weight: 500;
	}

	.preview-value {
		color: #c4cff5;
		flex: 1;
	}

	.preview-cron {
		background: rgba(20, 24, 39, 0.8);
		padding: 2px 8px;
		border-radius: 6px;
		font-size: 12px;
		color: #6ab04c;
		border: 1px solid #2a2f47;
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

	.error {
		color: #ff9f9f;
		font-size: 13px;
		margin: 0;
	}

	/* Jobs list */
	.jobs-list {
		display: flex;
		flex-direction: column;
		gap: 12px;
	}

	.job-card {
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

	.job-card:hover {
		border-color: rgba(106, 176, 76, 0.3);
	}

	.job-card.disabled {
		opacity: 0.55;
	}

	.job-info {
		flex: 1;
		min-width: 0;
	}

	.job-main {
		display: flex;
		align-items: center;
		gap: 10px;
		margin-bottom: 6px;
		flex-wrap: wrap;
	}

	.job-action {
		display: inline-flex;
		align-items: center;
		gap: 6px;
		padding: 3px 10px;
		border-radius: 20px;
		font-size: 12px;
		font-weight: 600;
		text-transform: uppercase;
	}

	.job-action.backup {
		background: rgba(106, 176, 76, 0.15);
		color: #b7f5a2;
		border: 1px solid rgba(106, 176, 76, 0.3);
	}

	.job-action.restart {
		background: rgba(91, 158, 255, 0.15);
		color: #a5b4fc;
		border: 1px solid rgba(91, 158, 255, 0.3);
	}

	.job-cron {
		background: rgba(20, 24, 39, 0.8);
		padding: 2px 8px;
		border-radius: 6px;
		font-size: 12px;
		color: #6ab04c;
		border: 1px solid #2a2f47;
	}

	.job-badge {
		font-size: 11px;
		padding: 2px 8px;
		border-radius: 20px;
		font-weight: 600;
		text-transform: uppercase;
	}

	.disabled-badge {
		background: rgba(255, 183, 77, 0.15);
		color: #ffd54f;
		border: 1px solid rgba(255, 183, 77, 0.3);
	}

	.job-meta {
		display: flex;
		gap: 12px;
		font-size: 12px;
		color: #7c87b2;
		flex-wrap: wrap;
	}

	.job-msg {
		color: #9aa2c5;
		font-style: italic;
	}

	.job-actions {
		display: flex;
		align-items: center;
		gap: 10px;
		flex-shrink: 0;
	}

	/* Toggle switch */
	.toggle {
		position: relative;
		display: inline-flex;
		align-items: center;
		flex-shrink: 0;
	}

	.toggle input {
		position: absolute;
		opacity: 0;
		width: 1px;
		height: 1px;
	}

	.toggle-slider {
		width: 42px;
		height: 24px;
		border-radius: 999px;
		background: #2a2f47;
		border: 1px solid #3a3f5a;
		position: relative;
		transition: all 0.2s;
		cursor: pointer;
	}

	.toggle-slider::after {
		content: '';
		position: absolute;
		top: 3px;
		left: 3px;
		width: 16px;
		height: 16px;
		border-radius: 50%;
		background: #c9d1f2;
		transition: transform 0.2s, background 0.2s;
	}

	.toggle input:checked + .toggle-slider {
		background: rgba(106, 176, 76, 0.35);
		border-color: rgba(106, 176, 76, 0.7);
	}

	.toggle input:checked + .toggle-slider::after {
		transform: translateX(18px);
		background: #6ab04c;
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

	.btn-icon.danger:hover {
		background: rgba(255, 92, 92, 0.2);
	}

	.empty-state {
		text-align: center;
		padding: 40px 20px;
		color: #7c87b2;
	}

	.empty-icon {
		font-size: 48px;
		margin-bottom: 12px;
	}

	.hint {
		color: #8890b1;
		font-size: 13px;
		font-style: italic;
	}

	/* Info section */
	.info-section {
		display: grid;
		grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
		gap: 20px;
		margin-top: 8px;
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

	.cron-ref {
		display: flex;
		flex-direction: column;
		gap: 2px;
		margin-bottom: 12px;
	}

	.cron-ref code {
		background: rgba(20, 24, 39, 0.8);
		padding: 2px 8px;
		border-radius: 4px;
		font-size: 12px;
		color: #9aa2c5;
		font-family: 'Consolas', 'Monaco', monospace;
	}

	/* Responsive */
	@media (max-width: 900px) {
		.cron-layout {
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

		.cron-layout {
			gap: 16px;
			margin-bottom: 20px;
		}

		.panel {
			padding: 16px;
			border-radius: 12px;
		}

		.job-card {
			flex-direction: column;
			align-items: stretch;
			gap: 12px;
		}

		.job-actions {
			justify-content: flex-end;
		}

		.info-section {
			grid-template-columns: 1fr;
		}

		.info-card {
			padding: 16px;
		}
	}

	@media (max-width: 480px) {
		.panel {
			padding: 14px;
		}

		.action-options {
			flex-direction: column;
		}

		.schedule-preview {
			flex-direction: column;
			align-items: flex-start;
			gap: 4px;
		}
	}
</style>
