using MineOS.Application.Dtos;

namespace MineOS.Application.Interfaces;

public interface IBackgroundJobService
{
    string QueueJob(string type, string serverName, Func<IProgress<JobProgressDto>, CancellationToken, Task> work);
    JobStatusDto? GetJobStatus(string jobId);
    IAsyncEnumerable<JobProgressDto> StreamJobProgressAsync(string jobId, CancellationToken cancellationToken);
}
