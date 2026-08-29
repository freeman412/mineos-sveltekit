using MineOS.Application.Dtos;

namespace MineOS.Application.Interfaces;

public interface IAiDiagnosisService
{
    /// <summary>Builds the exact payload that would be sent. Makes no outbound call.</summary>
    Task<DiagnosisPreviewDto> PreviewAsync(string serverName, int crashEventId, CancellationToken ct);

    /// <summary>
    /// Runs (or returns a cached) diagnosis. Keyed on a crash event id so the
    /// watchdog can call this unchanged when automatic mode is added.
    /// </summary>
    Task<CrashDiagnosisDto> DiagnoseAsync(string serverName, int crashEventId, CancellationToken ct);

    Task<CrashDiagnosisDto?> GetAsync(string serverName, int crashEventId, CancellationToken ct);
}
