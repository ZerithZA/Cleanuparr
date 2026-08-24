using System.Text.Json.Serialization;

namespace Cleanuparr.Infrastructure.Features.Ollama;

/// <summary>
/// The top-level body returned by Ollama's <c>/api/chat</c> endpoint.
/// </summary>
public sealed class OllamaChatResponse
{
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("message")]
    public OllamaChatMessage? Message { get; init; }

    [JsonPropertyName("done")]
    public bool Done { get; init; }

    [JsonPropertyName("done_reason")]
    public string? DoneReason { get; init; }
}
