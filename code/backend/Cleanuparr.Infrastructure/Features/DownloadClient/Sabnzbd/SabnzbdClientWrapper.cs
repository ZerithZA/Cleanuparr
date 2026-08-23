using Cleanuparr.Domain.Entities.Sabnzbd.Response;

namespace Cleanuparr.Infrastructure.Features.DownloadClient.Sabnzbd;

public sealed class SabnzbdClientWrapper : ISabnzbdClientWrapper
{
    private readonly SabnzbdClient _client;

    public SabnzbdClientWrapper(SabnzbdClient client)
    {
        _client = client;
    }

    public Task<string?> GetVersionAsync()
        => _client.GetVersionAsync();

    public Task<SabnzbdQueue> GetQueueAsync()
        => _client.GetQueueAsync();

    public Task<SabnzbdHistory> GetHistoryAsync(int limit = 0)
        => _client.GetHistoryAsync(limit);

    public Task<List<string>> AddByUrlAsync(string url, string? category = null)
        => _client.AddByUrlAsync(url, category);

    public Task DeleteAsync(string nzoId, bool deleteFiles, bool fromHistory)
        => _client.DeleteAsync(nzoId, deleteFiles, fromHistory);

    public Task ChangeCategoryAsync(string nzoId, string category)
        => _client.ChangeCategoryAsync(nzoId, category);

    public Task CreateCategoryAsync(string name)
        => _client.CreateCategoryAsync(name);
}
