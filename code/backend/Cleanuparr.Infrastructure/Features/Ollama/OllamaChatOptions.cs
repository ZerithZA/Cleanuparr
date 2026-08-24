using System.Text.Json.Serialization;

namespace Cleanuparr.Infrastructure.Features.Ollama;

/// <summary>
/// Model sampling options for an Ollama <c>/api/chat</c> request.
/// </summary>
public sealed class OllamaChatOptions
{
    /// <summary>
    /// Zero temperature keeps the classifier deterministic for the same input.
    /// </summary>
    [JsonPropertyName("temperature")]
    public double Temperature { get; init; } = 0;
}
