namespace Cleanuparr.Domain.Entities.Sonarr;

/// <summary>
/// One episode matched to a manual-import candidate.
/// </summary>
public sealed record SonarrManualImportEpisode
{
    /// <summary>
    /// The ID of the episode.
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// Whether Sonarr already has a file for this episode.
    /// </summary>
    public bool HasFile { get; init; }
}
