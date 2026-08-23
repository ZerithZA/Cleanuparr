using System.Text.Json.Serialization;

namespace Cleanuparr.Domain.Entities.Sabnzbd.Response;

/// <summary>
/// The response shape for simple SABnzbd API calls that only report success/failure,
/// such as mode=delete, mode=change_cat and mode=addurl.
/// </summary>
public sealed record SabnzbdBasicResponse
{
    [JsonPropertyName("status")]
    public bool Status { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("nzo_ids")]
    public List<string>? NzoIds { get; init; }
}
