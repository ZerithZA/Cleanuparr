using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Cleanuparr.Domain.Enums;
using Cleanuparr.Infrastructure.Features.Context;
using Microsoft.Extensions.Logging;

namespace Cleanuparr.Infrastructure.Features.DownloadClient.Sabnzbd;

public partial class SabnzbdService
{
    /// <inheritdoc/>
    /// <remarks>
    /// SABnzbd has no per-file "skip priority" like qBittorrent, so individual unwanted files
    /// inside an NZB job cannot be selectively excluded from the download. When the job's own
    /// filename matches a blocklist pattern, the whole job is flagged for removal instead of
    /// just the offending file(s).
    /// </remarks>
    public override async Task<BlockFilesResult> BlockUnwantedFilesAsync(string hash, IReadOnlyList<string> ignoredDownloads)
    {
        BlockFilesResult result = new();

        SabnzbdItemWrapper? torrent = await FindItemAsync(hash);

        if (torrent is null)
        {
            _logger.LogDebug("failed to find job {hash} in the {name} download client", hash, _downloadClientConfig.Name);
            return result;
        }

        result.IsPrivate = false;
        result.Found = true;
        result.Torrent = torrent;
        SetDownloadClientContext();

        if (torrent.IsIgnored(ignoredDownloads))
        {
            _logger.LogInformation("skip | download is ignored | {name}", torrent.Name);
            return result;
        }

        InstanceType instanceType = (InstanceType)ContextProvider.Get<object>(nameof(InstanceType));
        BlocklistType blocklistType = _blocklistProvider.GetBlocklistType(instanceType);
        ConcurrentBag<string> patterns = _blocklistProvider.GetPatterns(instanceType);
        ConcurrentBag<Regex> regexes = _blocklistProvider.GetRegexes(instanceType);

        if (_filenameEvaluator.IsValid(torrent.Name, blocklistType, patterns, regexes))
        {
            _logger.LogTrace("File is valid | {file}", torrent.Name);
            return result;
        }

        _logger.LogInformation("unwanted file found | {file}", torrent.Name);
        result.ShouldRemove = true;
        result.DeleteReason = DeleteReason.AtLeastOneFileBlocked;

        return result;
    }
}
