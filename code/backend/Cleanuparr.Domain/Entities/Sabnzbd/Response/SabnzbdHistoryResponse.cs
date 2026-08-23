using System.Text.Json.Serialization;

namespace Cleanuparr.Domain.Entities.Sabnzbd.Response;

/// <summary>
/// The top-level response envelope for SABnzbd's mode=history API call.
/// </summary>
public sealed record SabnzbdHistoryResponse
{
    [JsonPropertyName("history")]
    public SabnzbdHistory? History { get; init; }
}
