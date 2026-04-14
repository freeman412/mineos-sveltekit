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

        var existing = await GetLinkedAccountAsync(ct);
        if (existing != null)
            throw new InvalidOperationException("Already linked to a mineos.net account");

        lock (_lock)
        {
            _pollCts?.Cancel();
            _pollCts?.Dispose();
            _pollCts = null;
            _pollTask = null;
            _lastError = null;
        }

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
