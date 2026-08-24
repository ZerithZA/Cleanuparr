using Cleanuparr.Api.Jobs;
using Quartz;
using Shouldly;
using Xunit;

namespace Cleanuparr.Api.Tests.Jobs;

public sealed class GenericJobTests
{
    // AC-46: the per-process global AI-import tick budget and circuit breaker
    // (IAiImportBudget) are only well-defined because Quartz serialises QueueCleaner ticks.
    // This test pins that environmental assumption: if a future change removes
    // [DisallowConcurrentExecution] from GenericJob<T>, this test fails and the budget design
    // must be revisited (instance-scoped or ticket-based) rather than silently racing.
    [Fact]
    public void GenericJobOfT_CarriesDisallowConcurrentExecutionAttribute()
    {
        // Arrange
        Type openGenericType = typeof(GenericJob<>);

        // Act
        bool hasAttribute = Attribute.IsDefined(openGenericType, typeof(DisallowConcurrentExecutionAttribute));

        // Assert
        hasAttribute.ShouldBeTrue();
    }
}
