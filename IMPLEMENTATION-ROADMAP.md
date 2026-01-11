# MineOS Complete Implementation Roadmap

## Overview
This document outlines the complete implementation plan for all 20 feature enhancements plus UI polish improvements for MineOS.

---

## Phase 1: Foundation & Infrastructure (Week 1-2)

### Database Schema Setup
**Files to Create:**
- `apps/MineOS.Infrastructure/Data/Migrations/` - EF Core migrations
- `apps/MineOS.Domain/Entities/` - New entity models

**New Entities:**
1. **ServerNote** - Server descriptions and notes
2. **ServerTag** - Color-coded tags for servers
3. **UserFavorite** - Favorited servers per user
4. **PerformanceMetric** - Historical performance data
5. **Alert** - Performance alerts and notifications
6. **WebhookConfig** - Discord/external webhook configurations
7. **PlayerBan** - Enhanced ban records with reasons/duration
8. **ServerTemplate** - Saved server configurations
9. **ScheduledTask** - Enhanced cron job records
10. **AuditLog** - User action logging

### Setup Scripts
**Files to Create:**
- `run.sh` - Interactive Linux/Mac setup
- `run.ps1` - Interactive Windows PowerShell setup
- `docker-compose.yml` - Complete stack orchestration
- `.env.template` - Environment variable template
- `setup-wizard.sh` - First-time setup helper

---

## Phase 2: Core Features (Week 3-6)

### Feature 1: World Management ⭐ HIGH PRIORITY
**Database:** `World` entity (name, seed, type, size, lastBackup)

**Backend:**
```
apps/MineOS.Application/Interfaces/IWorldService.cs
apps/MineOS.Infrastructure/Services/WorldService.cs
apps/MineOS.Api/Endpoints/WorldEndpoints.cs
```

**Frontend:**
```
apps/web/src/routes/(app)/servers/[name]/worlds/+page.svelte
apps/web/src/routes/(app)/servers/[name]/worlds/+page.server.ts
```

**Features:**
- List worlds (world, world_nether, world_the_end)
- Download world as ZIP
- Upload/replace world
- Reset world with confirmation
- View world properties (seed, size, etc.)

**Endpoints:**
- `GET /api/servers/{name}/worlds` - List worlds
- `POST /api/servers/{name}/worlds/{world}/download` - Download world
- `POST /api/servers/{name}/worlds/{world}/upload` - Upload world
- `DELETE /api/servers/{name}/worlds/{world}` - Delete world
- `GET /api/servers/{name}/worlds/{world}/info` - World metadata

---

### Feature 2: Player Management ⭐ HIGH PRIORITY
**Database:** `Player` entity (uuid, lastSeen, playTime, banned, whitelisted)

**Backend:**
```
apps/MineOS.Application/Interfaces/IPlayerService.cs
apps/MineOS.Infrastructure/Services/PlayerService.cs
apps/MineOS.Api/Endpoints/PlayerEndpoints.cs
```

**Frontend:**
```
apps/web/src/routes/(app)/servers/[name]/players/+page.svelte
```

**Features:**
- Whitelist/blacklist editor
- OP management
- Ban with reason and expiry
- Player stats from stats folder
- Recent players list
- Search players

**Endpoints:**
- `GET /api/servers/{name}/players` - List all players
- `POST /api/servers/{name}/players/{uuid}/whitelist` - Add to whitelist
- `DELETE /api/servers/{name}/players/{uuid}/whitelist` - Remove from whitelist
- `POST /api/servers/{name}/players/{uuid}/ban` - Ban player
- `POST /api/servers/{name}/players/{uuid}/op` - OP player
- `GET /api/servers/{name}/players/{uuid}/stats` - Player statistics

---

### Feature 3: Performance Monitoring ⭐ HIGH PRIORITY
**Database:** `PerformanceMetric`, `Alert`

**Backend:**
```
apps/MineOS.Application/Interfaces/IPerformanceService.cs
apps/MineOS.Infrastructure/Services/PerformanceService.cs
apps/MineOS.Infrastructure/Background/PerformanceCollectorService.cs
```

**Frontend:**
```
apps/web/src/routes/(app)/servers/[name]/performance/+page.svelte
apps/web/src/lib/components/PerformanceChart.svelte
```

**Features:**
- Real-time graphs (CPU, RAM, TPS)
- Historical data with Chart.js
- Configurable alerts (low TPS, high memory)
- Crash detection
- Spark/Timings integration

**Endpoints:**
- `GET /api/servers/{name}/performance/realtime` - Current metrics
- `GET /api/servers/{name}/performance/history` - Historical data
- `POST /api/servers/{name}/performance/alerts` - Configure alerts
- `GET /api/servers/{name}/performance/stream` - SSE metrics stream

---

### Feature 4: Bulk Server Operations
**Database:** `ServerGroup` entity

**Backend:**
```
apps/MineOS.Application/Interfaces/IBulkOperationService.cs
```

**Frontend:**
```
apps/web/src/routes/(app)/servers/+page.svelte (enhanced)
apps/web/src/lib/components/BulkActionBar.svelte
```

**Features:**
- Multi-select servers with checkboxes
- Bulk start/stop/restart
- Bulk configuration apply
- Server groups/tags
- Bulk backup creation

**Endpoints:**
- `POST /api/servers/bulk/start` - Start multiple servers
- `POST /api/servers/bulk/stop` - Stop multiple servers
- `POST /api/servers/bulk/config` - Apply config to multiple

---

### Feature 5: Visual Cron Builder
**Database:** Enhanced `CronJob` entity (lastRun, nextRun, enabled, failureCount)

**Frontend:**
```
apps/web/src/routes/(app)/servers/[name]/cron/+page.svelte (enhanced)
apps/web/src/lib/components/CronBuilder.svelte
apps/web/src/lib/components/CronTemplates.svelte
```

**Features:**
- Visual time picker (dropdowns)
- Pre-made templates (daily restart, hourly backup)
- Test run cron job
- Execution history
- Error notifications

---

## Phase 3: Content Management (Week 7-10)

### Feature 6: Plugin Configuration Editor
**Database:** `PluginConfig` entity

**Backend:**
```
apps/MineOS.Application/Interfaces/IPluginService.cs
apps/MineOS.Infrastructure/Services/PluginService.cs
```

**Frontend:**
```
apps/web/src/routes/(app)/servers/[name]/plugins/+page.svelte
apps/web/src/lib/components/YamlEditor.svelte
apps/web/src/lib/components/PluginCard.svelte
```

**Features:**
- List installed plugins
- Enable/disable plugins
- YAML config editor with validation
- Popular plugin templates
- Plugin dependencies check

**Endpoints:**
- `GET /api/servers/{name}/plugins` - List plugins
- `PATCH /api/servers/{name}/plugins/{name}/toggle` - Enable/disable
- `GET /api/servers/{name}/plugins/{name}/config` - Get config
- `PUT /api/servers/{name}/plugins/{name}/config` - Update config

---

### Feature 7: Server Version Migration
**Database:** `MigrationHistory` entity

**Backend:**
```
apps/MineOS.Application/Interfaces/IMigrationService.cs
```

**Frontend:**
```
apps/web/src/routes/(app)/servers/[name]/migrate/+page.svelte
```

**Features:**
- Detect current version
- Show available versions
- Breaking changes warning
- Automatic backup before upgrade
- Rollback capability
- One-click upgrade

---

### Feature 8: Discord Integration
**Database:** `WebhookConfig` entity

**Backend:**
```
apps/MineOS.Infrastructure/Services/DiscordWebhookService.cs
apps/MineOS.Infrastructure/Background/DiscordNotifierService.cs
```

**Frontend:**
```
apps/web/src/routes/(app)/servers/[name]/integrations/+page.svelte
```

**Features:**
- Configure Discord webhooks
- Server event notifications (start/stop/crash)
- Player join/leave events
- Backup completion notifications
- Console commands from Discord (bot)

---

### Feature 9: Search/Command Palette ✅ ALREADY EXISTS
**Frontend Enhancement:**
```
apps/web/src/routes/(app)/search/+page.svelte (enhance existing)
apps/web/src/lib/components/CommandPalette.svelte
```

**Features:**
- Cmd+K keyboard shortcut
- Search servers, files, configs
- Recent actions
- Quick navigation
- Action commands

---

### Feature 10: Mobile-Optimized Console
**Frontend Enhancement:**
```
apps/web/src/routes/(app)/servers/[name]/+page.svelte (mobile CSS)
apps/web/src/lib/components/QuickCommands.svelte
```

**Features:**
- Touch-friendly buttons
- Common commands bar
- Swipe gestures
- Responsive terminal

---

## Phase 4: Templates & Automation (Week 11-13)

### Feature 11: Server Templates
**Database:** `ServerTemplate` entity

**Backend:**
```
apps/MineOS.Application/Interfaces/ITemplateService.cs
```

**Frontend:**
```
apps/web/src/routes/(app)/templates/+page.svelte
apps/web/src/routes/(app)/servers/new/+page.svelte (add template option)
```

**Features:**
- Save server as template
- Create from template
- Template sharing (export/import JSON)
- Community templates

---

### Feature 12: Automatic Server Detection
**Backend:**
```
apps/MineOS.Infrastructure/Services/ServerDiscoveryService.cs
```

**Frontend:**
```
apps/web/src/routes/(app)/import/+page.svelte (enhance)
```

**Features:**
- Scan for existing servers
- Import discovered servers
- Detect server type/version

---

### Feature 13: Resource Pack Manager
**Backend:**
```
apps/MineOS.Application/Interfaces/IResourcePackService.cs
```

**Frontend:**
```
apps/web/src/routes/(app)/servers/[name]/resources/+page.svelte
```

**Features:**
- Upload resource packs
- Set server resource pack URL
- Data pack management
- Pack hosting

---

### Feature 14: Integrated Dynmap
**Frontend:**
```
apps/web/src/routes/(app)/servers/[name]/map/+page.svelte
```

**Features:**
- Embed Dynmap if detected
- Quick access
- Configuration helper

---

### Feature 15: Backup Comparison
**Backend:**
```
apps/MineOS.Application/Interfaces/IBackupComparisonService.cs
```

**Frontend:**
```
apps/web/src/routes/(app)/servers/[name]/backups/+page.svelte (enhance)
apps/web/src/lib/components/BackupDiff.svelte
```

**Features:**
- Compare two backups
- File diff viewer
- Selective restore

---

## Phase 5: Advanced Features (Week 14-16)

### Feature 16: Multi-Host Support
**Database:** `Host` entity, update `Server` with hostId

**Backend:**
```
apps/MineOS.Application/Interfaces/IHostService.cs
apps/MineOS.Infrastructure/Services/RemoteHostService.cs
```

**Features:**
- Manage multiple hosts
- Remote agent communication
- Host health monitoring

---

### Feature 17: Server Cloning
**Backend:**
```
apps/MineOS.Application/Interfaces/ICloneService.cs
```

**Frontend:**
```
apps/web/src/routes/(app)/servers/[name]/+page.svelte (add clone button)
```

**Features:**
- Clone entire server
- Copy with modifications
- Clone to different host

---

### Feature 18: Log Analysis
**Backend:**
```
apps/MineOS.Infrastructure/Services/LogAnalysisService.cs
apps/MineOS.Infrastructure/ML/CrashDetectionModel.cs
```

**Frontend:**
```
apps/web/src/routes/(app)/servers/[name]/logs/+page.svelte
```

**Features:**
- Parse logs for errors
- Crash report detection
- Suggest fixes
- Error trending

---

### Feature 19: API & Webhooks
**Database:** `ApiKey`, `Webhook` entities

**Backend:**
```
apps/MineOS.Api/Endpoints/WebhookEndpoints.cs
```

**Frontend:**
```
apps/web/src/routes/(app)/admin/api/+page.svelte
```

**Features:**
- Generate API keys
- Webhook configuration
- Event subscriptions
- API documentation

---

### Feature 20: Performance Profiling
**Backend:**
```
apps/MineOS.Infrastructure/Services/ProfilingService.cs
```

**Frontend:**
```
apps/web/src/routes/(app)/servers/[name]/profiling/+page.svelte
```

**Features:**
- Upload Spark reports
- Parse Timings
- Suggest optimizations
- Performance score

---

## Phase 6: UI Polish (Week 17-18)

### Quick Wins
1. **Server Notes** - Add notes field to ServerDetail
2. **Favorites** - Star icon on server cards
3. **Server Icons** - Upload custom 64x64 icons
4. **Color Tags** - Tag servers with colors
5. **Recently Viewed** - Track and display
6. **File Upload Drag & Drop** - Enhance file manager
7. **Console History** - Arrow up/down in console
8. **Statistics Widget** - Server statistics
9. **Update Notifications** - Check for updates
10. **Health Score** - Green/yellow/red indicator

### UI Components
```
apps/web/src/lib/components/Toast.svelte
apps/web/src/lib/components/ConfirmDialog.svelte
apps/web/src/lib/components/LoadingSkeleton.svelte
apps/web/src/lib/components/EmptyState.svelte
apps/web/src/lib/components/KeyboardShortcuts.svelte
apps/web/src/lib/components/Onboarding.svelte
```

### Accessibility
- ARIA labels on all interactive elements
- Keyboard navigation
- Screen reader support
- Focus indicators
- Color contrast compliance

---

## Database Schema Summary

### New Tables Required

```sql
-- Core Features
CREATE TABLE ServerNotes (Id, ServerId, Content, UserId, CreatedAt);
CREATE TABLE ServerTags (Id, ServerId, Name, Color);
CREATE TABLE UserFavorites (UserId, ServerId, CreatedAt);
CREATE TABLE ServerIcons (Id, ServerId, FileName, UploadedAt);
CREATE TABLE ServerTemplates (Id, Name, Config, CreatedBy, CreatedAt);

-- Performance & Monitoring
CREATE TABLE PerformanceMetrics (Id, ServerId, Timestamp, Cpu, Ram, Tps, PlayerCount);
CREATE TABLE Alerts (Id, ServerId, Type, Threshold, Enabled, WebhookUrl);
CREATE TABLE AuditLogs (Id, UserId, Action, ServerId, Details, Timestamp);

-- Player Management
CREATE TABLE Players (Id, ServerId, Uuid, Name, LastSeen, PlayTimeSeconds, Banned, Whitelisted, IsOp);
CREATE TABLE PlayerBans (Id, PlayerId, Reason, BannedBy, BannedAt, ExpiresAt);
CREATE TABLE PlayerStats (Id, PlayerId, StatKey, StatValue);

-- Content & Config
CREATE TABLE Plugins (Id, ServerId, Name, Version, Enabled, ConfigPath);
CREATE TABLE Worlds (Id, ServerId, Name, Seed, Type, SizeBytes, LastBackup);
CREATE TABLE ResourcePacks (Id, ServerId, Name, Url, Hash, UploadedAt);

-- Integration & Automation
CREATE TABLE WebhookConfigs (Id, ServerId, Type, Url, Events, Enabled);
CREATE TABLE CronJobs (Id, ServerId, Schedule, Command, LastRun, NextRun, Enabled, FailureCount);
CREATE TABLE MigrationHistory (Id, ServerId, FromVersion, ToVersion, Status, MigratedAt);

-- Multi-Host
CREATE TABLE Hosts (Id, Name, Hostname, Port, ApiKey, Status, LastPing);
ALTER TABLE Servers ADD COLUMN HostId INT NULL;

-- API & Webhooks
CREATE TABLE ApiKeys (Id, UserId, Key, Name, Permissions, CreatedAt, ExpiresAt);
CREATE TABLE Webhooks (Id, Name, Url, Events, Secret, Enabled);
```

---

## Setup Scripts Requirements

### run.sh / run.ps1 Features

1. **Dependency Check**
   - Docker & Docker Compose installed
   - Minimum versions
   - .NET 8 SDK (for development)
   - Node.js & npm (for development)

2. **Configuration Wizard**
   - Database choice (SQLite/PostgreSQL/MySQL)
   - Admin credentials
   - Base directory for servers
   - Port configuration
   - Optional: CurseForge API key
   - Optional: Discord webhook URLs
   - Optional: Backup storage location

3. **Environment Setup**
   - Generate `.env` from template
   - Create required directories
   - Set file permissions
   - Generate JWT secret
   - Generate API keys

4. **Database Initialization**
   - Run EF Core migrations
   - Seed default data
   - Create admin user

5. **Docker Compose**
   - Start services
   - Health checks
   - Show access URLs
   - Display credentials

6. **First Run Guide**
   - Print access instructions
   - Show default credentials
   - Link to documentation
   - Troubleshooting tips

---

## Implementation Priority

### Must-Have (MVP+)
1. World Management
2. Player Management
3. Performance Monitoring
4. Visual Cron Builder
5. Plugin Config Editor
6. Setup Scripts

### Should-Have
7. Discord Integration
8. Server Templates
9. Bulk Operations
10. Mobile Console
11. UI Polish (all 10)

### Nice-to-Have
12. Version Migration
13. Backup Comparison
14. Server Cloning
15. Search Enhancement
16. Resource Pack Manager

### Future/Advanced
17. Multi-Host Support
18. Log Analysis with ML
19. API & Webhooks
20. Performance Profiling
21. Dynmap Integration
22. Auto Server Detection

---

## Success Metrics

- All features documented
- 100% API endpoint coverage
- Mobile-responsive design
- < 2s page load times
- Zero security vulnerabilities
- Comprehensive error handling
- Docker setup in < 5 minutes

---

## Next Steps

1. ✅ Review and approve this roadmap
2. Create database migrations
3. Build setup scripts (run.sh/run.ps1)
4. Implement Phase 1 features
5. Iterate based on feedback
