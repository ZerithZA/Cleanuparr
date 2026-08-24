using System.Text.Json.Serialization;

namespace Cleanuparr.Infrastructure.Features.Ollama;

/// <summary>
/// The body of a POST to Ollama's <c>/api/chat</c> endpoint, requesting structured JSON output.
/// </summary>
public sealed class OllamaChatRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("messages")]
    public required IReadOnlyList<OllamaChatMessage> Messages { get; init; }

    [JsonPropertyName("format")]
    public required OllamaResponseFormat Format { get; init; }

    [JsonPropertyName("stream")]
    public bool Stream { get; init; }

    [JsonPropertyName("options")]
    public required OllamaChatOptions Options { get; init; }
}
