using System.Text.Json.Serialization;

namespace Cleanuparr.Domain.Entities.Sabnzbd.Response;

/// <summary>
/// The "history" object returned by SABnzbd's mode=history API call.
/// </summary>
public sealed record SabnzbdHistory
{
    [JsonPropertyName("slots")]
    public List<SabnzbdHistorySlot> Slots { get; init; } = [];
}
