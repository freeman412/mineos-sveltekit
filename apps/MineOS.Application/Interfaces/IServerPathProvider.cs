namespace MineOS.Application.Interfaces;

/// <summary>
/// The on-disk layout of a managed server. Extracted from ConsoleService so the
/// convention lives in exactly one place.
/// </summary>
public interface IServerPathProvider
{
    string GetServerPath(string serverName);
    string GetLogPath(string serverName);
    string GetCrashReportsPath(string serverName);
}
