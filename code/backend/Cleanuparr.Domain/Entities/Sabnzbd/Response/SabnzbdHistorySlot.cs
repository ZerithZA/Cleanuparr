using System.Text.Json.Serialization;

namespace Cleanuparr.Domain.Entities.Sabnzbd.Response;

/// <summary>
/// A single item in the SABnzbd history (mode=history), representing a completed or failed NZB job.
/// </summary>
public sealed record SabnzbdHistorySlot
{
    [JsonPropertyName("nzo_id")]
    public string NzoId { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("category")]
    public string? Category { get; init; }

    /// <summary>
    /// The job's final state, e.g. Completed or Failed.
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// Total size of the job in bytes.
    /// </summary>
    [JsonPropertyName("bytes")]
    public long Bytes { get; init; }

    /// <summary>
    /// The final on-disk storage path for the completed job.
    /// </summary>
    [JsonPropertyName("storage")]
    public string? Storage { get; init; }

    /// <summary>
    /// Failure reason when <see cref="Status"/> is Failed.
    /// </summary>
    [JsonPropertyName("fail_message")]
    public string? FailMessage { get; init; }

    /// <summary>
    /// Unix timestamp of when the job completed.
    /// </summary>
    [JsonPropertyName("completed")]
    public long Completed { get; init; }
}
