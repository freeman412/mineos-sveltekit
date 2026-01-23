using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Application.Options;
using MineOS.Infrastructure.Utilities;

namespace MineOS.Infrastructure.Services;

public sealed class PluginService : IPluginService
{
    private const string RestartFlagFile = ".mineos-restart-required";
    private readonly ILogger<PluginService> _logger;
    private readonly HostOptions _hostOptions;

    public PluginService(ILogger<PluginService> logger, IOptions<HostOptions> hostOptions)
    {
        _logger = logger;
        _hostOptions = hostOptions.Value;
    }

    public Task<IReadOnlyList<InstalledPluginDto>> ListPluginsAsync(string serverName, CancellationToken cancellationToken)
    {
        var serverPath = GetServerPath(serverName);
        if (!Directory.Exists(serverPath))
        {
            throw new DirectoryNotFoundException($"Server '{serverName}' not found");
        }

        var pluginsPath = GetPluginsPath(serverName);
        if (!Directory.Exists(pluginsPath))
        {
            return Task.FromResult<IReadOnlyList<InstalledPluginDto>>(Array.Empty<InstalledPluginDto>());
        }

        var plugins = Directory.GetFiles(pluginsPath)
            .Select(path =>
            {
                var info = new FileInfo(path);
                var fileName = info.Name;
                return new InstalledPluginDto(
                    fileName,
                    info.Length,
                    info.LastWriteTimeUtc,
                    fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase));
            })
            .OrderBy(plugin => plugin.FileName)
            .ToList();

        return Task.FromResult<IReadOnlyList<InstalledPluginDto>>(plugins);
    }

    public async Task SavePluginAsync(string serverName, string fileName, Stream content, CancellationToken cancellationToken)
    {
        var serverPath = GetServerPath(serverName);
        if (!Directory.Exists(serverPath))
        {
            throw new DirectoryNotFoundException($"Server '{serverName}' not found");
        }

        var safeName = ValidateFileName(fileName);
        var lowerName = safeName.ToLowerInvariant();
        if (!lowerName.EndsWith(".jar") && !lowerName.EndsWith(".jar.disabled"))
        {
            throw new ArgumentException("Plugins must be .jar files");
        }

        var pluginsPath = EnsurePluginsPath(serverName);
        var targetPath = Path.Combine(pluginsPath, safeName);
        await using var target = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(target, cancellationToken);
        await OwnershipHelper.ChangeOwnershipAsync(
            targetPath,
            _hostOptions.RunAsUid,
            _hostOptions.RunAsGid,
            _logger,
            cancellationToken);

        MarkRestartRequired(serverPath);
        _logger.LogInformation("Uploaded plugin {FileName} for server {ServerName}", safeName, serverName);
    }

    public Task DeletePluginAsync(string serverName, string fileName, CancellationToken cancellationToken)
    {
        var serverPath = GetServerPath(serverName);
        if (!Directory.Exists(serverPath))
        {
            throw new DirectoryNotFoundException($"Server '{serverName}' not found");
        }

        var safeName = ValidateFileName(fileName);
        var pluginsPath = GetPluginsPath(serverName);
        var targetPath = Path.Combine(pluginsPath, safeName);

        if (!File.Exists(targetPath))
        {
            throw new FileNotFoundException($"Plugin '{safeName}' not found");
        }

        File.Delete(targetPath);
        MarkRestartRequired(serverPath);
        _logger.LogInformation("Deleted plugin {FileName} for server {ServerName}", safeName, serverName);
        return Task.CompletedTask;
    }

    public Task<string> GetPluginPathAsync(string serverName, string fileName, CancellationToken cancellationToken)
    {
        var serverPath = GetServerPath(serverName);
        if (!Directory.Exists(serverPath))
        {
            throw new DirectoryNotFoundException($"Server '{serverName}' not found");
        }

        var safeName = ValidateFileName(fileName);
        var pluginsPath = GetPluginsPath(serverName);
        var targetPath = Path.Combine(pluginsPath, safeName);

        if (!File.Exists(targetPath))
        {
            throw new FileNotFoundException($"Plugin '{safeName}' not found");
        }

        return Task.FromResult(targetPath);
    }

    public Task SetPluginEnabledAsync(string serverName, string fileName, bool enabled, CancellationToken cancellationToken)
    {
        var serverPath = GetServerPath(serverName);
        if (!Directory.Exists(serverPath))
        {
            throw new DirectoryNotFoundException($"Server '{serverName}' not found");
        }

        var safeName = ValidateFileName(fileName);
        var pluginsPath = GetPluginsPath(serverName);
        var normalizedName = safeName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
            ? safeName[..^".disabled".Length]
            : safeName;

        var enabledPath = Path.Combine(pluginsPath, normalizedName);
        var disabledPath = Path.Combine(pluginsPath, $"{normalizedName}.disabled");

        if (enabled)
        {
            if (File.Exists(disabledPath))
            {
                File.Move(disabledPath, enabledPath, overwrite: false);
                MarkRestartRequired(serverPath);
            }
            else if (!File.Exists(enabledPath))
            {
                throw new FileNotFoundException($"Plugin '{normalizedName}' not found");
            }
        }
        else
        {
            if (File.Exists(enabledPath))
            {
                File.Move(enabledPath, disabledPath, overwrite: false);
                MarkRestartRequired(serverPath);
            }
            else if (!File.Exists(disabledPath))
            {
                throw new FileNotFoundException($"Plugin '{normalizedName}' not found");
            }
        }

        return Task.CompletedTask;
    }

    private string GetServerPath(string serverName) =>
        Path.Combine(_hostOptions.BaseDirectory, _hostOptions.ServersPathSegment, serverName);

    private string GetPluginsPath(string serverName) =>
        Path.Combine(GetServerPath(serverName), "plugins");

    private string EnsurePluginsPath(string serverName)
    {
        var path = GetPluginsPath(serverName);
        Directory.CreateDirectory(path);
        OwnershipHelper.TrySetOwnership(path, _hostOptions.RunAsUid, _hostOptions.RunAsGid, _logger);
        return path;
    }

    private void MarkRestartRequired(string serverPath)
    {
        try
        {
            var flagPath = Path.Combine(serverPath, RestartFlagFile);
            File.WriteAllText(flagPath, DateTimeOffset.UtcNow.ToString("O"));
            OwnershipHelper.TrySetOwnership(flagPath, _hostOptions.RunAsUid, _hostOptions.RunAsGid, _logger);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to mark restart required for {ServerPath}", serverPath);
        }
    }

    private static string ValidateFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("File name is required");
        }

        var safeName = Path.GetFileName(fileName);
        if (!string.Equals(safeName, fileName, StringComparison.Ordinal))
        {
            throw new ArgumentException("Invalid file name");
        }

        return safeName;
    }
}
