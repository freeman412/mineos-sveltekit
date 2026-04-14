# Device Authorization & Telemetry Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Implement OAuth 2.0 Device Authorization Grant for linking MineOS installations to mineos.net accounts, and enhance telemetry with java version, disk space, error standardization, server types, backup health, and feature usage tracking.

**Architecture:** The Device Auth flow uses a singleton service managing polling state, with a background task handling the poll loop. Telemetry improvements extend the existing TelemetryService with new payload fields and a FeatureUsageTracker for counting feature usage between reports.

**Tech Stack:** .NET 8, Entity Framework Core, SQLite, ASP.NET Core Minimal APIs

---

## Task 1: Add LinkedAccount Entity

**Files:**
- Create: `apps/MineOS.Domain/Entities/LinkedAccount.cs`
- Modify: `apps/MineOS.Infrastructure/Persistence/AppDbContext.cs`

**Step 1: Create LinkedAccount entity**

Create `apps/MineOS.Domain/Entities/LinkedAccount.cs`:

```csharp
namespace MineOS.Domain.Entities;

public sealed class LinkedAccount
{
    public int Id { get; set; }
    public required string AccessToken { get; set; }
    public required string TokenType { get; set; }
    public required DateTimeOffset ExpiresAt { get; set; }
    public required string UserId { get; set; }
    public required string InstallationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

**Step 2: Add DbSet to AppDbContext**

In `apps/MineOS.Infrastructure/Persistence/AppDbContext.cs`, add after line 56 (after SystemSettings DbSet):

```csharp
// Linked Accounts (mineos.net)
public DbSet<LinkedAccount> LinkedAccounts => Set<LinkedAccount>();
```

**Step 3: Add entity configuration in OnModelCreating**

In `apps/MineOS.Infrastructure/Persistence/AppDbContext.cs`, add before the closing brace of `OnModelCreating`:

```csharp
// Linked Accounts
modelBuilder.Entity<LinkedAccount>(entity =>
{
    entity.HasKey(x => x.Id);
    entity.HasIndex(x => x.InstallationId).IsUnique();
    entity.Property(x => x.AccessToken).IsRequired();
    entity.Property(x => x.TokenType).HasMaxLength(32);
    entity.Property(x => x.UserId).HasMaxLength(64);
    entity.Property(x => x.InstallationId).HasMaxLength(64);

    var timestampConverter = new ValueConverter<DateTimeOffset, long>(
        value => value.ToUnixTimeSeconds(),
        value => DateTimeOffset.FromUnixTimeSeconds(value));
    entity.Property(x => x.ExpiresAt)
        .HasConversion(timestampConverter)
        .HasColumnType("INTEGER");
    entity.Property(x => x.CreatedAt)
        .HasConversion(timestampConverter)
        .HasColumnType("INTEGER");
    entity.Property(x => x.UpdatedAt)
        .HasConversion(timestampConverter)
        .HasColumnType("INTEGER");
});
```

**Step 4: Build to verify compilation**

Run: `dotnet build apps/MineOS.Api/MineOS.Api.csproj --verbosity quiet`
Expected: Build succeeded with 0 errors

**Step 5: Generate EF migration**

Run: `dotnet ef migrations add AddLinkedAccounts --project apps/MineOS.Infrastructure --startup-project apps/MineOS.Api`
Expected: Migration file created

**Step 6: Commit**

```bash
git add apps/MineOS.Domain/Entities/LinkedAccount.cs apps/MineOS.Infrastructure/Persistence/AppDbContext.cs apps/MineOS.Infrastructure/Migrations/
git commit -m "feat: add LinkedAccount entity for mineos.net account linking"
```

---

## Task 2: Add Device Auth DTOs and Interface

**Files:**
- Create: `apps/MineOS.Application/Dtos/DeviceAuthDtos.cs`
- Create: `apps/MineOS.Application/Interfaces/IDeviceAuthService.cs`

**Step 1: Create DTOs**

Create `apps/MineOS.Application/Dtos/DeviceAuthDtos.cs`:

```csharp
namespace MineOS.Application.Dtos;

public record DeviceCodeResponse(
    string UserCode,
    string VerificationUri,
    string VerificationUriComplete,
    int ExpiresIn
);

public record LinkStatus(
    string Status,
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

**Step 2: Create interface**

Create `apps/MineOS.Application/Interfaces/IDeviceAuthService.cs`:

```csharp
using MineOS.Application.Dtos;

namespace MineOS.Application.Interfaces;

public interface IDeviceAuthService
{
    Task<DeviceCodeResponse> InitiateAsync(CancellationToken ct = default);
    Task<LinkStatus> GetStatusAsync(CancellationToken ct = default);
    Task UnlinkAsync(CancellationToken ct = default);
    Task<LinkedAccountInfo?> GetLinkedAccountAsync(CancellationToken ct = default);
}
```

**Step 3: Build to verify compilation**

Run: `dotnet build apps/MineOS.Application/MineOS.Application.csproj --verbosity quiet`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add apps/MineOS.Application/Dtos/DeviceAuthDtos.cs apps/MineOS.Application/Interfaces/IDeviceAuthService.cs
git commit -m "feat: add device auth DTOs and interface"
```

---

## Task 3: Implement DeviceAuthService

**Files:**
- Create: `apps/MineOS.Infrastructure/Services/DeviceAuthService.cs`
- Modify: `apps/MineOS.Api/Program.cs`

**Step 1: Create DeviceAuthService**

Create `apps/MineOS.Infrastructure/Services/DeviceAuthService.cs`:

```csharp
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Domain.Entities;
using MineOS.Infrastructure.Persistence;

namespace MineOS.Infrastructure.Services;

public sealed class DeviceAuthService : IDeviceAuthService, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ILogger<DeviceAuthService> _logger;
    private readonly HttpClient _httpClient;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IDataProtector _protector;
    private readonly string _endpoint;
    private readonly string _installationId;

    // Polling state (singleton)
    private readonly object _lock = new();
    private string? _deviceCode;
    private string? _userCode;
    private string? _verificationUri;
    private string? _verificationUriComplete;
    private DateTimeOffset? _expiresAt;
    private int _pollInterval = 5;
    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;
    private string? _lastError;

    public DeviceAuthService(
        ILogger<DeviceAuthService> logger,
        HttpClient httpClient,
        IDbContextFactory<AppDbContext> dbFactory,
        IDataProtectionProvider dataProtection,
        IConfiguration configuration)
    {
        _logger = logger;
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _dbFactory = dbFactory;
        _protector = dataProtection.CreateProtector("MineOS.LinkedAccount.AccessToken");
        _endpoint = configuration["MINEOS_TELEMETRY_ENDPOINT"] ?? "https://mineos.net";
        _installationId = configuration["MINEOS_INSTALLATION_ID"] ?? string.Empty;
    }

    public async Task<DeviceCodeResponse> InitiateAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_installationId))
            throw new InvalidOperationException("MINEOS_INSTALLATION_ID is not configured");

        // Check if already linked
        var existing = await GetLinkedAccountAsync(ct);
        if (existing != null)
            throw new InvalidOperationException("Already linked to a mineos.net account");

        // Cancel any existing poll
        lock (_lock)
        {
            _pollCts?.Cancel();
            _pollCts?.Dispose();
            _pollCts = null;
            _pollTask = null;
            _lastError = null;
        }

        // Request device code
        var payload = new { installation_id = _installationId };
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogInformation("Requesting device code from {Endpoint}", _endpoint);
        var response = await _httpClient.PostAsync($"{_endpoint}/api/auth/device", content, ct);
        var responseJson = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Device code request failed: {Status} {Body}", (int)response.StatusCode, responseJson);
            throw new HttpRequestException($"Device code request failed: {response.StatusCode}");
        }

        var deviceResponse = JsonSerializer.Deserialize<DeviceCodeApiResponse>(responseJson, JsonOptions)
            ?? throw new InvalidOperationException("Invalid response from device auth endpoint");

        lock (_lock)
        {
            _deviceCode = deviceResponse.DeviceCode;
            _userCode = deviceResponse.UserCode;
            _verificationUri = deviceResponse.VerificationUri;
            _verificationUriComplete = deviceResponse.VerificationUriComplete;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(deviceResponse.ExpiresIn);
            _pollInterval = deviceResponse.Interval;

            // Start background polling
            _pollCts = new CancellationTokenSource();
            _pollTask = PollForCompletionAsync(_pollCts.Token);
        }

        return new DeviceCodeResponse(
            deviceResponse.UserCode,
            deviceResponse.VerificationUri,
            deviceResponse.VerificationUriComplete,
            deviceResponse.ExpiresIn
        );
    }

    public async Task<LinkStatus> GetStatusAsync(CancellationToken ct = default)
    {
        // Check if linked
        var linked = await GetLinkedAccountAsync(ct);
        if (linked != null)
        {
            return new LinkStatus(
                Status: "linked",
                UserCode: null,
                VerificationUri: null,
                VerificationUriComplete: null,
                ExpiresIn: null,
                LinkedAccount: linked,
                Error: null
            );
        }

        lock (_lock)
        {
            if (_deviceCode == null)
            {
                return new LinkStatus(
                    Status: "not_started",
                    UserCode: null,
                    VerificationUri: null,
                    VerificationUriComplete: null,
                    ExpiresIn: null,
                    LinkedAccount: null,
                    Error: _lastError
                );
            }

            if (_expiresAt.HasValue && DateTimeOffset.UtcNow >= _expiresAt.Value)
            {
                return new LinkStatus(
                    Status: "expired",
                    UserCode: null,
                    VerificationUri: null,
                    VerificationUriComplete: null,
                    ExpiresIn: null,
                    LinkedAccount: null,
                    Error: "Device code expired"
                );
            }

            var expiresIn = _expiresAt.HasValue
                ? (int)(_expiresAt.Value - DateTimeOffset.UtcNow).TotalSeconds
                : 0;

            return new LinkStatus(
                Status: "pending",
                UserCode: _userCode,
                VerificationUri: _verificationUri,
                VerificationUriComplete: _verificationUriComplete,
                ExpiresIn: expiresIn > 0 ? expiresIn : null,
                LinkedAccount: null,
                Error: _lastError
            );
        }
    }

    public async Task UnlinkAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            _pollCts?.Cancel();
            _pollCts?.Dispose();
            _pollCts = null;
            _pollTask = null;
            _deviceCode = null;
            _userCode = null;
            _lastError = null;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var account = await db.LinkedAccounts
            .FirstOrDefaultAsync(a => a.InstallationId == _installationId, ct);

        if (account != null)
        {
            db.LinkedAccounts.Remove(account);
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Unlinked mineos.net account");
        }
    }

    public async Task<LinkedAccountInfo?> GetLinkedAccountAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var account = await db.LinkedAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.InstallationId == _installationId, ct);

        if (account == null)
            return null;

        return new LinkedAccountInfo(
            UserId: account.UserId,
            LinkedAt: account.CreatedAt,
            ExpiresAt: account.ExpiresAt
        );
    }

    private async Task PollForCompletionAsync(CancellationToken ct)
    {
        _logger.LogInformation("Starting device auth polling");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_pollInterval), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            string? deviceCode;
            lock (_lock)
            {
                deviceCode = _deviceCode;
                if (deviceCode == null || (_expiresAt.HasValue && DateTimeOffset.UtcNow >= _expiresAt.Value))
                {
                    _logger.LogInformation("Device code expired, stopping poll");
                    break;
                }
            }

            try
            {
                var payload = new { device_code = deviceCode };
                var json = JsonSerializer.Serialize(payload, JsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_endpoint}/api/auth/device/poll", content, ct);
                var responseJson = await response.Content.ReadAsStringAsync(ct);

                if (response.IsSuccessStatusCode)
                {
                    var tokenResponse = JsonSerializer.Deserialize<TokenApiResponse>(responseJson, JsonOptions);
                    if (tokenResponse != null && !string.IsNullOrEmpty(tokenResponse.AccessToken))
                    {
                        await SaveLinkedAccountAsync(tokenResponse, ct);
                        lock (_lock)
                        {
                            _deviceCode = null;
                            _userCode = null;
                        }
                        _logger.LogInformation("Device authorization completed successfully");
                        return;
                    }
                }
                else
                {
                    var errorResponse = JsonSerializer.Deserialize<ErrorApiResponse>(responseJson, JsonOptions);
                    var error = errorResponse?.Error ?? "unknown_error";

                    if (error == "slow_down")
                    {
                        lock (_lock)
                        {
                            _pollInterval += 5;
                        }
                        _logger.LogInformation("Received slow_down, increasing interval to {Interval}s", _pollInterval);
                    }
                    else if (error == "expired_token")
                    {
                        lock (_lock)
                        {
                            _lastError = "Device code expired";
                            _deviceCode = null;
                        }
                        _logger.LogInformation("Device code expired");
                        return;
                    }
                    else if (error != "authorization_pending")
                    {
                        lock (_lock)
                        {
                            _lastError = errorResponse?.ErrorDescription ?? error;
                        }
                        _logger.LogWarning("Device auth poll error: {Error}", error);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Device auth poll request failed");
            }
        }
    }

    private async Task SaveLinkedAccountAsync(TokenApiResponse token, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var existing = await db.LinkedAccounts
            .FirstOrDefaultAsync(a => a.InstallationId == _installationId, ct);

        var encryptedToken = _protector.Protect(token.AccessToken);
        var now = DateTimeOffset.UtcNow;

        if (existing != null)
        {
            existing.AccessToken = encryptedToken;
            existing.TokenType = token.TokenType;
            existing.ExpiresAt = now.AddSeconds(token.ExpiresIn);
            existing.UserId = token.UserId;
            existing.UpdatedAt = now;
        }
        else
        {
            db.LinkedAccounts.Add(new LinkedAccount
            {
                AccessToken = encryptedToken,
                TokenType = token.TokenType,
                ExpiresAt = now.AddSeconds(token.ExpiresIn),
                UserId = token.UserId,
                InstallationId = _installationId,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        await db.SaveChangesAsync(ct);
    }

    public void Dispose()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
    }

    // API response DTOs
    private record DeviceCodeApiResponse(
        [property: JsonPropertyName("device_code")] string DeviceCode,
        [property: JsonPropertyName("user_code")] string UserCode,
        [property: JsonPropertyName("verification_uri")] string VerificationUri,
        [property: JsonPropertyName("verification_uri_complete")] string VerificationUriComplete,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("interval")] int Interval
    );

    private record TokenApiResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("token_type")] string TokenType,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("installation_id")] string InstallationId,
        [property: JsonPropertyName("user_id")] string UserId
    );

    private record ErrorApiResponse(
        [property: JsonPropertyName("error")] string Error,
        [property: JsonPropertyName("error_description")] string? ErrorDescription
    );
}
```

**Step 2: Register service in Program.cs**

In `apps/MineOS.Api/Program.cs`, add after line 199 (after TelemetryService registration):

```csharp
builder.Services.AddDataProtection();
builder.Services.AddSingleton<IDeviceAuthService, DeviceAuthService>();
builder.Services.AddHttpClient<IDeviceAuthService, DeviceAuthService>();
```

Note: AddHttpClient will override the singleton, so use this pattern instead:

```csharp
builder.Services.AddDataProtection();
builder.Services.AddHttpClient<DeviceAuthService>();
builder.Services.AddSingleton<IDeviceAuthService>(sp =>
    sp.GetRequiredService<DeviceAuthService>());
```

**Step 3: Build to verify compilation**

Run: `dotnet build apps/MineOS.Api/MineOS.Api.csproj --verbosity quiet`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add apps/MineOS.Infrastructure/Services/DeviceAuthService.cs apps/MineOS.Api/Program.cs
git commit -m "feat: implement DeviceAuthService with background polling"
```

---

## Task 4: Add Account Endpoints

**Files:**
- Create: `apps/MineOS.Api/Endpoints/AccountEndpoints.cs`
- Modify: `apps/MineOS.Api/Endpoints/ApiEndpoints.cs`

**Step 1: Create AccountEndpoints**

Create `apps/MineOS.Api/Endpoints/AccountEndpoints.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using MineOS.Application.Interfaces;

namespace MineOS.Api.Endpoints;

public static class AccountEndpoints
{
    public static RouteGroupBuilder MapAccountEndpoints(this RouteGroupBuilder api)
    {
        var account = api.MapGroup("/account");

        account.MapGet("/", async (IDeviceAuthService authService, CancellationToken ct) =>
        {
            var linked = await authService.GetLinkedAccountAsync(ct);
            return linked != null
                ? Results.Ok(new { linked = true, userId = linked.UserId, linkedAt = linked.LinkedAt, expiresAt = linked.ExpiresAt })
                : Results.Ok(new { linked = false });
        })
        .RequireAuthorization();

        account.MapGet("/link", async (IDeviceAuthService authService, CancellationToken ct) =>
        {
            try
            {
                var response = await authService.InitiateAsync(ct);
                return Results.Ok(response);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (HttpRequestException ex)
            {
                return Results.StatusCode(502, new { error = "Failed to contact mineos.net", details = ex.Message });
            }
        })
        .RequireAuthorization(new AuthorizeAttribute { Roles = "admin" });

        account.MapGet("/link/status", async (IDeviceAuthService authService, CancellationToken ct) =>
        {
            var status = await authService.GetStatusAsync(ct);
            return Results.Ok(status);
        })
        .RequireAuthorization();

        account.MapDelete("/link", async (IDeviceAuthService authService, CancellationToken ct) =>
        {
            await authService.UnlinkAsync(ct);
            return Results.Ok(new { message = "Account unlinked" });
        })
        .RequireAuthorization(new AuthorizeAttribute { Roles = "admin" });

        return api;
    }
}
```

**Step 2: Register endpoints in ApiEndpoints.cs**

In `apps/MineOS.Api/Endpoints/ApiEndpoints.cs`, find where endpoints are mapped and add:

```csharp
api.MapAccountEndpoints();
```

**Step 3: Build to verify compilation**

Run: `dotnet build apps/MineOS.Api/MineOS.Api.csproj --verbosity quiet`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add apps/MineOS.Api/Endpoints/AccountEndpoints.cs apps/MineOS.Api/Endpoints/ApiEndpoints.cs
git commit -m "feat: add account linking API endpoints"
```

---

## Task 5: Add FeatureUsageTracker Service

**Files:**
- Create: `apps/MineOS.Application/Interfaces/IFeatureUsageTracker.cs`
- Create: `apps/MineOS.Infrastructure/Services/FeatureUsageTracker.cs`
- Modify: `apps/MineOS.Api/Program.cs`

**Step 1: Create interface**

Create `apps/MineOS.Application/Interfaces/IFeatureUsageTracker.cs`:

```csharp
namespace MineOS.Application.Interfaces;

public interface IFeatureUsageTracker
{
    void Increment(string featureKey, int count = 1);
    Dictionary<string, int> GetAndReset();
}
```

**Step 2: Create implementation**

Create `apps/MineOS.Infrastructure/Services/FeatureUsageTracker.cs`:

```csharp
using System.Collections.Concurrent;
using MineOS.Application.Interfaces;

namespace MineOS.Infrastructure.Services;

public sealed class FeatureUsageTracker : IFeatureUsageTracker
{
    private readonly ConcurrentDictionary<string, int> _counts = new();

    public void Increment(string featureKey, int count = 1)
    {
        _counts.AddOrUpdate(featureKey, count, (_, existing) => existing + count);
    }

    public Dictionary<string, int> GetAndReset()
    {
        var result = new Dictionary<string, int>();

        foreach (var key in _counts.Keys.ToList())
        {
            if (_counts.TryRemove(key, out var count) && count > 0)
            {
                result[key] = count;
            }
        }

        return result;
    }
}
```

**Step 3: Register service in Program.cs**

In `apps/MineOS.Api/Program.cs`, add after DeviceAuthService registration:

```csharp
builder.Services.AddSingleton<IFeatureUsageTracker, FeatureUsageTracker>();
```

**Step 4: Build to verify compilation**

Run: `dotnet build apps/MineOS.Api/MineOS.Api.csproj --verbosity quiet`
Expected: Build succeeded

**Step 5: Commit**

```bash
git add apps/MineOS.Application/Interfaces/IFeatureUsageTracker.cs apps/MineOS.Infrastructure/Services/FeatureUsageTracker.cs apps/MineOS.Api/Program.cs
git commit -m "feat: add FeatureUsageTracker service"
```

---

## Task 6: Extend UsageData with New Fields

**Files:**
- Modify: `apps/MineOS.Application/Interfaces/ITelemetryService.cs`

**Step 1: Update UsageData record**

Replace the `UsageData` record at the end of `apps/MineOS.Application/Interfaces/ITelemetryService.cs`:

```csharp
public record UsageData(
    int ServerCount,
    int ActiveServerCount,
    int TotalUserCount,
    int ActiveUserCount,
    long UptimeSeconds,
    // New fields
    string[]? ServerTypes = null,
    int? BackupCount = null,
    bool? LastBackupSuccess = null,
    int? BackupTotalSizeMb = null,
    Dictionary<string, int>? FeatureUsage = null);
```

**Step 2: Build to verify compilation**

Run: `dotnet build apps/MineOS.Application/MineOS.Application.csproj --verbosity quiet`
Expected: Build succeeded (may have warnings about optional parameters)

**Step 3: Commit**

```bash
git add apps/MineOS.Application/Interfaces/ITelemetryService.cs
git commit -m "feat: extend UsageData with server types, backup health, feature usage"
```

---

## Task 7: Add System Info Helpers to TelemetryService

**Files:**
- Modify: `apps/MineOS.Infrastructure/Services/TelemetryService.cs`

**Step 1: Add helper methods for java version, disk space, container info**

In `apps/MineOS.Infrastructure/Services/TelemetryService.cs`, add these private methods before the payload classes:

```csharp
private static string? _cachedJavaVersion;
private static readonly object JavaVersionLock = new();

private string? GetJavaVersion()
{
    lock (JavaVersionLock)
    {
        if (_cachedJavaVersion != null)
            return _cachedJavaVersion;

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "java",
                Arguments = "-version",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null) return null;

            // java -version outputs to stderr
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(5000);

            // Parse version from output like: openjdk version "17.0.2" 2022-01-18
            var match = System.Text.RegularExpressions.Regex.Match(
                stderr, @"(?:openjdk|java) version ""([^""]+)""");

            if (match.Success)
            {
                _cachedJavaVersion = match.Groups[1].Value;
                if (_cachedJavaVersion.Length > 50)
                    _cachedJavaVersion = _cachedJavaVersion[..50];
                return _cachedJavaVersion;
            }

            // Try alternate format: openjdk 17.0.2 2022-01-18
            match = System.Text.RegularExpressions.Regex.Match(
                stderr, @"(?:openjdk|java) (\d+[\d._-]*)");

            if (match.Success)
            {
                _cachedJavaVersion = match.Groups[1].Value;
                if (_cachedJavaVersion.Length > 50)
                    _cachedJavaVersion = _cachedJavaVersion[..50];
                return _cachedJavaVersion;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get Java version");
        }

        return null;
    }
}

private (int? totalGb, int? availableGb) GetDiskSpace(string? baseDirectory)
{
    try
    {
        var path = baseDirectory ?? "/var/games/minecraft";
        if (!Directory.Exists(path))
            path = "/";

        var driveInfo = new DriveInfo(Path.GetPathRoot(path) ?? "/");
        var totalGb = (int)(driveInfo.TotalSize / (1024 * 1024 * 1024));
        var availableGb = (int)(driveInfo.AvailableFreeSpace / (1024 * 1024 * 1024));
        return (totalGb, availableGb);
    }
    catch (Exception ex)
    {
        _logger.LogDebug(ex, "Failed to get disk space");
        return (null, null);
    }
}

private (string? engine, string? version) GetContainerInfo()
{
    try
    {
        // Check for Docker
        if (File.Exists("/.dockerenv"))
        {
            var version = GetContainerVersion("docker");
            return ("docker", version);
        }

        // Check for Podman
        if (File.Exists("/run/.containerenv"))
        {
            var version = GetContainerVersion("podman");
            return ("podman", version);
        }

        // Check cgroups for container detection
        if (File.Exists("/proc/1/cgroup"))
        {
            var cgroup = File.ReadAllText("/proc/1/cgroup");
            if (cgroup.Contains("docker"))
                return ("docker", GetContainerVersion("docker"));
            if (cgroup.Contains("lxc"))
                return ("lxc", null);
        }
    }
    catch (Exception ex)
    {
        _logger.LogDebug(ex, "Failed to detect container engine");
    }

    return (null, null);
}

private string? GetContainerVersion(string engine)
{
    try
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = engine,
            Arguments = "--version",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(psi);
        if (process == null) return null;

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(5000);

        // Parse version from "Docker version 24.0.7, build afdd53b"
        var match = System.Text.RegularExpressions.Regex.Match(
            output, @"version (\d+\.\d+\.\d+)");

        return match.Success ? match.Groups[1].Value : null;
    }
    catch
    {
        return null;
    }
}
```

**Step 2: Build to verify compilation**

Run: `dotnet build apps/MineOS.Infrastructure/MineOS.Infrastructure.csproj --verbosity quiet`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add apps/MineOS.Infrastructure/Services/TelemetryService.cs
git commit -m "feat: add system info helpers for java, disk, container"
```

---

## Task 8: Update Install Payload with New Fields

**Files:**
- Modify: `apps/MineOS.Infrastructure/Services/TelemetryService.cs`

**Step 1: Update InstallPayload class**

In `apps/MineOS.Infrastructure/Services/TelemetryService.cs`, update the `InstallPayload` class:

```csharp
private class InstallPayload
{
    [JsonPropertyName("installation_id")]
    public string InstallationId { get; set; } = string.Empty;

    [JsonPropertyName("os")]
    public string Os { get; set; } = string.Empty;

    [JsonPropertyName("architecture")]
    public string Architecture { get; set; } = string.Empty;

    [JsonPropertyName("mineos_version")]
    public string MineOSVersion { get; set; } = string.Empty;

    [JsonPropertyName("install_method")]
    public string InstallMethod { get; set; } = string.Empty;

    [JsonPropertyName("install_success")]
    public bool InstallSuccess { get; set; }

    [JsonPropertyName("is_docker")]
    public bool IsDocker { get; set; }

    [JsonPropertyName("user_agent")]
    public string UserAgent { get; set; } = string.Empty;

    // New fields
    [JsonPropertyName("java_version")]
    public string? JavaVersion { get; set; }

    [JsonPropertyName("disk_total_gb")]
    public int? DiskTotalGb { get; set; }

    [JsonPropertyName("disk_available_gb")]
    public int? DiskAvailableGb { get; set; }

    [JsonPropertyName("container_engine")]
    public string? ContainerEngine { get; set; }

    [JsonPropertyName("container_version")]
    public string? ContainerVersion { get; set; }
}
```

**Step 2: Update RegisterAndGetKeyAsync to populate new fields**

In `apps/MineOS.Infrastructure/Services/TelemetryService.cs`, update the `RegisterAndGetKeyAsync` method. Replace the `registerPayload` initialization:

```csharp
var (diskTotal, diskAvailable) = GetDiskSpace(null);
var (containerEngine, containerVersion) = GetContainerInfo();

var registerPayload = new InstallPayload
{
    InstallationId = _installationId,
    Os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows"
       : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "darwin" : "linux",
    Architecture = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
    MineOSVersion = _version,
    InstallMethod = "docker",
    InstallSuccess = true,
    IsDocker = containerEngine == "docker",
    UserAgent = $"MineOS-API/{_version}",
    // New fields
    JavaVersion = GetJavaVersion(),
    DiskTotalGb = diskTotal,
    DiskAvailableGb = diskAvailable,
    ContainerEngine = containerEngine,
    ContainerVersion = containerVersion
};
```

**Step 3: Build to verify compilation**

Run: `dotnet build apps/MineOS.Infrastructure/MineOS.Infrastructure.csproj --verbosity quiet`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add apps/MineOS.Infrastructure/Services/TelemetryService.cs
git commit -m "feat: add java version, disk space, container info to install telemetry"
```

---

## Task 9: Update Usage Payload with New Fields

**Files:**
- Modify: `apps/MineOS.Infrastructure/Services/TelemetryService.cs`

**Step 1: Update UsagePayload class**

In `apps/MineOS.Infrastructure/Services/TelemetryService.cs`, update the `UsagePayload` class:

```csharp
private class UsagePayload
{
    [JsonPropertyName("installation_id")]
    public string InstallationId { get; set; } = string.Empty;

    [JsonPropertyName("server_count")]
    public int? ServerCount { get; set; }

    [JsonPropertyName("active_server_count")]
    public int? ActiveServerCount { get; set; }

    [JsonPropertyName("total_user_count")]
    public int? TotalUserCount { get; set; }

    [JsonPropertyName("active_user_count")]
    public int? ActiveUserCount { get; set; }

    [JsonPropertyName("uptime_seconds")]
    public long UptimeSeconds { get; set; }

    [JsonPropertyName("mineos_version")]
    public string MineOSVersion { get; set; } = string.Empty;

    // New fields
    [JsonPropertyName("server_types")]
    public string[]? ServerTypes { get; set; }

    [JsonPropertyName("backup_count")]
    public int? BackupCount { get; set; }

    [JsonPropertyName("last_backup_success")]
    public bool? LastBackupSuccess { get; set; }

    [JsonPropertyName("backup_total_size_mb")]
    public int? BackupTotalSizeMb { get; set; }

    [JsonPropertyName("feature_usage")]
    public Dictionary<string, int>? FeatureUsage { get; set; }
}
```

**Step 2: Update ReportUsageAsync to include new fields**

In `apps/MineOS.Infrastructure/Services/TelemetryService.cs`, update the `ReportUsageAsync` method:

```csharp
public async Task ReportUsageAsync(UsageData data, CancellationToken cancellationToken = default)
{
    if (!_enabled)
    {
        _logger.LogInformation("Usage telemetry disabled, skipping report");
        return;
    }

    if (string.IsNullOrEmpty(_installationId))
    {
        _logger.LogWarning("Usage telemetry skipped: MINEOS_INSTALLATION_ID is not set");
        return;
    }

    try
    {
        var payload = new UsagePayload
        {
            InstallationId = _installationId,
            MineOSVersion = _version,
            ServerCount = data.ServerCount,
            ActiveServerCount = data.ActiveServerCount,
            TotalUserCount = data.TotalUserCount,
            ActiveUserCount = data.ActiveUserCount,
            UptimeSeconds = data.UptimeSeconds,
            // New fields
            ServerTypes = data.ServerTypes,
            BackupCount = data.BackupCount,
            LastBackupSuccess = data.LastBackupSuccess,
            BackupTotalSizeMb = data.BackupTotalSizeMb,
            FeatureUsage = data.FeatureUsage?.Count > 0 ? data.FeatureUsage : null
        };

        await SendWithAuthAsync($"{_endpoint}/api/telemetry/usage", payload, cancellationToken);
        _logger.LogInformation("Usage telemetry reported successfully");
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Failed to send usage telemetry");
    }
}
```

**Step 3: Build to verify compilation**

Run: `dotnet build apps/MineOS.Infrastructure/MineOS.Infrastructure.csproj --verbosity quiet`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add apps/MineOS.Infrastructure/Services/TelemetryService.cs
git commit -m "feat: add server types, backup health, feature usage to usage telemetry"
```

---

## Task 10: Add Standardized Error Reporting

**Files:**
- Modify: `apps/MineOS.Application/Interfaces/ITelemetryService.cs`
- Modify: `apps/MineOS.Infrastructure/Services/TelemetryService.cs`

**Step 1: Add ReportErrorAsync to interface**

In `apps/MineOS.Application/Interfaces/ITelemetryService.cs`, add to the interface:

```csharp
/// <summary>
/// Reports a standardized error event with structured metadata.
/// </summary>
Task ReportErrorAsync(
    string errorCode,
    string errorMessage,
    string? stackTrace = null,
    string severity = "medium",
    string? serverName = null,
    CancellationToken cancellationToken = default);

/// <summary>
/// Reports an update lifecycle event.
/// </summary>
Task ReportUpdateEventAsync(
    string eventType,
    string fromVersion,
    string toVersion,
    CancellationToken cancellationToken = default);
```

**Step 2: Implement in TelemetryService**

In `apps/MineOS.Infrastructure/Services/TelemetryService.cs`, add after `ReportLifecycleEventAsync`:

```csharp
public async Task ReportErrorAsync(
    string errorCode,
    string errorMessage,
    string? stackTrace = null,
    string severity = "medium",
    string? serverName = null,
    CancellationToken cancellationToken = default)
{
    if (!_enabled || string.IsNullOrEmpty(_installationId))
        return;

    // Validate severity
    var validSeverities = new[] { "low", "medium", "high", "critical" };
    if (!validSeverities.Contains(severity.ToLowerInvariant()))
        severity = "medium";

    // Hash server name for privacy
    string? serverIdHash = null;
    if (!string.IsNullOrEmpty(serverName))
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(serverName));
        serverIdHash = Convert.ToHexString(hashBytes)[..12].ToLowerInvariant();
    }

    var metadata = new Dictionary<string, object?>
    {
        ["error_code"] = errorCode,
        ["error_message"] = errorMessage.Length > 500 ? errorMessage[..500] : errorMessage,
        ["severity"] = severity.ToLowerInvariant()
    };

    if (!string.IsNullOrEmpty(stackTrace))
        metadata["stack_trace"] = stackTrace.Length > 500 ? stackTrace[..500] : stackTrace;

    if (!string.IsNullOrEmpty(serverIdHash))
        metadata["server_id_hash"] = serverIdHash;

    await ReportLifecycleEventAsync("error", metadata, cancellationToken);
}

public async Task ReportUpdateEventAsync(
    string eventType,
    string fromVersion,
    string toVersion,
    CancellationToken cancellationToken = default)
{
    if (!_enabled || string.IsNullOrEmpty(_installationId))
        return;

    // Only allow specific update event types
    var validTypes = new[] { "update_available", "update_declined" };
    if (!validTypes.Contains(eventType))
    {
        _logger.LogWarning("Invalid update event type: {EventType}", eventType);
        return;
    }

    var metadata = new Dictionary<string, string>
    {
        ["from_version"] = fromVersion,
        ["to_version"] = toVersion
    };

    await ReportLifecycleEventAsync(eventType, metadata, cancellationToken);
}
```

**Step 3: Build to verify compilation**

Run: `dotnet build apps/MineOS.Infrastructure/MineOS.Infrastructure.csproj --verbosity quiet`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add apps/MineOS.Application/Interfaces/ITelemetryService.cs apps/MineOS.Infrastructure/Services/TelemetryService.cs
git commit -m "feat: add standardized error reporting and update lifecycle events"
```

---

## Task 11: Update TelemetryReporterService to Gather New Data

**Files:**
- Modify: `apps/MineOS.Infrastructure/Background/TelemetryReporterService.cs`

**Step 1: Update ReportTelemetryAsync**

Replace the `ReportTelemetryAsync` method in `apps/MineOS.Infrastructure/Background/TelemetryReporterService.cs`:

```csharp
private async Task ReportTelemetryAsync(CancellationToken cancellationToken)
{
    using var scope = _scopeFactory.CreateScope();

    var settingsService = scope.ServiceProvider.GetRequiredService<ISettingsService>();
    var enabled = await settingsService.GetAsync(
        Services.SettingsService.Keys.TelemetryEnabled, cancellationToken);

    if (string.Equals(enabled, "false", StringComparison.OrdinalIgnoreCase))
    {
        _logger.LogInformation("Telemetry disabled via settings, skipping report");
        return;
    }

    _logger.LogInformation("Gathering usage data for telemetry report...");

    var serverService = scope.ServiceProvider.GetRequiredService<IServerService>();
    var userService = scope.ServiceProvider.GetRequiredService<IUserService>();
    var backupService = scope.ServiceProvider.GetRequiredService<IBackupService>();
    var featureTracker = scope.ServiceProvider.GetRequiredService<IFeatureUsageTracker>();

    var servers = await serverService.ListServersAsync(cancellationToken);
    var users = await userService.ListUsersAsync(cancellationToken);

    var activeServerCount = 0;
    var serverTypes = new List<string>();

    foreach (var server in servers)
    {
        try
        {
            var status = await serverService.GetServerStatusAsync(server.Name, cancellationToken);
            if (status.Status == "up")
                activeServerCount++;

            // Extract server type (e.g., "paper", "vanilla", "forge")
            if (!string.IsNullOrEmpty(server.ServerType))
                serverTypes.Add(server.ServerType.ToLowerInvariant());
        }
        catch
        {
            // Ignore errors for individual servers
        }
    }

    // Gather backup health
    int? backupCount = null;
    bool? lastBackupSuccess = null;
    int? backupTotalSizeMb = null;

    try
    {
        var allBackups = new List<(bool success, long sizeBytes)>();
        foreach (var server in servers)
        {
            try
            {
                var backups = await backupService.ListBackupsAsync(server.Name, cancellationToken);
                foreach (var backup in backups)
                {
                    allBackups.Add((true, backup.SizeBytes));
                }
            }
            catch
            {
                // Ignore per-server backup errors
            }
        }

        if (allBackups.Count > 0)
        {
            backupCount = allBackups.Count;
            lastBackupSuccess = true; // If we can list them, they exist
            backupTotalSizeMb = (int)(allBackups.Sum(b => b.sizeBytes) / (1024 * 1024));
        }
    }
    catch (Exception ex)
    {
        _logger.LogDebug(ex, "Failed to gather backup health");
    }

    var uptimeSeconds = (long)Stopwatch.GetElapsedTime(StartTimestamp).TotalSeconds;
    var featureUsage = featureTracker.GetAndReset();

    var data = new UsageData(
        ServerCount: servers.Count,
        ActiveServerCount: activeServerCount,
        TotalUserCount: users.Count,
        ActiveUserCount: users.Count,
        UptimeSeconds: uptimeSeconds,
        ServerTypes: serverTypes.Count > 0 ? serverTypes.ToArray() : null,
        BackupCount: backupCount,
        LastBackupSuccess: lastBackupSuccess,
        BackupTotalSizeMb: backupTotalSizeMb,
        FeatureUsage: featureUsage.Count > 0 ? featureUsage : null);

    _logger.LogInformation("Sending usage telemetry report...");
    var telemetryService = scope.ServiceProvider.GetRequiredService<ITelemetryService>();
    await telemetryService.ReportUsageAsync(data, cancellationToken);
}
```

**Step 2: Add missing using statements**

At the top of the file, ensure these usings exist:

```csharp
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MineOS.Application.Interfaces;
```

**Step 3: Build to verify compilation**

Run: `dotnet build apps/MineOS.Infrastructure/MineOS.Infrastructure.csproj --verbosity quiet`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add apps/MineOS.Infrastructure/Background/TelemetryReporterService.cs
git commit -m "feat: gather server types, backup health, feature usage in telemetry reporter"
```

---

## Task 12: Integrate Feature Usage Tracking

**Files:**
- Modify: `apps/MineOS.Infrastructure/Services/BackupService.cs`
- Modify: `apps/MineOS.Infrastructure/Services/ConsoleService.cs`

**Step 1: Add feature tracking to BackupService**

Find `apps/MineOS.Infrastructure/Services/BackupService.cs`. In the `CreateBackupAsync` method (or equivalent), after a successful backup creation, add:

```csharp
_featureTracker.Increment("backups_created");
```

Add the dependency:

```csharp
private readonly IFeatureUsageTracker _featureTracker;
```

Add to constructor:

```csharp
IFeatureUsageTracker featureTracker
```

And assign:

```csharp
_featureTracker = featureTracker;
```

**Step 2: Add feature tracking to ConsoleService**

Find `apps/MineOS.Infrastructure/Services/ConsoleService.cs`. In the method that sends commands to servers, add:

```csharp
_featureTracker.Increment("console_commands");
```

Add the same dependency pattern as BackupService.

**Step 3: Build to verify compilation**

Run: `dotnet build apps/MineOS.Infrastructure/MineOS.Infrastructure.csproj --verbosity quiet`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add apps/MineOS.Infrastructure/Services/BackupService.cs apps/MineOS.Infrastructure/Services/ConsoleService.cs
git commit -m "feat: track backup creation and console commands for feature usage"
```

---

## Task 13: Add Error Reporting Integration

**Files:**
- Modify: `apps/MineOS.Infrastructure/Services/BackupService.cs`

**Step 1: Add error reporting to BackupService**

In the `BackupService`, find where backup failures are caught. Add error reporting:

```csharp
await _telemetryService.ReportErrorAsync(
    "BACKUP_FAILED",
    ex.Message,
    ex.StackTrace,
    "high",
    serverName,
    cancellationToken);
```

Add ITelemetryService as a dependency if not already present.

**Step 2: Build to verify compilation**

Run: `dotnet build apps/MineOS.Infrastructure/MineOS.Infrastructure.csproj --verbosity quiet`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add apps/MineOS.Infrastructure/Services/BackupService.cs
git commit -m "feat: report backup failures to telemetry"
```

---

## Task 14: Run All Tests

**Step 1: Run test suite**

Run: `dotnet test --verbosity normal`
Expected: All tests pass

**Step 2: If tests fail, fix issues and re-run**

---

## Task 15: Final Build and Manual Verification

**Step 1: Clean build**

Run: `dotnet build --no-incremental`
Expected: Build succeeded

**Step 2: Verify migrations can be applied**

Run: `dotnet ef database update --project apps/MineOS.Infrastructure --startup-project apps/MineOS.Api -- --connection "Data Source=:memory:"`
(Or use a test database)

**Step 3: Commit any remaining changes**

```bash
git status
# Add any remaining files
git add .
git commit -m "chore: final cleanup"
```

---

## Summary of Changes

### New Files Created
- `apps/MineOS.Domain/Entities/LinkedAccount.cs`
- `apps/MineOS.Application/Dtos/DeviceAuthDtos.cs`
- `apps/MineOS.Application/Interfaces/IDeviceAuthService.cs`
- `apps/MineOS.Application/Interfaces/IFeatureUsageTracker.cs`
- `apps/MineOS.Infrastructure/Services/DeviceAuthService.cs`
- `apps/MineOS.Infrastructure/Services/FeatureUsageTracker.cs`
- `apps/MineOS.Api/Endpoints/AccountEndpoints.cs`

### Files Modified
- `apps/MineOS.Infrastructure/Persistence/AppDbContext.cs`
- `apps/MineOS.Api/Program.cs`
- `apps/MineOS.Api/Endpoints/ApiEndpoints.cs`
- `apps/MineOS.Application/Interfaces/ITelemetryService.cs`
- `apps/MineOS.Infrastructure/Services/TelemetryService.cs`
- `apps/MineOS.Infrastructure/Background/TelemetryReporterService.cs`
- `apps/MineOS.Infrastructure/Services/BackupService.cs`
- `apps/MineOS.Infrastructure/Services/ConsoleService.cs`

### Database Changes
- New migration: `AddLinkedAccounts`
- New table: `linked_accounts`
