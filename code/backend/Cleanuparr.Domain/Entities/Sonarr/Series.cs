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

    /// <summary>
    /// The year the series first aired, as returned by <c>GET /api/v3/series/{id}</c>'s
    /// <c>year</c> field. Used as a discriminator in AI-assisted release-title matching to tell
    /// apart similarly-named entries from different eras of the same franchise (e.g. a 2002
    /// series vs. an unrelated 2026 remake sharing a title/alias).
    /// </summary>
    public int Year { get; init; }

    /// <summary>
    /// The series' typical episode runtime in minutes, as returned by
    /// <c>GET /api/v3/series/{id}</c>'s <c>runtime</c> field. A secondary discriminator in
    /// AI-assisted release-title matching against similarly-named entries with a different
    /// episode format (e.g. a full-length series vs. a short-form spin-off).
    /// </summary>
    public int Runtime { get; init; }
}
