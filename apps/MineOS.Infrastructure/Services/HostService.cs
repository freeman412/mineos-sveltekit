using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;
using MineOS.Application.Options;

namespace MineOS.Infrastructure.Services;

public sealed class HostService : IHostService
{
    private readonly HostOptions _options;
    private readonly ILogger<HostService> _logger;
    private readonly IProcessManager _processManager;
    private readonly IMonitoringService _monitoringService;

    public HostService(
        IOptions<HostOptions> options,
        ILogger<HostService> logger,
        IProcessManager processManager,
        IMonitoringService monitoringService)
    {
        _options = options.Value;
        _logger = logger;
        _processManager = processManager;
        _monitoringService = monitoringService;
    }

    public Task<HostMetricsDto> GetMetricsAsync(CancellationToken cancellationToken)
    {
        var uptimeSeconds = (long)(Environment.TickCount64 / 1000);
        var freeMemBytes = TryReadMemAvailableBytes();
        var loadAvg = TryReadLoadAverage();
        var disk = ReadDiskMetrics(_options.BaseDirectory);

        if (disk.total == 0)
        {
            _logger.LogWarning("Disk metrics unavailable for {BaseDirectory}", _options.BaseDirectory);
        }

        var metrics = new HostMetricsDto(
            uptimeSeconds,
            freeMemBytes,
            loadAvg,
            new DiskMetricsDto(disk.available, disk.free, disk.total));

        return Task.FromResult(metrics);
    }

    public async IAsyncEnumerable<HostMetricsDto> StreamMetricsAsync(
        TimeSpan interval,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            yield return await GetMetricsAsync(cancellationToken);
            try
            {
                await Task.Delay(interval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
        }
    }

    public async Task<IReadOnlyList<ServerSummaryDto>> GetServersAsync(CancellationToken cancellationToken)
    {
        var serversPath = Path.Combine(_options.BaseDirectory, _options.ServersPathSegment);
        if (!Directory.Exists(serversPath))
        {
            return Array.Empty<ServerSummaryDto>();
        }

        var results = new List<ServerSummaryDto>();
        foreach (var dir in Directory.EnumerateDirectories(serversPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(dir);
            var props = TryReadProperties(Path.Combine(dir, "server.properties"));

            var port = props.TryGetValue("server-port", out var portValue) && int.TryParse(portValue, out var portInt)
                ? portInt
                : (int?)null;
            var maxPlayers = props.TryGetValue("max-players", out var maxValue) && int.TryParse(maxValue, out var maxInt)
                ? maxInt
                : (int?)null;
            var processInfo = _processManager.GetServerProcess(name);
            var up = processInfo?.JavaPid != null || processInfo?.ScreenPid != null;
            PingInfoDto? ping = null;
            long? memoryBytes = null;
            var needsRestart = File.Exists(Path.Combine(dir, ".mineos-restart-required"));

            if (up)
            {
                var pingTask = _monitoringService.GetPingInfoAsync(name, cancellationToken);
                var memoryTask = _monitoringService.GetMemoryInfoAsync(name, cancellationToken);

                await Task.WhenAll(pingTask, memoryTask);
                ping = await pingTask;
                memoryBytes = memoryTask.Result.ResidentMemory;
            }

            var playersOnline = ping?.PlayersOnline;
            var playersMax = ping?.PlayersMax ?? maxPlayers;

            results.Add(new ServerSummaryDto(
                Name: name,
                Up: up,
                Profile: null,
                Port: port,
                PlayersOnline: playersOnline,
                PlayersMax: playersMax,
                MemoryBytes: memoryBytes,
                NeedsRestart: needsRestart));
        }

        return results;
    }

    public Task<IReadOnlyList<ProfileDto>> GetProfilesAsync(CancellationToken cancellationToken)
    {
        var profilesPath = Path.Combine(_options.BaseDirectory, _options.ProfilesPathSegment);
        if (!Directory.Exists(profilesPath))
        {
            return Task.FromResult<IReadOnlyList<ProfileDto>>(Array.Empty<ProfileDto>());
        }

        var results = new List<ProfileDto>();
        foreach (var dir in Directory.EnumerateDirectories(profilesPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = Path.GetFileName(dir);
            var jar = Directory.EnumerateFiles(dir)
                .FirstOrDefault(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
                                     f.EndsWith(".phar", StringComparison.OrdinalIgnoreCase));

            results.Add(new ProfileDto(
                Id: id,
                Group: "local",
                Type: "unknown",
                Version: string.Empty,
                ReleaseTime: string.Empty,
                Url: string.Empty,
                Filename: jar != null ? Path.GetFileName(jar) : string.Empty,
                Downloaded: jar != null,
                Progress: null));
        }

        return Task.FromResult<IReadOnlyList<ProfileDto>>(results);
    }

    public Task<IReadOnlyList<ArchiveEntryDto>> GetImportsAsync(CancellationToken cancellationToken)
    {
        var importPath = Path.Combine(_options.BaseDirectory, _options.ImportPathSegment);
        if (!Directory.Exists(importPath))
        {
            return Task.FromResult<IReadOnlyList<ArchiveEntryDto>>(Array.Empty<ArchiveEntryDto>());
        }

        var results = new List<ArchiveEntryDto>();
        foreach (var file in Directory.EnumerateFiles(importPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(file);
            results.Add(new ArchiveEntryDto(info.LastWriteTimeUtc, info.Length, info.Name));
        }

        return Task.FromResult<IReadOnlyList<ArchiveEntryDto>>(results);
    }

    public Task<IReadOnlyList<string>> GetLocalesAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_options.LocalesPath))
        {
            return Task.FromResult<IReadOnlyList<string>>(new[] { "en_US" });
        }

        var locales = Directory.EnumerateFiles(_options.LocalesPath, "locale-*.json")
            .Select(path => Path.GetFileName(path))
            .Select(name => name.Replace("locale-", string.Empty, StringComparison.OrdinalIgnoreCase)
                                .Replace(".json", string.Empty, StringComparison.OrdinalIgnoreCase))
            .Where(locale => !string.IsNullOrWhiteSpace(locale))
            .OrderBy(locale => locale)
            .ToArray();

        return Task.FromResult<IReadOnlyList<string>>(locales);
    }

    public Task<IReadOnlyList<HostUserDto>> GetUsersAsync(CancellationToken cancellationToken)
    {
        var passwdPath = "/etc/passwd";
        if (!File.Exists(passwdPath))
        {
            return Task.FromResult<IReadOnlyList<HostUserDto>>(Array.Empty<HostUserDto>());
        }

        var results = new List<HostUserDto>();
        foreach (var line in File.ReadLines(passwdPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            var parts = line.Split(':');
            if (parts.Length < 7)
            {
                continue;
            }

            if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var uid))
            {
                continue;
            }

            if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var gid))
            {
                continue;
            }

            results.Add(new HostUserDto(parts[0], uid, gid, parts[5]));
        }

        return Task.FromResult<IReadOnlyList<HostUserDto>>(results);
    }

    public Task<IReadOnlyList<HostGroupDto>> GetGroupsAsync(CancellationToken cancellationToken)
    {
        var groupPath = "/etc/group";
        if (!File.Exists(groupPath))
        {
            return Task.FromResult<IReadOnlyList<HostGroupDto>>(Array.Empty<HostGroupDto>());
        }

        var results = new List<HostGroupDto>();
        foreach (var line in File.ReadLines(groupPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            var parts = line.Split(':');
            if (parts.Length < 3)
            {
                continue;
            }

            if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var gid))
            {
                continue;
            }

            results.Add(new HostGroupDto(parts[0], gid));
        }

        return Task.FromResult<IReadOnlyList<HostGroupDto>>(results);
    }

    private static Dictionary<string, string> TryReadProperties(string path)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
        {
            return dict;
        }

        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            var idx = line.IndexOf('=');
            if (idx <= 0)
            {
                continue;
            }

            var key = line.Substring(0, idx).Trim();
            var value = line[(idx + 1)..].Trim();
            dict[key] = value;
        }

        return dict;
    }

    private static (long available, long free, long total) ReadDiskMetrics(string path)
    {
        try
        {
            var root = ResolveDriveRoot(path);
            if (string.IsNullOrWhiteSpace(root))
            {
                return (0, 0, 0);
            }

            var drive = new DriveInfo(root);
            if (!drive.IsReady)
            {
                return (0, 0, 0);
            }
            return (drive.AvailableFreeSpace, drive.TotalFreeSpace, drive.TotalSize);
        }
        catch
        {
            return (0, 0, 0);
        }
    }

    private static long TryReadMemAvailableBytes()
    {
        if (OperatingSystem.IsLinux())
        {
            return TryReadMemAvailableBytesLinux();
        }

        if (OperatingSystem.IsWindows())
        {
            return TryReadMemAvailableBytesWindows();
        }

        var gcInfo = GC.GetGCMemoryInfo();
        return gcInfo.TotalAvailableMemoryBytes > 0 ? gcInfo.TotalAvailableMemoryBytes : 0;
    }

    private static long TryReadMemAvailableBytesLinux()
    {
        const string meminfoPath = "/proc/meminfo";
        if (!File.Exists(meminfoPath))
        {
            return 0;
        }

        foreach (var line in File.ReadLines(meminfoPath))
        {
            if (!line.StartsWith("MemAvailable:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                return 0;
            }

            if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var kb))
            {
                return 0;
            }

            return kb * 1024;
        }

        return 0;
    }

    private static double[] TryReadLoadAverage()
    {
        if (!OperatingSystem.IsLinux())
        {
            return new[] { 0d, 0d, 0d };
        }

        return TryReadLoadAverageLinux();
    }

    private static double[] TryReadLoadAverageLinux()
    {
        const string loadavgPath = "/proc/loadavg";
        if (!File.Exists(loadavgPath))
        {
            return new[] { 0d, 0d, 0d };
        }

        var line = File.ReadLines(loadavgPath).FirstOrDefault();
        if (line == null)
        {
            return new[] { 0d, 0d, 0d };
        }

        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
        {
            return new[] { 0d, 0d, 0d };
        }

        var values = new double[3];
        for (var i = 0; i < 3; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                values[i] = 0d;
            }
            else
            {
                values[i] = value;
            }
        }

        return values;
    }

    private static string? ResolveDriveRoot(string path)
    {
        var root = Path.GetPathRoot(path);
        if (!string.IsNullOrWhiteSpace(root))
        {
            return root;
        }

        root = Path.GetPathRoot(Environment.SystemDirectory);
        if (!string.IsNullOrWhiteSpace(root))
        {
            return root;
        }

        return Path.GetPathRoot(AppContext.BaseDirectory);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    private static long TryReadMemAvailableBytesWindows()
    {
        var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? (long)status.ullAvailPhys : 0;
    }
}
