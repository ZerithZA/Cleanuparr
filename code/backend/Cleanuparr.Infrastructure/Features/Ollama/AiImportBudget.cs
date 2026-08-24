using Cleanuparr.Infrastructure.Features.Context;
using Cleanuparr.Persistence.Models.Configuration.QueueCleaner;
using Microsoft.Extensions.Logging;

namespace Cleanuparr.Infrastructure.Features.Ollama;

/// <summary>
/// Process-global implementation of <see cref="IAiImportBudget"/>. Registered as a singleton:
/// its per-tick stopwatch and breaker counters must survive across the scoped services created
/// for each QueueCleaner tick. QueueCleaner ticks never overlap
/// (<c>[DisallowConcurrentExecution]</c> on <c>GenericJob&lt;T&gt;</c>), so no locking is
/// required for the tick-budget stopwatch or the breaker counters.
/// </summary>
public sealed class AiImportBudget : IAiImportBudget
{
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AiImportBudget> _logger;

    private long _tickStartTimestamp;
    private bool _tickStarted;

    private int _consecutiveFailures;
    private DateTimeOffset _breakerOpenedUntil;
    private bool _breakerOpen;

    public AiImportBudget(TimeProvider timeProvider, ILogger<AiImportBudget> logger)
    {
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public void StartTick()
    {
        _tickStartTimestamp = _timeProvider.GetTimestamp();
        _tickStarted = true;
    }

    public bool CanCallOllama()
    {
        AiImportConfig config = ContextProvider.Get<QueueCleanerConfig>().AiImport;

        if (_breakerOpen)
        {
            if (_timeProvider.GetUtcNow() < _breakerOpenedUntil)
            {
                return false;
            }

            // Cooldown elapsed: allow a probe call through. The breaker only fully closes on
            // RecordSuccess(); a failed probe reopens it via RecordFailure().
            _breakerOpen = false;
        }

        if (!_tickStarted)
        {
            return true;
        }

        TimeSpan elapsed = _timeProvider.GetElapsedTime(_tickStartTimestamp);
        return elapsed < TimeSpan.FromSeconds(config.TickBudgetSeconds);
    }

    public void RecordSuccess()
    {
        _consecutiveFailures = 0;

        if (_breakerOpen)
        {
            _breakerOpen = false;
            _logger.LogInformation("AI import circuit breaker closed after a successful Ollama call");
        }
    }

    public void RecordFailure()
    {
        AiImportConfig config = ContextProvider.Get<QueueCleanerConfig>().AiImport;

        _consecutiveFailures++;

        if (_consecutiveFailures < config.BreakerFailureThreshold)
        {
            return;
        }

        _breakerOpen = true;
        _breakerOpenedUntil = _timeProvider.GetUtcNow() + TimeSpan.FromMinutes(config.BreakerCooldownMinutes);

        _logger.LogWarning(
            "AI import circuit breaker opened after {ConsecutiveFailures} consecutive Ollama failures; " +
            "AI-assisted import disabled for {CooldownMinutes} minutes",
            _consecutiveFailures,
            config.BreakerCooldownMinutes);
    }

    public void Reset()
    {
        _consecutiveFailures = 0;
        _breakerOpen = false;

        _logger.LogInformation("AI import circuit breaker manually reset");
    }
}
