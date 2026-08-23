using Cleanuparr.Domain.Entities.Sabnzbd.Response;

namespace Cleanuparr.Infrastructure.Features.DownloadClient.Sabnzbd;

/// <summary>
/// Wraps <see cref="SabnzbdClient"/> behind an interface so download client services can be unit tested
/// with a substitute, mirroring the pattern used by the other hand-rolled download clients.
/// </summary>
public interface ISabnzbdClientWrapper
{
    Task<string?> GetVersionAsync();
    Task<SabnzbdQueue> GetQueueAsync();
    Task<SabnzbdHistory> GetHistoryAsync(int limit = 0);
    Task<List<string>> AddByUrlAsync(string url, string? category = null);
    Task DeleteAsync(string nzoId, bool deleteFiles, bool fromHistory);
    Task ChangeCategoryAsync(string nzoId, string category);
    Task CreateCategoryAsync(string name);
}
