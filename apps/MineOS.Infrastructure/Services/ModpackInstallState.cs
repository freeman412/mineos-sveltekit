using MineOS.Application.Dtos;
using MineOS.Application.Interfaces;

namespace MineOS.Infrastructure.Services;

public sealed class ModpackInstallState : IModpackInstallState
{
    private readonly object _lock = new();
    private readonly List<string> _outputLines = new();
    private readonly List<string> _installedFilePaths = new();
    private const int MaxOutputLines = 100;

    public ModpackInstallState(string jobId, string serverName)
    {
        JobId = jobId;
        ServerName = serverName;
        Status = "queued";
    }

    public string JobId { get; }
    public string ServerName { get; }
    public string Status { get; private set; }
    public int Percentage { get; private set; }
    public string? CurrentStep { get; private set; }
    public int CurrentModIndex { get; private set; }
    public int TotalMods { get; private set; }
    public string? CurrentModName { get; private set; }
    public string? Error { get; private set; }
    public bool IsComplete => Status == "completed" || Status == "failed";

    public void SetRunning()
    {
        lock (_lock)
        {
            Status = "running";
        }
    }

    public void SetTotalMods(int total)
    {
        lock (_lock)
        {
            TotalMods = total;
        }
    }

    public void UpdateProgress(int percentage, string step)
    {
        lock (_lock)
        {
            Percentage = percentage;
            CurrentStep = step;
        }
    }

    public void UpdateModProgress(int index, string? modName)
    {
        lock (_lock)
        {
            CurrentModIndex = index;
            CurrentModName = modName;
            // Calculate percentage based on mod progress (0-90% for mods, 90-100% for finalization)
            if (TotalMods > 0)
            {
                Percentage = (int)(90.0 * index / TotalMods);
            }
        }
    }

    public void AppendOutput(string line)
    {
        lock (_lock)
        {
            _outputLines.Add(line);
            // Keep only the last MaxOutputLines lines
            while (_outputLines.Count > MaxOutputLines)
            {
                _outputLines.RemoveAt(0);
            }
        }
    }

    public void RecordInstalledFile(string filePath)
    {
        lock (_lock)
        {
            _installedFilePaths.Add(filePath);
        }
    }

    public IReadOnlyList<string> GetInstalledFilePaths()
    {
        lock (_lock)
        {
            return _installedFilePaths.ToList();
        }
    }

    public void MarkCompleted()
    {
        lock (_lock)
        {
            Status = "completed";
            Percentage = 100;
            CurrentStep = "Installation complete";
        }
    }

    public void MarkFailed(string error)
    {
        lock (_lock)
        {
            Status = "failed";
            Error = error;
            CurrentStep = "Installation failed";
        }
    }

    public ModpackInstallProgressDto ToDto()
    {
        lock (_lock)
        {
            return new ModpackInstallProgressDto(
                JobId,
                ServerName,
                Status,
                Percentage,
                CurrentStep,
                CurrentModIndex,
                TotalMods,
                CurrentModName,
                _outputLines.ToList(),
                Error
            );
        }
    }
}
