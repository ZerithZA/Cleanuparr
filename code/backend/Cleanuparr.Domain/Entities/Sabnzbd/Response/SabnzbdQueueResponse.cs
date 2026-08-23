using System.Text.Json.Serialization;

namespace Cleanuparr.Domain.Entities.Sabnzbd.Response;

/// <summary>
/// The top-level response envelope for SABnzbd's mode=queue API call.
/// </summary>
public sealed record SabnzbdQueueResponse
{
    [JsonPropertyName("queue")]
    public SabnzbdQueue? Queue { get; init; }
}
