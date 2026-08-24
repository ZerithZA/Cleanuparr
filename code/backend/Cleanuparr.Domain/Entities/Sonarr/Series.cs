namespace Cleanuparr.Domain.Entities.Sonarr;

/// <summary>
/// A series in Sonarr or Whisparr v2.
/// </summary>
public sealed record Series
{
    /// <summary>
    /// The ID of the series.
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// The name of the series.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Alternate titles/aliases Sonarr knows for the series, used to widen AI-assisted
    /// release-title matching (see <c>IOllamaClient.ClassifyAsync</c>'s <c>seriesAliases</c>).
    /// </summary>
    public List<SeriesAlternateTitle> AlternateTitles { get; init; } = [];
}
