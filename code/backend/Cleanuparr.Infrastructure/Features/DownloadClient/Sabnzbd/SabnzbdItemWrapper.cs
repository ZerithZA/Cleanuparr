using System.Globalization;
using Cleanuparr.Domain.Entities;
using Cleanuparr.Domain.Entities.Sabnzbd.Response;

namespace Cleanuparr.Infrastructure.Features.DownloadClient.Sabnzbd;

/// <summary>
/// Wrapper that adapts a SABnzbd queue or history slot to the <see cref="ITorrentItemWrapper"/>
/// abstraction shared across download clients.
/// </summary>
/// <remarks>
/// SABnzbd is a Usenet client, so several torrent-only concepts have no meaning here and are
/// stubbed: <see cref="Ratio"/> is always 0, <see cref="SeederCount"/> is always null,
/// <see cref="IsPrivate"/> is always false, and <see cref="TrackerDomains"/>/<see cref="Tags"/>
/// are always empty. The SABnzbd job id (nzo_id) is reused as <see cref="Hash"/> since it is the
/// stable identity SABnzbd exposes for a job.
/// </remarks>
public sealed class SabnzbdItemWrapper : ITorrentItemWrapper
{
    /// <summary>
    /// States reported by SABnzbd for queue slots that are still being fetched/processed after
    /// the download itself has finished (post-processing). A job in one of these states is no
    /// longer downloading, but is not stalled either.
    /// </summary>
    private static readonly HashSet<string> PostProcessingStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "Checking",
        "QuickCheck",
        "Verifying",
        "Repairing",
        "Extracting",
        "Moving",
        "Running Script",
    };

    private readonly SabnzbdQueueSlot? _queueSlot;
    private readonly SabnzbdHistorySlot? _historySlot;

    /// <summary>
    /// Wraps a queue slot (an active, in-progress job).
    /// </summary>
    public SabnzbdItemWrapper(SabnzbdQueueSlot queueSlot)
    {
        _queueSlot = queueSlot ?? throw new ArgumentNullException(nameof(queueSlot));
    }

    /// <summary>
    /// Wraps a history slot (a completed or failed job).
    /// </summary>
    public SabnzbdItemWrapper(SabnzbdHistorySlot historySlot)
    {
        _historySlot = historySlot ?? throw new ArgumentNullException(nameof(historySlot));
    }

    /// <summary>
    /// True when this wraps a history slot rather than a queue slot.
    /// </summary>
    public bool IsHistoryItem => _historySlot is not null;

    /// <summary>
    /// The raw SABnzbd status string (e.g. Downloading, Queued, Completed, Failed).
    /// </summary>
    public string Status => _queueSlot?.Status ?? _historySlot?.Status ?? string.Empty;

    public string Hash => _queueSlot?.NzoId ?? _historySlot?.NzoId ?? string.Empty;

    public string Name => _queueSlot?.Filename ?? _historySlot?.Name ?? string.Empty;

    /// <inheritdoc/>
    /// <remarks>Usenet has no notion of private trackers; always false.</remarks>
    public bool IsPrivate => false;

    public long Size => _queueSlot is not null
        ? MbToBytes(ParseDouble(_queueSlot.Mb))
        : _historySlot?.Bytes ?? 0;

    public double CompletionPercentage
    {
        get
        {
            if (_queueSlot is not null)
            {
                return ParseDouble(_queueSlot.Percentage);
            }

            // History items are terminal: 100% for a completed job, 0% for a failed one.
            return string.Equals(_historySlot?.Status, "Completed", StringComparison.OrdinalIgnoreCase) ? 100.0 : 0.0;
        }
    }

    public long DownloadedBytes
    {
        get
        {
            if (_queueSlot is not null)
            {
                double totalMb = ParseDouble(_queueSlot.Mb);
                double leftMb = ParseDouble(_queueSlot.MbLeft);
                return MbToBytes(Math.Max(0, totalMb - leftMb));
            }

            return _historySlot?.Bytes ?? 0;
        }
    }

    public long DownloadSpeed => _queueSlot is not null
        ? (long)(ParseDouble(_queueSlot.KbPerSec) * 1024)
        : 0;

    /// <inheritdoc/>
    /// <remarks>Usenet has no seed/leech ratio; always 0.</remarks>
    public double Ratio => 0;

    /// <inheritdoc/>
    /// <remarks>Usenet has no peers/seeders; always null.</remarks>
    public int? SeederCount => null;

    public long Eta => _queueSlot is not null ? ParseTimeLeft(_queueSlot.TimeLeft) : 0;

    /// <inheritdoc/>
    /// <remarks>Usenet jobs are not seeded after completion; always 0.</remarks>
    public long SeedingTimeSeconds => 0;

    public DateTime? LastActivityTime => _historySlot is { Completed: > 0 }
        ? DateTimeOffset.FromUnixTimeSeconds(_historySlot.Completed).UtcDateTime
        : null;

    public string? Category
    {
        get => _queueSlot?.Category ?? _historySlot?.Category;
        set
        {
            if (_queueSlot is not null)
            {
                _queueSlot.Category = value;
            }
        }
    }

    public string SavePath => _queueSlot?.Storage ?? _historySlot?.Storage ?? string.Empty;

    /// <inheritdoc/>
    /// <remarks>Usenet has no trackers; always empty.</remarks>
    public IReadOnlyList<string> TrackerDomains => [];

    /// <inheritdoc/>
    /// <remarks>SABnzbd exposes no per-job tags/labels beyond category; always empty.</remarks>
    public IReadOnlyList<string> Tags => [];

    /// <summary>
    /// True for a queue item that is still actively downloading. Items in a post-processing
    /// state (checking/verifying/repairing/extracting/moving/running script), paused items, and
    /// anything in history are not considered downloading.
    /// </summary>
    public bool IsDownloading()
    {
        if (_historySlot is not null)
        {
            return false;
        }

        return string.Equals(_queueSlot?.Status, "Downloading", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Best-effort stall detection: a queue item is stalled when it is in the Downloading state
    /// but reports zero throughput and no post-processing activity is in progress.
    /// </summary>
    public bool IsStalled()
    {
        if (_queueSlot is null)
        {
            return false;
        }

        if (PostProcessingStates.Contains(_queueSlot.Status))
        {
            return false;
        }

        return IsDownloading() && DownloadSpeed <= 0;
    }

    public bool IsIgnored(IReadOnlyList<string> ignoredDownloads)
    {
        if (ignoredDownloads.Count == 0)
        {
            return false;
        }

        foreach (string pattern in ignoredDownloads)
        {
            if (Hash.Equals(pattern, StringComparison.InvariantCultureIgnoreCase))
            {
                return true;
            }

            if (Category?.Equals(pattern, StringComparison.InvariantCultureIgnoreCase) is true)
            {
                return true;
            }
        }

        return false;
    }

    private static double ParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) ? result : 0;

    private static long MbToBytes(double mb) => (long)(mb * 1024 * 1024);

    /// <summary>
    /// Parses SABnzbd's "H:MM:SS" (or "MM:SS") timeleft string into total seconds.
    /// Returns 0 when the value is missing or unparsable.
    /// </summary>
    private static long ParseTimeLeft(string? timeLeft)
    {
        if (string.IsNullOrWhiteSpace(timeLeft))
        {
            return 0;
        }

        string[] parts = timeLeft.Split(':');
        long seconds = 0;

        foreach (string part in parts)
        {
            if (!long.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out long component))
            {
                return 0;
            }

            seconds = seconds * 60 + component;
        }

        return seconds;
    }
}
