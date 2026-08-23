using Cleanuparr.Domain.Entities;
using Cleanuparr.Domain.Entities.Sabnzbd.Response;
using Cleanuparr.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Cleanuparr.Infrastructure.Features.DownloadClient.Sabnzbd;

public partial class SabnzbdService
{
    /// <inheritdoc/>
    /// <remarks>
    /// SABnzbd splits jobs across two separate lists: the queue (still active) and the history
    /// (completed or failed). A download that finished since it was queued in the *arr will no
    /// longer be in the queue, so both lists are checked to find the job.
    /// </remarks>
    public override async Task<DownloadCheckResult> ShouldRemoveFromArrQueueAsync(string hash, IReadOnlyList<string> ignoredDownloads)
    {
        DownloadCheckResult result = new();

        SabnzbdItemWrapper? torrent = await FindItemAsync(hash);

        if (torrent is null)
        {
            _logger.LogDebug("Failed to find job {hash} in the {name} download client", hash, _downloadClientConfig.Name);
            return result;
        }

        result.Found = true;
        result.IsPrivate = false;
        result.Torrent = torrent;
        SetDownloadClientContext();

        if (torrent.IsIgnored(ignoredDownloads))
        {
            _logger.LogInformation("skip | download is ignored | {name}", torrent.Name);
            return result;
        }

        if (torrent.IsHistoryItem)
        {
            return await EvaluateHistoryItem(torrent, result);
        }

        (result.ShouldRemove, result.DeleteReason, result.DeleteFromClient, result.ChangeCategory) = await EvaluateDownloadRemoval(torrent);

        return result;
    }

    /// <summary>
    /// A history item that failed is treated as a stuck/unwanted download and removed; a
    /// completed history item has nothing left to evaluate.
    /// </summary>
    private Task<DownloadCheckResult> EvaluateHistoryItem(SabnzbdItemWrapper torrent, DownloadCheckResult result)
    {
        if (string.Equals(torrent.Status, "Failed", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("job failed | removing download | {name}", torrent.Name);
            result.ShouldRemove = true;
            result.DeleteReason = DeleteReason.None;
            result.DeleteFromClient = true;
        }

        return Task.FromResult(result);
    }

    private async Task<(bool ShouldRemove, DeleteReason Reason, bool DeleteFromClient, bool ChangeCategory)> EvaluateDownloadRemoval(ITorrentItemWrapper wrapper)
    {
        (bool ShouldRemove, DeleteReason Reason, bool DeleteFromClient, bool ChangeCategory) slowResult = await CheckIfSlow(wrapper);

        if (slowResult.ShouldRemove)
        {
            return slowResult;
        }

        return await CheckIfStuck(wrapper);
    }

    private async Task<(bool ShouldRemove, DeleteReason Reason, bool DeleteFromClient, bool ChangeCategory)> CheckIfSlow(ITorrentItemWrapper wrapper)
    {
        if (!wrapper.IsDownloading())
        {
            _logger.LogTrace("skip slow check | download is not in downloading state | {name}", wrapper.Name);
            return (false, DeleteReason.None, false, false);
        }

        if (wrapper.DownloadSpeed <= 0)
        {
            _logger.LogTrace("skip slow check | download speed is 0 | {name}", wrapper.Name);
            return (false, DeleteReason.None, false, false);
        }

        return await _queueRuleEvaluator.EvaluateSlowRulesAsync(wrapper);
    }

    /// <summary>
    /// SABnzbd has no direct "stalled" signal like BitTorrent peers dropping off. As a
    /// best-effort proxy, a job that is in the Downloading state but reporting zero throughput
    /// (and is not in a post-processing state) is treated as stalled.
    /// </summary>
    private async Task<(bool ShouldRemove, DeleteReason Reason, bool DeleteFromClient, bool ChangeCategory)> CheckIfStuck(ITorrentItemWrapper wrapper)
    {
        if (!wrapper.IsStalled())
        {
            _logger.LogTrace("skip stalled check | download is not in stalled state | {name}", wrapper.Name);
            return (false, DeleteReason.None, false, false);
        }

        return await _queueRuleEvaluator.EvaluateStallRulesAsync(wrapper);
    }

    /// <summary>
    /// Finds a job by nzo_id, checking the active queue first and falling back to history since
    /// completed/failed jobs no longer appear in the queue.
    /// </summary>
    private async Task<SabnzbdItemWrapper?> FindItemAsync(string nzoId)
    {
        SabnzbdQueue queue = await _client.GetQueueAsync();
        SabnzbdQueueSlot? queueSlot = queue.Slots.FirstOrDefault(x => x.NzoId.Equals(nzoId, StringComparison.OrdinalIgnoreCase));

        if (queueSlot is not null)
        {
            return new SabnzbdItemWrapper(queueSlot);
        }

        SabnzbdHistory history = await _client.GetHistoryAsync();
        SabnzbdHistorySlot? historySlot = history.Slots.FirstOrDefault(x => x.NzoId.Equals(nzoId, StringComparison.OrdinalIgnoreCase));

        return historySlot is not null ? new SabnzbdItemWrapper(historySlot) : null;
    }
}
