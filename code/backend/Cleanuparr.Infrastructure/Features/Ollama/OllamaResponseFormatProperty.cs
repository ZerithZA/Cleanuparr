using System.Text.Json.Serialization;

namespace Cleanuparr.Infrastructure.Features.Ollama;

/// <summary>
/// A single property entry (type only) within <see cref="OllamaResponseFormat"/>.
/// </summary>
public sealed class OllamaResponseFormatProperty
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }
}
