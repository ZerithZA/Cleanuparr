using Cleanuparr.Infrastructure.Features.Context;
using Cleanuparr.Infrastructure.Features.Ollama;
using Cleanuparr.Persistence.Models.Configuration.QueueCleaner;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Cleanuparr.Infrastructure.Tests.Features.Ollama;

public sealed class AiImportBudgetTests
{
    private readonly FakeTimeProvider _timeProvider;
    private readonly AiImportBudget _budget;

    public AiImportBudgetTests()
    {
        _timeProvider = new FakeTimeProvider();
        _budget = new AiImportBudget(_timeProvider, Substitute.For<ILogger<AiImportBudget>>());
    }

    private static void SetConfig(int tickBudgetSeconds = 30, int breakerFailureThreshold = 5, int breakerCooldownMinutes = 15) =>
        ContextProvider.Set(new QueueCleanerConfig
        {
            AiImport = new AiImportConfig
            {
                TickBudgetSeconds = tickBudgetSeconds,
                BreakerFailureThreshold = breakerFailureThreshold,
                BreakerCooldownMinutes = breakerCooldownMinutes,
            },
        });

    // AC-35: with a wedged Ollama and N candidate records in one tick, total AI wall-clock time
    // is bounded by TickBudgetSeconds, independent of N. Asserted deterministically: once the
    // elapsed time since StartTick() exceeds TickBudgetSeconds, CanCallOllama() returns false for
    // every subsequent candidate in the tick (N = 50), regardless of how many have already run.
    [Fact]
    public void CanCallOllama_TickBudgetExceeded_ReturnsFalseForAllSubsequentCandidates()
    {
        // Arrange
        SetConfig(tickBudgetSeconds: 30);
        _budget.StartTick();
        _timeProvider.Advance(TimeSpan.FromSeconds(31));

        // Act & Assert — N = 50 candidates all denied once the budget is exceeded.
        for (int i = 0; i < 50; i++)
        {
            _budget.CanCallOllama().ShouldBeFalse();
        }
    }

    [Fact]
    public void CanCallOllama_WithinTickBudget_ReturnsTrue()
    {
        // Arrange
        SetConfig(tickBudgetSeconds: 30);
        _budget.StartTick();
        _timeProvider.Advance(TimeSpan.FromSeconds(10));

        // Act
        bool result = _budget.CanCallOllama();

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void CanCallOllama_TickNeverStarted_ReturnsTrue()
    {
        // Arrange
        SetConfig();

        // Act
        bool result = _budget.CanCallOllama();

        // Assert — StartTick() is called once per QueueCleaner tick before any candidate is
        // evaluated; if it was somehow never called, the budget must not incorrectly deny.
        result.ShouldBeTrue();
    }

    // AC-36: after BreakerFailureThreshold (default 5) consecutive transport failures/timeouts,
    // the breaker opens: subsequent calls return false (zero Ollama calls) for
    // BreakerCooldownMinutes (default 15).
    [Fact]
    public void CanCallOllama_AfterThresholdConsecutiveFailures_BreakerOpensAndDeniesCalls()
    {
        // Arrange
        SetConfig(breakerFailureThreshold: 5, breakerCooldownMinutes: 15);

        for (int i = 0; i < 5; i++)
        {
            _budget.RecordFailure();
        }

        // Act
        bool result = _budget.CanCallOllama();

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void CanCallOllama_BelowFailureThreshold_BreakerStaysClosedAndAllowsCalls()
    {
        // Arrange
        SetConfig(breakerFailureThreshold: 5, breakerCooldownMinutes: 15);

        for (int i = 0; i < 4; i++)
        {
            _budget.RecordFailure();
        }

        // Act
        bool result = _budget.CanCallOllama();

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void CanCallOllama_BreakerOpen_RemainsClosedUntilCooldownElapses()
    {
        // Arrange
        SetConfig(breakerFailureThreshold: 5, breakerCooldownMinutes: 15);
        for (int i = 0; i < 5; i++)
        {
            _budget.RecordFailure();
        }
        _budget.CanCallOllama().ShouldBeFalse();

        // Act — advance less than the cooldown.
        _timeProvider.Advance(TimeSpan.FromMinutes(14));

        // Assert
        _budget.CanCallOllama().ShouldBeFalse();
    }

    [Fact]
    public void CanCallOllama_BreakerOpen_AllowsProbeCallAfterCooldownElapses()
    {
        // Arrange
        SetConfig(breakerFailureThreshold: 5, breakerCooldownMinutes: 15);
        for (int i = 0; i < 5; i++)
        {
            _budget.RecordFailure();
        }
        _budget.CanCallOllama().ShouldBeFalse();

        // Act — advance past the cooldown.
        _timeProvider.Advance(TimeSpan.FromMinutes(16));

        // Assert — the breaker allows a probe call through once the cooldown elapses.
        _budget.CanCallOllama().ShouldBeTrue();
    }

    // AC-37: any successful Ollama response closes the breaker and resets the failure counter.
    [Fact]
    public void RecordSuccess_ResetsConsecutiveFailureCounter()
    {
        // Arrange
        SetConfig(breakerFailureThreshold: 5, breakerCooldownMinutes: 15);
        for (int i = 0; i < 4; i++)
        {
            _budget.RecordFailure();
        }

        // Act — a success resets the counter, so 4 more failures should not open the breaker.
        _budget.RecordSuccess();
        for (int i = 0; i < 4; i++)
        {
            _budget.RecordFailure();
        }

        // Assert
        _budget.CanCallOllama().ShouldBeTrue();
    }

    [Fact]
    public void RecordSuccess_ClosesAnOpenBreakerImmediately()
    {
        // Arrange — open the breaker.
        SetConfig(breakerFailureThreshold: 5, breakerCooldownMinutes: 15);
        for (int i = 0; i < 5; i++)
        {
            _budget.RecordFailure();
        }
        _budget.CanCallOllama().ShouldBeFalse();

        // Advance past cooldown to allow a probe through, then have it succeed.
        _timeProvider.Advance(TimeSpan.FromMinutes(16));
        _budget.CanCallOllama().ShouldBeTrue();

        // Act
        _budget.RecordSuccess();

        // Assert — breaker fully closed; further calls are allowed without waiting on cooldown.
        _budget.CanCallOllama().ShouldBeTrue();
    }
}
