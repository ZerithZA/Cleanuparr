using System.Text.Json.Serialization;

namespace Cleanuparr.Infrastructure.Features.Ollama;

/// <summary>
/// The DATA payload sent to Ollama as the "user" message content, serialised as JSON.
/// This is never string-concatenated into the instruction/system prompt - it is always
/// delivered as a separate, clearly-scoped data field so that untrusted release titles and
/// series aliases cannot be interpreted as instructions (AC-28, AC-29).
/// </summary>
public sealed class OllamaClassificationRequestData
{
    [JsonPropertyName("releaseTitle")]
    public required string ReleaseTitle { get; init; }

    [JsonPropertyName("seriesTitle")]
    public required string SeriesTitle { get; init; }

    [JsonPropertyName("seriesAliases")]
    public required IReadOnlyList<string> SeriesAliases { get; init; }
}
