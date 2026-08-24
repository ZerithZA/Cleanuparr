using System.Text.Json.Serialization;

namespace Cleanuparr.Infrastructure.Features.Ollama;

/// <summary>
/// The top-level body returned by Ollama's <c>/api/tags</c> endpoint, used as a lightweight
/// connectivity/model-listing probe (distinct from the classification-specific <c>/api/chat</c>
/// call made by <see cref="OllamaClient"/>).
/// </summary>
public sealed class OllamaTagsResponse
{
    [JsonPropertyName("models")]
    public List<OllamaTagsModel>? Models { get; init; }
}
