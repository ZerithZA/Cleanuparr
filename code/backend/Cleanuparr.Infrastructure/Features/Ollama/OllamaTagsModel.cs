using System.Text.Json.Serialization;

namespace Cleanuparr.Infrastructure.Features.Ollama;

/// <summary>
/// A single model entry returned by Ollama's <c>/api/tags</c> endpoint.
/// </summary>
public sealed class OllamaTagsModel
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }
}
