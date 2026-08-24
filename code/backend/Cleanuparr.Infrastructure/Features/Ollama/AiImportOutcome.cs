namespace Cleanuparr.Infrastructure.Features.Ollama;

/// <summary>
/// The outcome of <c>IArrClient.TryAiAssistedImportAsync</c> for a single queue record.
/// </summary>
public enum AiImportOutcome
{
    /// <summary>
    /// MUST remain the zero member. IArrClient is a plain interface with no default interface
    /// methods, so a Substitute.For&lt;IArrClient&gt;() mock does not inherit ArrClient's
    /// Skipped-returning base implementation; NSubstitute auto-returns default(AiImportOutcome)
    /// for the unstubbed member. Pinning Skipped = 0 is what keeps the repo's 115 IArrClient-shaped
    /// mock sites inert. Reordering this enum silently changes the behaviour of every test that
    /// does not stub TryAiAssistedImportAsync. Pinned by AC-41; see also AC-42, AC-43.
    /// </summary>
    Skipped = 0,

    /// <summary>
    /// The AI path did not import the record; normal queue-cleaner processing
    /// (<c>ShouldRemoveFromQueue</c>) continues as if the AI path did not exist.
    /// </summary>
    FallThrough = 1,

    /// <summary>
    /// The AI path issued a manual-import command for the record; the caller should treat the
    /// record as handled and skip the normal failed-import strike/removal flow for this tick.
    /// </summary>
    Imported = 2,
}
