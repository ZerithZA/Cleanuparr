using System.Text.Json.Serialization;

namespace Cleanuparr.Domain.Entities.Sabnzbd.Response;

/// <summary>
/// A single item in the SABnzbd queue (mode=queue), representing an actively
/// downloading, queued, paused or otherwise in-progress NZB job.
/// </summary>
public sealed record SabnzbdQueueSlot
{
    [JsonPropertyName("nzo_id")]
    public string NzoId { get; init; } = string.Empty;

    [JsonPropertyName("filename")]
    public string Filename { get; init; } = string.Empty;

    [JsonPropertyName("cat")]
    public string? Category { get; set; }

    /// <summary>
    /// The job's current state, e.g. Downloading, Queued, Paused, Checking, Fetching,
    /// QuickCheck, Verifying, Repairing, Extracting, Moving, Running Script.
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// Completion percentage as reported by SABnzbd, as a numeric string (e.g. "42.5").
    /// </summary>
    [JsonPropertyName("percentage")]
    public string Percentage { get; init; } = "0";

    /// <summary>
    /// Total size of the job in megabytes, as a numeric string.
    /// </summary>
    [JsonPropertyName("mb")]
    public string Mb { get; init; } = "0";

    /// <summary>
    /// Remaining megabytes to download, as a numeric string.
    /// </summary>
    [JsonPropertyName("mbleft")]
    public string MbLeft { get; init; } = "0";

    /// <summary>
    /// Current download speed in KB/s, as a numeric string. Only meaningful for the first/active slot.
    /// </summary>
    [JsonPropertyName("kbpersec")]
    public string KbPerSec { get; init; } = "0";

    /// <summary>
    /// Estimated time left, formatted as "H:MM:SS" (or "0:00:00" when unknown).
    /// </summary>
    [JsonPropertyName("timeleft")]
    public string TimeLeft { get; init; } = string.Empty;

    /// <summary>
    /// The final storage path once the job completes, when known.
    /// </summary>
    [JsonPropertyName("storage")]
    public string? Storage { get; init; }

    /// <summary>
    /// Priority of the queue slot within the queue order.
    /// </summary>
    [JsonPropertyName("priority")]
    public string? Priority { get; init; }
}
