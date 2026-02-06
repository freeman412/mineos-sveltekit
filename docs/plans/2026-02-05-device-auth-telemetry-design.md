# Device Authorization & Telemetry Improvements Design

**Date**: 2026-02-05
**Status**: Approved
**Source**: mineos-site/APP_TEAM_REQUIREMENTS.md

---

## Overview

This design implements two features from the MineOS site team requirements:

1. **Device Authorization Flow** - OAuth 2.0 Device Authorization Grant for linking MineOS installations to mineos.net user accounts
2. **Telemetry Improvements** - Enhanced data collection for install, usage, and error events

---

## 1. Device Authorization Flow

### New Entity: LinkedAccount

```csharp
public sealed class LinkedAccount
{
    public int Id { get; set; }
    public required string AccessToken { get; set; }      // Encrypted
    public required string TokenType { get; set; }        // "bearer"
    public required DateTimeOffset ExpiresAt { get; set; }
    public required string UserId { get; set; }           // mineos.net user ID
    public required string InstallationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

Table: `linked_accounts` (SQLite)

### New Service: IDeviceAuthService

```csharp
public interface IDeviceAuthService
{
    Task<DeviceCodeResponse> InitiateAsync(CancellationToken ct = default);
    Task<LinkStatus> GetStatusAsync(CancellationToken ct = default);
    Task UnlinkAsync(CancellationToken ct = default);
    Task<LinkedAccount?> GetLinkedAccountAsync(CancellationToken ct = default);
}
```

### DTOs

```csharp
public record DeviceCodeResponse(
    string UserCode,
    string VerificationUri,
    string VerificationUriComplete,
    int ExpiresIn
);

public record LinkStatus(
    string Status,  // "not_started" | "pending" | "linked" | "expired" | "error"
    string? UserCode,
    string? VerificationUri,
    string? VerificationUriComplete,
    int? ExpiresIn,
    LinkedAccountInfo? LinkedAccount,
    string? Error
);

public record LinkedAccountInfo(
    string UserId,
    DateTimeOffset LinkedAt,
    DateTimeOffset ExpiresAt
);
```

### Implementation: DeviceAuthService

**InitiateAsync()**:
1. Call `POST https://mineos.net/api/auth/device` with `installation_id`
2. Store `device_code` and expiry in memory (singleton state)
3. Start background polling task
4. Return user-facing code and URLs

**Background Polling**:
- Poll `POST https://mineos.net/api/auth/device/poll` every `interval` seconds
- Handle responses:
  - `authorization_pending` (400): continue polling
  - `slow_down` (429): increase interval by 5 seconds
  - `expired_token` (400): stop polling, set status to expired
  - Success (200): save `LinkedAccount` to DB, stop polling

**Token Storage**:
- Encrypt `access_token` using ASP.NET Data Protection API
- Store in `linked_accounts` table
- Only one linked account per installation

### API Endpoints

```
GET  /api/v1/account/link        → Initiates linking
GET  /api/v1/account/link/status → Returns current status
DELETE /api/v1/account/link      → Unlinks account
GET  /api/v1/account             → Returns linked account info
```

All require authentication.

---

## 2. Telemetry Improvements

### Install Telemetry (POST /api/telemetry/install)

**New fields in TelemetryService.RegisterInstallationAsync()**:

| Field | Type | Priority | Source |
|-------|------|----------|--------|
| `java_version` | string | High | `java -version` output, cached |
| `disk_total_gb` | int | High | `DriveInfo` for data directory |
| `disk_available_gb` | int | High | `DriveInfo` for data directory |
| `container_engine` | string? | Low | Detect docker/podman/lxc |
| `container_version` | string? | Low | `docker --version` etc. |

**Helper Methods**:
- `GetJavaVersion()` - Parse `java -version` stderr, cache result
- `GetDiskInfo()` - Use `DriveInfo` class for minecraft base directory
- `GetContainerEngine()` - Check `/.dockerenv`, `/run/.containerenv`, cgroups
- `GetContainerVersion()` - Run engine CLI with `--version`

### Usage Telemetry (POST /api/telemetry/usage)

**New fields in TelemetryService.ReportUsageAsync()**:

| Field | Type | Priority | Source |
|-------|------|----------|--------|
| `server_types` | string[] | Medium | Query servers, extract type |
| `backup_count` | int | Medium | Count backup files |
| `last_backup_success` | bool | Medium | Check last backup result |
| `backup_total_size_mb` | int | Medium | Sum backup file sizes |
| `feature_usage` | object | Low | FeatureUsageTracker service |

### Error Event Standardization

**New helper method**:

```csharp
public async Task ReportErrorAsync(
    string errorCode,
    string errorMessage,
    string? stackTrace = null,
    string severity = "medium",
    string? serverIdHash = null
)
```

**Metadata shape**:
```json
{
  "event_type": "error",
  "metadata": {
    "error_code": "BACKUP_FAILED",
    "error_message": "Insufficient disk space",
    "stack_trace": "Error: ENOSPC at ...",
    "severity": "high",
    "server_id_hash": "abc123def456"
  }
}
```

**Integration points**:
- `BackupService` - backup failures
- `ServerManager` - server crashes, start failures
- `ModInstallService` - mod/modpack failures
- Global exception handler - unhandled exceptions

### Update Lifecycle Events

**New methods**:

```csharp
public Task ReportUpdateAvailableAsync(string fromVersion, string toVersion);
public Task ReportUpdateDeclinedAsync(string fromVersion, string toVersion);
```

Event types: `update_available`, `update_declined`

**Note**: These event types need server-side whitelist on mineos.net.

### Feature Usage Tracking

**New service: IFeatureUsageTracker**

```csharp
public interface IFeatureUsageTracker
{
    void Increment(string featureKey, int count = 1);
    Dictionary<string, int> GetAndReset();
}
```

**Implementation**:
- Singleton with `ConcurrentDictionary<string, int>`
- Thread-safe increment
- `GetAndReset()` called by TelemetryReporterService every 24 hours

**Tracked features**:
- `backups_created` - BackupService.CreateBackupAsync()
- `servers_imported` - ServerManager.ImportServerAsync()
- `whitelist_changes` - WhitelistService add/remove
- `scheduled_restarts` - Scheduled task execution
- `console_commands` - ConsoleService.SendCommandAsync()

---

## File Changes Summary

### New Files

```
apps/MineOS.Domain/Entities/LinkedAccount.cs
apps/MineOS.Application/Interfaces/IDeviceAuthService.cs
apps/MineOS.Application/Interfaces/IFeatureUsageTracker.cs
apps/MineOS.Application/DTOs/DeviceAuthDtos.cs
apps/MineOS.Infrastructure/Services/DeviceAuthService.cs
apps/MineOS.Infrastructure/Services/FeatureUsageTracker.cs
apps/MineOS.Api/Endpoints/AccountEndpoints.cs
```

### Modified Files

```
apps/MineOS.Infrastructure/Data/AppDbContext.cs          # Add LinkedAccount DbSet
apps/MineOS.Infrastructure/Services/TelemetryService.cs  # Extended payloads
apps/MineOS.Infrastructure/Background/TelemetryReporterService.cs  # Feature usage
apps/MineOS.Api/Program.cs                               # Register new services
apps/MineOS.Infrastructure/Services/BackupService.cs     # Error reporting
apps/MineOS.Infrastructure/Services/ServerManager.cs     # Error reporting
```

### Database Migration

New table: `linked_accounts`
- Run `dotnet ef migrations add AddLinkedAccounts`

---

## Error Handling

- Network failures to mineos.net: retry with exponential backoff
- Invalid device codes: return clear error status
- Rate limiting: respect `slow_down`, increase poll interval
- Token expiry: clear linked account, require re-linking

---

## Security Considerations

- Access tokens encrypted at rest using Data Protection API
- Device codes never exposed to frontend (only user codes)
- All account endpoints require authentication
- Server names hashed before sending in error events

---

## Testing Strategy

- Unit tests for DeviceAuthService state machine
- Unit tests for telemetry payload builders
- Integration tests for API endpoints
- Mock mineos.net responses for device auth flow
