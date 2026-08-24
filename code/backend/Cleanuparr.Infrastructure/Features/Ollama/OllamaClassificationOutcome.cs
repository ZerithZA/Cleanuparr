namespace Cleanuparr.Infrastructure.Features.Ollama;

/// <summary>
/// The outcome of a single <see cref="IOllamaClient"/> classification attempt.
/// Distinct from the higher-level <c>AiImportOutcome</c> (Step 5) - this enum only describes
/// whether the Ollama call itself produced a usable, schema-valid classification.
/// </summary>
public enum OllamaClassificationOutcome
{
    /// <summary>A schema-valid classification was returned.</summary>
    Success,

    /// <summary>The call was skipped without contacting Ollama (tick budget exhausted or breaker open).</summary>
    SkippedByBudget,

    /// <summary>The call timed out or failed at the transport level.</summary>
    TransportFailure,

    /// <summary>Ollama responded, but the response failed schema validation.</summary>
    InvalidResponse,
}
