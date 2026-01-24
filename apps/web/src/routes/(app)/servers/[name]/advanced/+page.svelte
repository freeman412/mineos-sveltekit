<script lang="ts">
	import { enhance } from '$app/forms';
	import type { PageData, ActionData } from './$types';
	import type { ServerConfig, Profile } from '$lib/api/types';

	let { data, form }: { data: PageData; form: ActionData } = $props();

	let config = $state<ServerConfig>({
			java: {
				javaBinary: '',
				javaXmx: 4096,
				javaXms: 4096,
				javaTweaks: null,
				jarFile: null,
				jarArgs: null
			},
			minecraft: {
				profile: null,
				unconventional: false
			},
			onReboot: {
				start: false
			},
			autoRestart: {
				enabled: false,
				maxAttempts: 3,
				cooldownSeconds: 300,
				attemptResetMinutes: 30,
				notifyOnCrash: true,
				notifyOnRestart: true
			}
		}
	);

	let loading = $state(false);
	let selectedProfile = $state(config.minecraft.profile ?? '');

	$effect(() => {
		config = data.config.data || {
			java: {
				javaBinary: '',
				javaXmx: 4096,
				javaXms: 4096,
				javaTweaks: null,
				jarFile: null,
				jarArgs: null
			},
			minecraft: {
				profile: null,
				unconventional: false
			},
			onReboot: {
				start: false
			},
			autoRestart: {
				enabled: false,
				maxAttempts: 3,
				cooldownSeconds: 60,
				attemptResetMinutes: 30,
				notifyOnCrash: true,
				notifyOnRestart: true
			}
		};
		selectedProfile = config.minecraft.profile ?? '';
	});

	const javaTweaksPresets = [
		{
			id: 'custom',
			label: 'Custom (manual)',
			value: null
		},
		{
			id: 'none',
			label: 'None (clear tweaks)',
			value: ''
		},
		{
			id: 'low-latency',
			label: 'Low-latency G1 (Freeman)',
			value:
				'-XX:+UseG1GC -Dsun.rmi.dgc.server.gcInterval=2147483646 -XX:+UnlockExperimentalVMOptions -XX:G1NewSizePercent=20 -XX:G1ReservePercent=20 -XX:MaxGCPauseMillis=50 -XX:G1HeapRegionSize=32M'
		},
		{
			id: 'balanced-g1',
			label: 'Balanced G1GC (general)',
			value:
				'-XX:+UseG1GC -XX:+ParallelRefProcEnabled -XX:MaxGCPauseMillis=200 -XX:InitiatingHeapOccupancyPercent=20 -XX:+DisableExplicitGC'
		},
		{
			id: 'aikar-high',
			label: 'Aikar G1GC (8GB+)',
			value:
				'-XX:+UseG1GC -XX:+ParallelRefProcEnabled -XX:MaxGCPauseMillis=200 -XX:+UnlockExperimentalVMOptions -XX:+DisableExplicitGC -XX:+AlwaysPreTouch -XX:G1NewSizePercent=30 -XX:G1MaxNewSizePercent=40 -XX:G1HeapRegionSize=8M -XX:G1ReservePercent=20 -XX:G1HeapWastePercent=5 -XX:G1MixedGCCountTarget=4 -XX:InitiatingHeapOccupancyPercent=15 -XX:G1MixedGCLiveThresholdPercent=90 -XX:G1RSetUpdatingPauseTimePercent=5 -XX:SurvivorRatio=32 -XX:+PerfDisableSharedMem -XX:MaxTenuringThreshold=1'
		}
	];

	const presetByValue = new Map(
		javaTweaksPresets
			.filter((preset) => preset.value !== null)
			.map((preset) => [preset.value as string, preset.id])
	);

	let selectedJavaTweaksPreset = $state(
		presetByValue.get(config.java.javaTweaks ?? '') ?? 'custom'
	);

	const jarOptions = $derived.by(() => {
		const options = new Set<string>();
		const jarFile = config.java.jarFile?.trim();
		if (jarFile) {
			options.add(jarFile);
		}
		for (const entry of data.jarFiles?.data ?? []) {
			options.add(entry);
		}
		for (const entry of data.forgeArgFiles?.data ?? []) {
			options.add(entry);
		}
		return Array.from(options).sort((a, b) => a.localeCompare(b));
	});

	const profiles = $derived((data.profiles?.data ?? []) as Profile[]);
	const profileGroups = $derived.by(() => {
		const groups: Record<string, Profile[]> = {};
		for (const profile of profiles) {
			if (!groups[profile.group]) {
				groups[profile.group] = [];
			}
			groups[profile.group].push(profile);
		}
		return groups;
	});
	const selectedProfileMissing = $derived.by(() => {
		if (!selectedProfile) return false;
		return !profiles.some((profile) => profile.id === selectedProfile);
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

	$effect(() => {
		config.minecraft.profile = selectedProfile.trim() ? selectedProfile : null;
	});

	function applyJavaTweaksPreset(presetId: string) {
		selectedJavaTweaksPreset = presetId;
		const preset = javaTweaksPresets.find((item) => item.id === presetId);
		if (!preset) return;
		if (preset.value === null) return;
		config.java.javaTweaks = preset.value;
	}

	function markTweaksCustom() {
		selectedJavaTweaksPreset = 'custom';
	}
</script>

<div class="page">
	<div class="page-header">
		<div>
			<h2>Server Configuration</h2>
			<p class="subtitle">Edit server.config advanced settings</p>
		</div>
	</div>

	{#if data.config.error}
		<div class="error-box">
			<p>Failed to load config: {data.config.error}</p>
		</div>
	{:else}
		<form
			method="POST"
			use:enhance={() => {
				loading = true;
				return async ({ update }) => {
					await update();
					loading = false;
				};
			}}
		>
			<input type="hidden" name="config" value={JSON.stringify(config)} />

			<div class="config-sections">
				<!-- Java Configuration -->
				<div class="section">
					<h3>Java Settings</h3>
					<div class="form-grid">
						<div class="form-field">
							<label for="java-binary">Java Binary</label>
							<input
								type="text"
								id="java-binary"
								bind:value={config.java.javaBinary}
								placeholder="java (default)"
								list="java-binary-options"
							/>
							<datalist id="java-binary-options">
								<option value="java"></option>
								<option value="/usr/lib/jvm/temurin-8-jre/bin/java"></option>
								<option value="/usr/lib/jvm/java-17-openjdk-amd64/bin/java"></option>
								<option value="/usr/lib/jvm/temurin-21-jre/bin/java"></option>
							</datalist>
							<p class="field-hint">
								Use <code>java</code> for the default runtime or set a full path to target Java 8/17/21.
							</p>
						</div>

						<div class="form-field">
							<label for="java-xmx">Max Memory (Xmx MB)</label>
							<input
								type="number"
								id="java-xmx"
								bind:value={config.java.javaXmx}
								min="128"
								step="128"
							/>
						</div>

						<div class="form-field">
							<label for="java-xms">Min Memory (Xms MB)</label>
							<input
								type="number"
								id="java-xms"
								bind:value={config.java.javaXms}
								min="128"
								step="128"
							/>
						</div>

						<div class="form-field">
							<label for="jar-file">JAR File</label>
							<input
								type="text"
								id="jar-file"
								bind:value={config.java.jarFile}
								placeholder="server.jar"
								list="jar-file-options"
							/>
							<datalist id="jar-file-options">
								{#each jarOptions as option}
									<option value={option}></option>
								{/each}
							</datalist>
							{#if data.jarFiles?.error}
								<p class="error-text">{data.jarFiles.error}</p>
							{/if}
							<p class="field-hint">
								Select from detected JARs or Forge args files, or enter a custom path.
							</p>
						</div>

						<div class="form-field full-width">
							<label for="java-tweaks-preset">Java Tweaks Preset</label>
							<select
								id="java-tweaks-preset"
								bind:value={selectedJavaTweaksPreset}
								onchange={(event) => applyJavaTweaksPreset((event.target as HTMLSelectElement).value)}
							>
								{#each javaTweaksPresets as preset}
									<option value={preset.id}>{preset.label}</option>
								{/each}
							</select>
							<p class="field-hint">
								Choose a tuned preset or keep custom tweaks below.
							</p>
						</div>

						<div class="form-field full-width">
							<label for="java-tweaks">Java Tweaks (optional)</label>
							<input
								type="text"
								id="java-tweaks"
								bind:value={config.java.javaTweaks}
								placeholder="-XX:+UseG1GC -XX:+ParallelRefProcEnabled"
								oninput={markTweaksCustom}
							/>
						</div>

						<div class="form-field full-width">
							<label for="jar-args">JAR Arguments (optional)</label>
							<input
								type="text"
								id="jar-args"
								bind:value={config.java.jarArgs}
								placeholder="--nogui"
							/>
						</div>
					</div>
				</div>

				<!-- Minecraft Configuration -->
				<div class="section">
					<h3>Minecraft Settings</h3>
					<div class="form-grid">
					<div class="form-field">
						<label for="profile">Profile (optional)</label>
						<select id="profile" bind:value={selectedProfile}>
							<option value="">None</option>
							{#if selectedProfileMissing}
								<option value={selectedProfile}>{selectedProfile} (custom)</option>
							{/if}
							{#each Object.entries(profileGroups) as [groupName, items]}
								<optgroup label={formatGroupLabel(groupName)}>
									{#each items as profile}
										<option value={profile.id}>
											{profile.version} ({profile.type}{profile.downloaded ? '' : ' - will download'})
										</option>
									{/each}
								</optgroup>
							{/each}
						</select>
						{#if data.profiles?.error}
							<p class="error-text">Failed to load profiles: {data.profiles.error}</p>
						{/if}
					</div>

						<div class="form-field">
							<label class="checkbox-label">
								<input type="checkbox" bind:checked={config.minecraft.unconventional} />
								<span>Unconventional Server</span>
							</label>
							<p class="field-hint">Enable for modded servers or custom setups</p>
						</div>
					</div>
				</div>

				<!-- On Reboot Configuration -->
				<div class="section">
					<h3>On Reboot</h3>
					<div class="form-grid">
						<div class="form-field">
							<label class="checkbox-label">
								<input type="checkbox" bind:checked={config.onReboot.start} />
								<span>Start server on system reboot</span>
							</label>
						</div>
					</div>
				</div>

				<!-- Auto-Restart Configuration -->
				<div class="section">
					<h3>Crash Detection & Auto-Restart</h3>
					<p class="section-desc">Automatically restart the server when it crashes unexpectedly.</p>
					<div class="form-grid">
						<div class="form-field full-width">
							<label class="checkbox-label">
								<input type="checkbox" bind:checked={config.autoRestart.enabled} />
								<span>Enable auto-restart on crash</span>
							</label>
							<p class="field-hint">When enabled, the server will automatically restart if it crashes or stops unexpectedly.</p>
						</div>

						{#if config.autoRestart.enabled}
							<div class="form-field">
								<label for="max-attempts">Max Restart Attempts</label>
								<input
									type="number"
									id="max-attempts"
									bind:value={config.autoRestart.maxAttempts}
									min="0"
									max="10"
								/>
								<p class="field-hint">Maximum restart attempts before giving up (0 = unlimited)</p>
							</div>

							<div class="form-field">
								<label for="cooldown">Cooldown (seconds)</label>
								<input
									type="number"
									id="cooldown"
									bind:value={config.autoRestart.cooldownSeconds}
									min="30"
									max="3600"
									step="30"
								/>
								<p class="field-hint">Minimum time between restart attempts</p>
							</div>

							<div class="form-field">
								<label for="reset-time">Stability Reset (minutes)</label>
								<input
									type="number"
									id="reset-time"
									bind:value={config.autoRestart.attemptResetMinutes}
									min="5"
									max="120"
								/>
								<p class="field-hint">Reset attempt counter after this many minutes of uptime</p>
							</div>

							<div class="form-field">
								<label class="checkbox-label">
									<input type="checkbox" bind:checked={config.autoRestart.notifyOnCrash} />
									<span>Notify on crash</span>
								</label>
								<p class="field-hint">Send a notification when a crash is detected</p>
							</div>

							<div class="form-field">
								<label class="checkbox-label">
									<input type="checkbox" bind:checked={config.autoRestart.notifyOnRestart} />
									<span>Notify on auto-restart</span>
								</label>
								<p class="field-hint">Send a notification when auto-restart is triggered</p>
							</div>
						{/if}
					</div>
				</div>
			</div>

			{#if form?.error}
				<div class="error-text">{form.error}</div>
			{/if}

			{#if form?.success}
				<div class="success-text">Configuration saved successfully!</div>
			{/if}

			<div class="actions">
				<button type="submit" class="btn-primary" disabled={loading}>
					{loading ? 'Saving...' : 'Save Configuration'}
				</button>
			</div>
		</form>
	{/if}
</div>

<style>
	.page {
		display: flex;
		flex-direction: column;
		gap: 24px;
	}

	.page-header {
		display: flex;
		justify-content: space-between;
		align-items: center;
		gap: 24px;
	}

	h2 {
		margin: 0 0 8px;
		font-size: 24px;
		font-weight: 600;
	}

	.subtitle {
		margin: 0;
		color: #aab2d3;
		font-size: 14px;
	}

	.error-box {
		background: rgba(255, 92, 92, 0.1);
		border: 1px solid rgba(255, 92, 92, 0.3);
		border-radius: 12px;
		padding: 16px 20px;
		color: #ff9f9f;
	}

	.error-box p {
		margin: 0;
	}

	.config-sections {
		display: flex;
		flex-direction: column;
		gap: 24px;
	}

	.section {
		background: #1a1e2f;
		border-radius: 16px;
		padding: 24px;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.35);
	}

	.section h3 {
		margin: 0 0 8px;
		font-size: 18px;
		font-weight: 600;
		color: #9aa2c5;
	}

	.section-desc {
		margin: 0 0 20px;
		font-size: 14px;
		color: #7c87b2;
	}

	.section h3:last-of-type:not(:has(+ .section-desc)) {
		margin-bottom: 20px;
	}

	.form-grid {
		display: grid;
		grid-template-columns: repeat(auto-fit, minmax(250px, 1fr));
		gap: 20px;
	}

	.form-field {
		display: flex;
		flex-direction: column;
		gap: 8px;
	}

	.full-width {
		grid-column: 1 / -1;
	}

	.form-field label {
		font-size: 14px;
		font-weight: 500;
		color: #9aa2c5;
	}

	.form-field input[type='text'],
	.form-field input[type='number'],
	.form-field select {
		background: #141827;
		border: 1px solid #2a2f47;
		border-radius: 8px;
		padding: 10px 14px;
		color: #eef0f8;
		font-family: inherit;
		font-size: 14px;
	}

	.form-field input:focus {
		outline: none;
		border-color: #5865f2;
	}

	.checkbox-label {
		display: flex;
		align-items: center;
		gap: 10px;
		cursor: pointer;
		font-weight: normal;
	}

	.checkbox-label input[type='checkbox'] {
		width: 18px;
		height: 18px;
		cursor: pointer;
	}

	.checkbox-label span {
		color: #eef0f8;
	}

	.field-hint {
		margin: 0;
		font-size: 12px;
		color: #8890b1;
	}

	.error-text {
		color: #ff9f9f;
		font-size: 14px;
	}

	.success-text {
		color: #7ae68d;
		font-size: 14px;
	}

	.actions {
		display: flex;
		justify-content: flex-end;
		padding-top: 8px;
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
		opacity: 0.6;
		cursor: not-allowed;
	}

	@media (max-width: 640px) {
		.form-grid {
			grid-template-columns: 1fr;
		}
	}
</style>
