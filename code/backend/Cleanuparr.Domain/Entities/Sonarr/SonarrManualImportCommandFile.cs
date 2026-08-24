using System.Text.Json;

namespace Cleanuparr.Domain.Entities.Sonarr;

/// <summary>
/// One file entry of a <see cref="SonarrManualImportCommand"/>.
/// </summary>
public sealed record SonarrManualImportCommandFile
{
    /// <summary>
    /// The absolute path of the file on disk.
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// The name of the folder that contains the file.
    /// </summary>
    public string? FolderName { get; init; }

    /// <summary>
    /// The ID of the series to import the file into.
    /// </summary>
    public long SeriesId { get; init; }

    /// <summary>
    /// The IDs of the episodes to import the file into.
    /// </summary>
    public List<long> EpisodeIds { get; init; } = [];

    /// <summary>
    /// The quality of the file, echoed back from the candidate-list response.
    /// </summary>
    public JsonElement Quality { get; init; }

    /// <summary>
    /// The languages of the file, echoed back from the candidate-list response.
    /// </summary>
    public JsonElement Languages { get; init; }

    /// <summary>
    /// The release type of the file, for example "singleEpisode".
    /// </summary>
    public string? ReleaseType { get; init; }

    /// <summary>
    /// The hash that the download client uses for the item.
    /// </summary>
    public string DownloadId { get; init; } = string.Empty;
}
