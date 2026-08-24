namespace Cleanuparr.Domain.Entities.Sonarr;

public sealed record Episode
{
    public long Id { get; set; }

    public int EpisodeNumber { get; set; }

    public int SeasonNumber { get; set; }

    public long SeriesId { get; set; }

    /// <summary>
    /// The episode's title, as returned by <c>GET /api/v3/episode</c>'s <c>title</c> field.
    /// Used as additional context for AI-assisted release-title matching.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// The episode's air date, as returned by <c>GET /api/v3/episode</c>'s <c>airDate</c> field
    /// (an ISO 8601 date, no time component). Used alongside <see cref="Series.Year"/> as a
    /// discriminator in AI-assisted release-title matching.
    /// </summary>
    public DateOnly? AirDate { get; set; }

    /// <summary>
    /// The episode's absolute episode number (continuous numbering across seasons, as commonly
    /// used by anime releases), as returned by <c>GET /api/v3/episode</c>'s
    /// <c>absoluteEpisodeNumber</c> field. <see langword="null"/> when Sonarr has no absolute
    /// numbering for this episode (the common case for non-anime series). Used alongside
    /// <see cref="SeasonNumber"/>/<see cref="EpisodeNumber"/> in AI-assisted release-title
    /// matching, since some releases label episodes by absolute number instead of season/episode.
    /// </summary>
    public int? AbsoluteEpisodeNumber { get; set; }
}