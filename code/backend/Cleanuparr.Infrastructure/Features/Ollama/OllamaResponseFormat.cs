using System.Text.Json.Serialization;

namespace Cleanuparr.Infrastructure.Features.Ollama;

/// <summary>
/// The JSON schema passed as the <c>format</c> field of an Ollama <c>/api/chat</c> request,
/// instructing the model to return structured output matching the declared shape.
/// </summary>
public sealed class OllamaResponseFormat
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "object";

    [JsonPropertyName("properties")]
    public required OllamaResponseFormatProperties Properties { get; init; }

    [JsonPropertyName("required")]
    public required IReadOnlyList<string> Required { get; init; }
}
