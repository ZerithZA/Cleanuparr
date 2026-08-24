using Cleanuparr.Persistence.Models.Configuration.QueueCleaner;
using Shouldly;
using Xunit;
using ValidationException = Cleanuparr.Domain.Exceptions.ValidationException;

namespace Cleanuparr.Persistence.Tests.Models.Configuration.QueueCleaner;

public sealed class AiImportConfigTests
{
    #region Default Values

    [Fact]
    public void Enabled_DefaultsToFalse()
    {
        var config = new AiImportConfig();

        config.Enabled.ShouldBeFalse();
    }

    [Fact]
    public void ConfidenceThreshold_DefaultsTo75()
    {
        var config = new AiImportConfig();

        config.ConfidenceThreshold.ShouldBe(75);
    }

    [Fact]
    public void TimeoutSeconds_DefaultsTo8()
    {
        var config = new AiImportConfig();

        config.TimeoutSeconds.ShouldBe(8);
    }

    [Fact]
    public void TickBudgetSeconds_DefaultsTo30()
    {
        var config = new AiImportConfig();

        config.TickBudgetSeconds.ShouldBe(30);
    }

    [Fact]
    public void BreakerFailureThreshold_DefaultsTo5()
    {
        var config = new AiImportConfig();

        config.BreakerFailureThreshold.ShouldBe(5);
    }

    [Fact]
    public void BreakerCooldownMinutes_DefaultsTo15()
    {
        var config = new AiImportConfig();

        config.BreakerCooldownMinutes.ShouldBe(15);
    }

    [Fact]
    public void SkipBudget_DefaultsTo3()
    {
        var config = new AiImportConfig();

        config.SkipBudget.ShouldBe(3);
    }

    [Fact]
    public void DecisionCacheTtlHours_DefaultsTo24()
    {
        var config = new AiImportConfig();

        config.DecisionCacheTtlHours.ShouldBe(24);
    }

    [Fact]
    public void TargetMessagePrefix_DefaultsToExpectedValue()
    {
        var config = new AiImportConfig();

        config.TargetMessagePrefix.ShouldBe("Found matching series via grab history");
    }

    #endregion

    #region Validate - Valid Configurations

    [Fact]
    public void Validate_WithDefaultConfig_DoesNotThrow()
    {
        var config = new AiImportConfig();

        Should.NotThrow(() => config.Validate());
    }

    #endregion

    #region Validate - ConfidenceThreshold Validation (AC-32)

    [Theory]
    [InlineData(50)]
    [InlineData(75)]
    [InlineData(100)]
    public void Validate_WithConfidenceThresholdInValidRange_DoesNotThrow(int threshold)
    {
        var config = new AiImportConfig
        {
            ConfidenceThreshold = threshold
        };

        Should.NotThrow(() => config.Validate());
    }

    [Theory]
    [InlineData(49)]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(200)]
    public void Validate_WithConfidenceThresholdOutOfRange_ThrowsValidationException(int threshold)
    {
        var config = new AiImportConfig
        {
            ConfidenceThreshold = threshold
        };

        var exception = Should.Throw<ValidationException>(() => config.Validate());
        exception.Message.ShouldBe("AI import confidence threshold must be between 50 and 100");
    }

    #endregion

    #region Validate - TimeoutSeconds Validation

    [Theory]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(30)]
    public void Validate_WithTimeoutSecondsInValidRange_DoesNotThrow(int timeoutSeconds)
    {
        var config = new AiImportConfig
        {
            TimeoutSeconds = timeoutSeconds
        };

        Should.NotThrow(() => config.Validate());
    }

    [Theory]
    [InlineData(2)]
    [InlineData(0)]
    [InlineData(31)]
    public void Validate_WithTimeoutSecondsOutOfRange_ThrowsValidationException(int timeoutSeconds)
    {
        var config = new AiImportConfig
        {
            TimeoutSeconds = timeoutSeconds
        };

        var exception = Should.Throw<ValidationException>(() => config.Validate());
        exception.Message.ShouldBe("AI import timeout seconds must be between 3 and 30");
    }

    #endregion

    #region Validate - TargetMessagePrefix Validation (AC-33b)

    [Fact]
    public void Validate_WithEmptyTargetMessagePrefix_ThrowsValidationException()
    {
        var config = new AiImportConfig
        {
            TargetMessagePrefix = ""
        };

        var exception = Should.Throw<ValidationException>(() => config.Validate());
        exception.Message.ShouldBe("AI import target message prefix cannot be empty");
    }

    [Fact]
    public void Validate_WithWhitespaceOnlyTargetMessagePrefix_ThrowsValidationException()
    {
        var config = new AiImportConfig
        {
            TargetMessagePrefix = "   "
        };

        var exception = Should.Throw<ValidationException>(() => config.Validate());
        exception.Message.ShouldBe("AI import target message prefix cannot be empty");
    }

    [Fact]
    public void Validate_WithNonEmptyTargetMessagePrefix_DoesNotThrow()
    {
        var config = new AiImportConfig
        {
            TargetMessagePrefix = "Found matching series via grab history"
        };

        Should.NotThrow(() => config.Validate());
    }

    #endregion
}
