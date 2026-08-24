using System.Text.Json.Serialization;

namespace Cleanuparr.Infrastructure.Features.Ollama;

/// <summary>
/// The declared property schema of <see cref="OllamaResponseFormat"/>.
/// </summary>
public sealed class OllamaResponseFormatProperties
{
    [JsonPropertyName("match")]
    public required OllamaResponseFormatProperty Match { get; init; }

    [JsonPropertyName("confidence")]
    public required OllamaResponseFormatProperty Confidence { get; init; }

    [JsonPropertyName("reasoning")]
    public required OllamaResponseFormatProperty Reasoning { get; init; }
}
