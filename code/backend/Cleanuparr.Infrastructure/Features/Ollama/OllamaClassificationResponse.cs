namespace Cleanuparr.Infrastructure.Features.Ollama;

/// <summary>
/// The result of calling <see cref="IOllamaClient.ClassifyAsync"/>: an outcome plus the
/// validated classification when the outcome is <see cref="OllamaClassificationOutcome.Success"/>.
/// </summary>
/// <param name="Outcome">What happened during the classification attempt.</param>
/// <param name="Result">The validated classification, present only when <paramref name="Outcome"/> is <see cref="OllamaClassificationOutcome.Success"/>.</param>
public sealed record OllamaClassificationResponse(OllamaClassificationOutcome Outcome, OllamaClassificationResult? Result = null);
