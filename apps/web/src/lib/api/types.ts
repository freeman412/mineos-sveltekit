export type DiskMetrics = {
	availableBytes: number;
	freeBytes: number;
	totalBytes: number;
};

export type HostMetrics = {
	uptimeSeconds: number;
	freeMemBytes: number;
	loadAvg: number[];
	disk: DiskMetrics;
};

export type ServerSummary = {
	name: string;
	up: boolean;
	profile?: string | null;
	port?: number | null;
	playersOnline?: number | null;
	playersMax?: number | null;
	memoryBytes?: number | null;
	needsRestart: boolean;
};

export type Profile = {
	id: string;
	group: string;
	type: string;
	version: string;
	releaseTime: string;
	url: string;
	filename: string;
	downloaded: boolean;
	progress: unknown | null;
};

export type ArchiveEntry = {
	time: string;
	size: number;
	filename: string;
};

export type ClientPackageEntry = {
	time: string;
	size: number;
	filename: string;
};

export type ApiResult<T> = {
	data: T | null;
	error: string | null;
};

// Server management types
export type ServerDetail = {
	name: string;
	createdAt: string;
	ownerUid: number;
	ownerGid: number;
	ownerUsername: string;
	ownerGroupname: string;
	status: string;
	javaPid: number | null;
	screenPid: number | null;
	config: ServerConfig | null;
	eulaAccepted: boolean;
	needsRestart: boolean;
};

export type ServerConfig = {
	java: JavaConfig;
	minecraft: MinecraftConfig;
	onReboot: OnRebootConfig;
	autoRestart: AutoRestartConfig;
};

export type JavaConfig = {
	javaBinary: string;
	javaXmx: number;
	javaXms: number;
	javaTweaks: string | null;
	jarFile: string | null;
	jarArgs: string | null;
};

export type MinecraftConfig = {
	profile: string | null;
	unconventional: boolean;
};

export type OnRebootConfig = {
	start: boolean;
};

export type AutoRestartConfig = {
	enabled: boolean;
	maxAttempts: number;
	cooldownSeconds: number;
	attemptResetMinutes: number;
	notifyOnCrash: boolean;
	notifyOnRestart: boolean;
};

export type CrashEvent = {
	id: number;
	serverName: string;
	detectedAt: string;
	crashType: string;
	crashDetails: string | null;
	autoRestartAttempted: boolean;
	autoRestartSucceeded: boolean;
};

export type WatchdogStatus = {
	serverName: string;
	isMonitoring: boolean;
	wasRunning: boolean;
	restartAttempts: number;
	lastCrashTime: string | null;
	lastRestartAttempt: string | null;
	cooldownEndsAt: string | null;
};

export type ServerHeartbeat = {
	name: string;
	status: string;
	javaPid: number | null;
	screenPid: number | null;
	ping: PingInfo | null;
	memoryBytes: number | null;
};

export type PingInfo = {
	protocol: number;
	serverVersion: string;
	motd: string;
	playersOnline: number;
	playersMax: number;
};

export type CreateServerRequest = {
	name: string;
	ownerUid: number;
	ownerGid: number;
};

export type CloneServerRequest = {
	newName: string;
};

export type InstalledMod = {
	fileName: string;
	sizeBytes: number;
	modifiedAt: string;
	isDisabled: boolean;
};

export type InstalledPlugin = {
	fileName: string;
	sizeBytes: number;
	modifiedAt: string;
	isDisabled: boolean;
};

export type ModrinthProjectHit = {
	projectId: string;
	slug: string;
	title: string;
	description: string;
	iconUrl: string | null;
	downloads: number;
	versions: string[];
	categories: string[];
};

export type ModrinthSearchResult = {
	index: number;
	pageSize: number;
	totalHits: number;
	results: ModrinthProjectHit[];
};

export type ModrinthProject = {
	id: string;
	slug: string;
	title: string;
	description: string;
	body: string | null;
	projectType: string;
	downloads: number;
	categories: string[];
	gameVersions: string[];
	loaders: string[];
	iconUrl: string | null;
	clientSide?: string | null;
	serverSide?: string | null;
};

export type ModrinthVersionFile = {
	url: string;
	fileName: string;
	size: number;
	primary: boolean;
};

export type ModrinthVersion = {
	id: string;
	projectId?: string;
	name: string;
	versionNumber: string;
	datePublished: string;
	downloads: number;
	gameVersions: string[];
	loaders: string[];
	files: ModrinthVersionFile[];
	dependencies?: ModrinthDependency[];
};

export type ModrinthDependency = {
	projectId: string | null;
	versionId: string | null;
	dependencyType: string;
};

export type CurseForgeCategory = {
	id: number;
	name: string;
	url: string | null;
	iconUrl: string | null;
};

export type CurseForgeFile = {
	id: number;
	fileName: string;
	fileLength: number;
	fileDate: string;
	downloadUrl: string | null;
	gameVersions: string[];
	releaseType: number | null;
};

export type CurseForgeModSummary = {
	id: number;
	name: string;
	summary: string;
	downloadCount: number;
	logoUrl: string | null;
	latestFileId: number | null;
	categories: CurseForgeCategory[];
};

export type CurseForgeMod = {
	id: number;
	name: string;
	summary: string;
	downloadCount: number;
	logoUrl: string | null;
	categories: CurseForgeCategory[];
	latestFiles: CurseForgeFile[];
};

export type CurseForgeSearchResult = {
	index: number;
	pageSize: number;
	resultCount: number;
	totalCount: number;
	results: CurseForgeModSummary[];
};

// World Management
export type World = {
	name: string;
	type: string;
	sizeBytes: number;
	lastModified: string | null;
};

export type WorldInfo = {
	name: string;
	type: string;
	sizeBytes: number;
	seed: string | null;
	levelName: string | null;
	gameMode: string | null;
	difficulty: string | null;
	hardcore: boolean | null;
	lastModified: string | null;
	fileCount: number;
	directoryCount: number;
};

// Performance Monitoring
export type PerformanceSample = {
	serverName: string;
	timestamp: string;
	isRunning: boolean;
	cpuPercent: number;
	ramUsedMb: number;
	ramTotalMb: number;
	tps: number | null;
	playerCount: number;
};

export type SparkStatus = {
	installed: boolean;
	mode: string | null;
	jarName: string | null;
	version: string | null;
	reportCount: number;
	reports: string[];
};

// Player Management
export type PlayerSummary = {
	uuid: string;
	name: string;
	whitelisted: boolean;
	isOp: boolean;
	opLevel: number | null;
	opBypassPlayerLimit: boolean | null;
	banned: boolean;
	banReason: string | null;
	banExpiresAt: string | null;
	lastSeen: string | null;
	playTimeSeconds: number | null;
};

export type PlayerStats = {
	uuid: string;
	rawJson: string;
	lastModified: string | null;
};

// Mojang API types
export type MojangProfile = {
	uuid: string;
	name: string;
	avatarUrl: string;
};

// Forge types
export type ForgeVersion = {
	minecraftVersion: string;
	forgeVersion: string;
	fullVersion: string;
	isRecommended: boolean;
	isLatest: boolean;
	releaseDate: string | null;
};

export type ForgeInstallResult = {
	installId: string;
	status: string;
	error: string | null;
};

export type ForgeInstallStatus = {
	installId: string;
	minecraftVersion: string;
	forgeVersion: string;
	serverName: string;
	status: string;
	progress: number;
	currentStep: string | null;
	error: string | null;
	output: string | null;
	startedAt: string;
	completedAt: string | null;
};

// Modpack types
export type ModpackInstallProgress = {
	jobId: string;
	serverName: string;
	status: string;
	percentage: number;
	currentStep: string | null;
	currentModIndex: number;
	totalMods: number;
	currentModName: string | null;
	outputLines: string[];
	error: string | null;
};

export type InstalledModpack = {
	id: number;
	name: string;
	version: string | null;
	logoUrl: string | null;
	modCount: number;
	installedAt: string;
};

export type InstalledModWithModpack = {
	fileName: string;
	sizeBytes: number;
	modifiedAt: string;
	isDisabled: boolean;
	modpackId: number | null;
	modpackName: string | null;
	curseForgeProjectId: number | null;
};

// Background job types
export type JobStatus = {
	jobId: string;
	type: string;
	serverName: string;
	status: 'queued' | 'running' | 'completed' | 'failed';
	percentage: number;
	message: string | null;
	startedAt: string;
	completedAt: string | null;
	error: string | null;
};

export type ActiveJobsResponse = {
	jobs: JobStatus[];
	modpackInstalls: ModpackInstallProgress[];
};

// Notification types
export type SystemNotification = {
	id: number;
	type: 'info' | 'warning' | 'error' | 'success';
	title: string;
	message: string;
	createdAt: string;
	dismissedAt: string | null;
	isRead: boolean;
	serverName: string | null;
};

// Player Activity types
export type PlayerActivityEvent = {
	id: number;
	serverName: string;
	playerUuid: string;
	playerName: string;
	timestamp: string;
	eventType: string;
	eventData: string | null;
};

export type PlayerSession = {
	id: number;
	serverName: string;
	playerUuid: string;
	playerName: string;
	joinedAt: string;
	leftAt: string | null;
	durationSeconds: number | null;
	leaveReason: string | null;
};

export type PlayerActivityStats = {
	playerUuid: string;
	playerName: string;
	totalSessions: number;
	totalPlayTimeSeconds: number;
	averageSessionSeconds: number;
	firstSeen: string | null;
	lastSeen: string | null;
	joinCount: number;
	leaveCount: number;
	deathCount: number;
};
