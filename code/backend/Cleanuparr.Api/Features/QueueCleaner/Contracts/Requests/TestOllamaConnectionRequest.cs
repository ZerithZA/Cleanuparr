using System.ComponentModel.DataAnnotations;

namespace Cleanuparr.Api.Features.QueueCleaner.Contracts.Requests;

/// <summary>
/// Request body for testing connectivity to an Ollama server before the AI-assisted import
/// configuration is saved.
/// </summary>
public sealed record TestOllamaConnectionRequest
{
    [Required]
    public required string OllamaUrl { get; init; }
}
