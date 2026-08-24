using System.ComponentModel.DataAnnotations.Schema;
using Cleanuparr.Domain.Exceptions;

namespace Cleanuparr.Persistence.Models.Configuration.QueueCleaner;

[ComplexType]
public sealed record AiImportConfig
{
    public bool Enabled { get; init; }

    public int ConfidenceThreshold { get; init; } = 75;

    public int TimeoutSeconds { get; init; } = 8;

    public int TickBudgetSeconds { get; init; } = 30;

    public int BreakerFailureThreshold { get; init; } = 5;

    public int BreakerCooldownMinutes { get; init; } = 15;

    public int SkipBudget { get; init; } = 3;

    public int DecisionCacheTtlHours { get; init; } = 24;

    public string OllamaUrl { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public string TargetMessagePrefix { get; init; } = "Found matching series via grab history";

    public void Validate()
    {
        if (ConfidenceThreshold is < 50 or > 100)
        {
            throw new ValidationException("AI import confidence threshold must be between 50 and 100");
        }

        if (TimeoutSeconds is < 3 or > 30)
        {
            throw new ValidationException("AI import timeout seconds must be between 3 and 30");
        }

        if (string.IsNullOrWhiteSpace(TargetMessagePrefix?.Trim()))
        {
            throw new ValidationException("AI import target message prefix cannot be empty");
        }
    }
}
