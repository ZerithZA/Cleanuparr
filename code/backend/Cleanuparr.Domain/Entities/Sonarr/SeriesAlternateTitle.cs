namespace Cleanuparr.Domain.Entities.Sonarr;

/// <summary>
/// One alternate title/alias Sonarr knows for a series, as returned by
/// <c>GET /api/v3/series/{id}</c>'s <c>alternateTitles[]</c> field.
/// </summary>
public sealed record SeriesAlternateTitle
{
    /// <summary>
    /// The alternate title text.
    /// </summary>
    public string Title { get; init; } = string.Empty;
}
