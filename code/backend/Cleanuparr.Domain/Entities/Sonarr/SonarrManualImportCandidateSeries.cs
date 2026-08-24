namespace Cleanuparr.Domain.Entities.Sonarr;

/// <summary>
/// The series Sonarr matched a manual-import candidate to, as nested under the candidate's
/// <c>series</c> field by <c>GET /api/v3/manualimport</c>.
/// </summary>
public sealed record SonarrManualImportCandidateSeries
{
    /// <summary>
    /// The ID of the series.
    /// </summary>
    public long Id { get; init; }
}
