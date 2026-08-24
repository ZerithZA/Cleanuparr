namespace Cleanuparr.Infrastructure.Features.Ollama;

/// <summary>
/// The validated, scale-normalised result of an Ollama classification call.
/// </summary>
/// <param name="Match">Whether the model judged the release title to match the series.</param>
/// <param name="Confidence">Confidence on a 0-100 percentage scale (see AC-30b).</param>
/// <param name="Reasoning">The model's free-text justification, for logging only.</param>
public sealed record OllamaClassificationResult(bool Match, int Confidence, string Reasoning);
