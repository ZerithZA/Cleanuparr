using System.Text.Json;

namespace Cleanuparr.Domain.Entities.Sonarr;

/// <summary>
/// One candidate file returned by Sonarr's manual-import candidate-list endpoint
/// (<c>GET /api/v3/manualimport?downloadId={id}</c>).
/// </summary>
/// <remarks>
/// <see cref="Quality"/> and <see cref="Languages"/> are captured as opaque JSON so they can be
/// echoed back verbatim into the manual-import command without this codebase modeling Sonarr's
/// full quality/language schema.
/// </remarks>
public sealed record SonarrManualImportCandidate
{
    /// <summary>
    /// The absolute path of the candidate file on disk.
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// The name of the folder that contains the candidate file.
    /// </summary>
    public string? FolderName { get; init; }

    /// <summary>
    /// The series that Sonarr matched the candidate to, or <see langword="null"/> if Sonarr
    /// could not resolve one. <c>GET /api/v3/manualimport</c> nests the matched series' ID under
    /// this field rather than as a top-level <c>seriesId</c> property on the candidate.
    /// </summary>
    public SonarrManualImportCandidateSeries? Series { get; init; }

    /// <summary>
    /// The episodes that Sonarr matched the candidate to.
    /// </summary>
    public List<SonarrManualImportEpisode> Episodes { get; init; } = [];

    /// <summary>
    /// The quality of the candidate, echoed back verbatim into the import command.
    /// </summary>
    public JsonElement Quality { get; init; }

    /// <summary>
    /// The languages of the candidate, echoed back verbatim into the import command.
    /// </summary>
    public JsonElement Languages { get; init; }

    /// <summary>
    /// The release group of the candidate, if known.
    /// </summary>
    public string? ReleaseGroup { get; init; }

    /// <summary>
    /// Bitwise indexer flags for the candidate.
    /// </summary>
    public int IndexerFlags { get; init; }

    /// <summary>
    /// The release type of the candidate, for example "singleEpisode".
    /// </summary>
    public string? ReleaseType { get; init; }

    /// <summary>
    /// The hash that the download client uses for the item.
    /// </summary>
    public string DownloadId { get; init; } = string.Empty;

    /// <summary>
    /// The reasons Sonarr would reject this candidate from automatic import, if any.
    /// </summary>
    public List<JsonElement>? Rejections { get; init; }
}
