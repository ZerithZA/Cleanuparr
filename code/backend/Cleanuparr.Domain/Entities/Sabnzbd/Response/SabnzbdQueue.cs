using System.Text.Json.Serialization;

namespace Cleanuparr.Domain.Entities.Sabnzbd.Response;

/// <summary>
/// The "queue" object returned by SABnzbd's mode=queue API call.
/// </summary>
public sealed record SabnzbdQueue
{
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("speed")]
    public string? Speed { get; init; }

    [JsonPropertyName("slots")]
    public List<SabnzbdQueueSlot> Slots { get; init; } = [];
}
