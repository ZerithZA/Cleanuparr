namespace Cleanuparr.Infrastructure.Features.Ollama;

/// <summary>
/// Client for classifying whether a release title matches a series, via a locally/LAN-hosted
/// Ollama instance using structured JSON output.
/// </summary>
public interface IOllamaClient
{
    /// <summary>
    /// Classifies whether <paramref name="releaseTitle"/> plausibly matches
    /// <paramref name="seriesTitle"/> (and any of <paramref name="seriesAliases"/>).
    /// </summary>
    /// <remarks>
    /// Honors the per-tick AI time budget and circuit breaker (<see cref="IAiImportBudget"/>):
    /// the call may be skipped without contacting Ollama at all. The deadline for the Ollama
    /// call itself is enforced by a per-request <see cref="CancellationTokenSource"/>, not by
    /// the HttpClient's own timeout (see the plan's "Timeout Mechanism" section).
    /// </remarks>
    Task<OllamaClassificationResponse> ClassifyAsync(
        string releaseTitle,
        string seriesTitle,
        IReadOnlyList<string> seriesAliases,
        CancellationToken cancellationToken);
}
