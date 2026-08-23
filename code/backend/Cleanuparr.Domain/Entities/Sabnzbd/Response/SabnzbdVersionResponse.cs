using System.Text.Json.Serialization;

namespace Cleanuparr.Domain.Entities.Sabnzbd.Response;

/// <summary>
/// The response for SABnzbd's mode=version API call, used for health checks.
/// </summary>
public sealed record SabnzbdVersionResponse
{
    [JsonPropertyName("version")]
    public string? Version { get; init; }
}
