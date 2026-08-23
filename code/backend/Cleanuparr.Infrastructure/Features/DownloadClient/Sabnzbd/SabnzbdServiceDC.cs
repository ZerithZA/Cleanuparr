using Cleanuparr.Domain.Entities;
using Cleanuparr.Domain.Entities.Sabnzbd.Response;
using Cleanuparr.Infrastructure.Features.Context;
using Cleanuparr.Persistence.Models.Configuration.DownloadCleaner;
using Microsoft.Extensions.Logging;

namespace Cleanuparr.Infrastructure.Features.DownloadClient.Sabnzbd;

public partial class SabnzbdService
{
    /// <inheritdoc/>
    /// <remarks>
    /// Usenet downloads are never seeded, so there is nothing for the seeding-rules cleanup to
    /// evaluate here. An empty list is a safe no-op: <see cref="DownloadService.CleanDownloadsAsync"/>
    /// returns immediately when given no downloads.
    /// </remarks>
    public override Task<List<ITorrentItemWrapper>> GetSeedingDownloads() =>
        Task.FromResult(new List<ITorrentItemWrapper>());

    /// <inheritdoc/>
    public override async Task<List<ITorrentItemWrapper>> GetAllTorrentsLite()
    {
        SabnzbdQueue queue = await _client.GetQueueAsync();
        SabnzbdHistory history = await _client.GetHistoryAsync();

        List<ITorrentItemWrapper> result = new(queue.Slots.Count + history.Slots.Count);

        result.AddRange(queue.Slots
            .Where(x => !string.IsNullOrEmpty(x.NzoId))
            .Select(ITorrentItemWrapper (slot) => new SabnzbdItemWrapper(slot)));

        result.AddRange(history.Slots
            .Where(x => !string.IsNullOrEmpty(x.NzoId))
            .Select(ITorrentItemWrapper (slot) => new SabnzbdItemWrapper(slot)));

        return result;
    }

    /// <inheritdoc/>
    public override Task<IReadOnlyList<string>> GetClaimedPathsAsync(IReadOnlyList<ITorrentItemWrapper> torrents) =>
        BuildClaimedPathsAsync(torrents, _ => Task.FromResult<IReadOnlyCollection<string>>([]));

    /// <inheritdoc/>
    public override List<ITorrentItemWrapper>? FilterDownloadsToBeCleanedAsync(List<ITorrentItemWrapper>? downloads, List<ISeedingRule> seedingRules) =>
        downloads
            ?.Where(x => !string.IsNullOrEmpty(x.Hash))
            .Where(x => seedingRules.Any(rule => rule.Categories.Any(cat => cat.Equals(x.Category, StringComparison.OrdinalIgnoreCase))))
            .ToList();

    /// <inheritdoc/>
    public override List<ITorrentItemWrapper>? FilterDownloadsToChangeCategoryAsync(List<ITorrentItemWrapper>? downloads, UnlinkedConfig unlinkedConfig) =>
        downloads
            ?.Where(x => !string.IsNullOrEmpty(x.Hash))
            .Where(x => unlinkedConfig.Categories.Any(cat => cat.Equals(x.Category, StringComparison.InvariantCultureIgnoreCase)))
            .ToList();

    /// <inheritdoc/>
    public override async Task DeleteDownload(ITorrentItemWrapper torrent, bool deleteSourceFiles)
    {
        bool fromHistory = torrent is SabnzbdItemWrapper { IsHistoryItem: true };
        await _client.DeleteAsync(torrent.Hash, deleteSourceFiles, fromHistory);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// SABnzbd has no dedicated "create category" API call — categories are defined in the
    /// server's own config. <see cref="SabnzbdClient.CreateCategoryAsync"/> adds the category to
    /// that config via <c>set_config</c>; if the SABnzbd instance rejects it (e.g. an older
    /// version), the failure is logged rather than surfaced, matching the no-op fallback
    /// described in the task scope.
    /// </remarks>
    public override async Task CreateCategoryAsync(string name)
    {
        _logger.LogDebug("Creating category {name}", name);

        try
        {
            await _dryRunInterceptor.InterceptAsync(() => _client.CreateCategoryAsync(name));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create category {name} on SABnzbd client {clientId}; continuing", name, _downloadClientConfig.Id);
        }
    }

    /// <inheritdoc/>
    public override async Task ChangeCategoryForNoHardLinksAsync(List<ITorrentItemWrapper>? downloads, UnlinkedConfig unlinkedConfig)
    {
        if (downloads?.Count is null or 0)
        {
            return;
        }

        foreach (ITorrentItemWrapper torrent in downloads)
        {
            if (string.IsNullOrEmpty(torrent.Name) || string.IsNullOrEmpty(torrent.Hash) || string.IsNullOrEmpty(torrent.Category))
            {
                continue;
            }

            if (string.IsNullOrEmpty(torrent.SavePath))
            {
                _logger.LogDebug("skip | no save path for {name}", torrent.Name);
                continue;
            }

            ContextProvider.Set(ContextProvider.Keys.ItemName, torrent.Name);
            ContextProvider.Set(ContextProvider.Keys.Hash, torrent.Hash);
            SetDownloadClientContext();

            long hardlinkCount = _hardLinkFileService.GetHardLinkCount(torrent.SavePath, unlinkedConfig.IgnoredRootDirs.Count > 0);

            if (hardlinkCount < 0)
            {
                _logger.LogError("skip | file does not exist or insufficient permissions | {file}", torrent.SavePath);
                continue;
            }

            if (hardlinkCount > 0)
            {
                _logger.LogDebug("skip | download has hardlinks | {name}", torrent.Name);
                continue;
            }

            await _dryRunInterceptor.InterceptAsync(() => ChangeCategory(torrent.Hash, unlinkedConfig.TargetCategory));

            await _eventPublisher.PublishCategoryChanged(torrent.Category, unlinkedConfig.TargetCategory, false);

            _logger.LogInformation("category changed for {name}", torrent.Name);
            torrent.Category = unlinkedConfig.TargetCategory;
        }
    }

    /// <inheritdoc/>
    /// <remarks>SABnzbd has no separate tag/label concept; <paramref name="useTag"/> is ignored and the category is always changed.</remarks>
    public override async Task ChangeTorrentCategoryAsync(ITorrentItemWrapper torrent, string targetCategory, bool useTag)
    {
        ContextProvider.Set(ContextProvider.Keys.ItemName, torrent.Name);
        ContextProvider.Set(ContextProvider.Keys.Hash, torrent.Hash);
        SetDownloadClientContext();

        string currentCategory = torrent.Category ?? string.Empty;

        await _dryRunInterceptor.InterceptAsync(() => ChangeCategory(torrent.Hash, targetCategory));

        await _eventPublisher.PublishCategoryChanged(currentCategory, targetCategory, false);

        torrent.Category = targetCategory;
    }

    protected virtual async Task ChangeCategory(string nzoId, string newCategory)
    {
        await _client.ChangeCategoryAsync(nzoId, newCategory);
    }
}
