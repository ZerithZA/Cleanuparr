namespace Cleanuparr.Infrastructure.Features.Ollama;

/// <summary>
/// Tracks the per-tick AI time budget and the in-pass circuit breaker for the AI-assisted
/// import feature. State is process-global (per the plan's "Per-Tick Budget and Circuit
/// Breaker" section) and relies on QueueCleaner ticks never overlapping
/// (<c>[DisallowConcurrentExecution]</c> on <c>GenericJob&lt;T&gt;</c>).
/// </summary>
public interface IAiImportBudget
{
    /// <summary>
    /// Starts (or restarts) the per-tick AI time budget stopwatch. Call once at the beginning
    /// of a QueueCleaner tick, before any AI-assisted import candidates are evaluated.
    /// </summary>
    void StartTick();

    /// <summary>
    /// Whether an Ollama call is currently allowed: the per-tick time budget has not been
    /// exceeded, and the circuit breaker is not open.
    /// </summary>
    bool CanCallOllama();

    /// <summary>
    /// Records that an Ollama call succeeded. Closes the circuit breaker and resets the
    /// consecutive-failure counter.
    /// </summary>
    void RecordSuccess();

    /// <summary>
    /// Records that an Ollama call failed at the transport level (error or timeout).
    /// Opens the circuit breaker once <c>BreakerFailureThreshold</c> consecutive failures
    /// have been recorded.
    /// </summary>
    void RecordFailure();
}
