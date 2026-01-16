using MineOS.Application.Dtos;

namespace MineOS.Application.Interfaces;

public interface IModpackInstallState
{
    string JobId { get; }
    string ServerName { get; }
    bool IsComplete { get; }
    void SetRunning();
    void SetTotalMods(int total);
    void UpdateProgress(int percentage, string step);
    void UpdateModProgress(int index, string? modName);
    void AppendOutput(string line);
    void RecordInstalledFile(string filePath);
    IReadOnlyList<string> GetInstalledFilePaths();
    void MarkCompleted();
    void MarkFailed(string error);
}

public interface IBackgroundJobService
{
    string QueueJob(string type, string serverName, Func<IProgress<JobProgressDto>, CancellationToken, Task> work);
    JobStatusDto? GetJobStatus(string jobId);
    IAsyncEnumerable<JobProgressDto> StreamJobProgressAsync(string jobId, CancellationToken cancellationToken);

    // Modpack-specific methods
    string QueueModpackInstall(string serverName, Func<IModpackInstallState, CancellationToken, Task> work);
    ModpackInstallProgressDto? GetModpackInstallStatus(string jobId);
    IAsyncEnumerable<ModpackInstallProgressDto> StreamModpackProgressAsync(string jobId, CancellationToken cancellationToken);
}
