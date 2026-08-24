namespace Cleanuparr.Infrastructure.Features.Ollama;

/// <summary>
/// Client for classifying whether a release title matches a series, via a locally/LAN-hosted
/// Ollama instance using structured JSON output.
/// </summary>
public interface IOllamaClient
{
    /// <summary>
    /// Classifies whether <paramref name="releaseTitle"/> plausibly matches
    /// <paramref name="seriesTitle"/> (and any of <paramref name="seriesAliases"/>).
    /// </summary>
    /// <param name="releaseTitle">The release title to classify.</param>
    /// <param name="seriesTitle">The title of the series the release was assigned to.</param>
    /// <param name="seriesAliases">Alternate titles/aliases known for the series.</param>
    /// <param name="seriesFirstAiredYear">
    /// The year the series first aired, or <see langword="null"/> if unknown. Used as a
    /// discriminator against similarly-named entries from a different era of the same franchise.
    /// </param>
    /// <param name="expectedEpisodeTitle">
    /// The title of the specific episode the release is expected to be, or <see langword="null"/>
    /// if unavailable.
    /// </param>
    /// <param name="expectedEpisodeAirDate">
    /// The air date of the specific episode the release is expected to be, or
    /// <see langword="null"/> if unavailable.
    /// </param>
    /// <param name="seriesRuntimeMinutes">
    /// The series' typical episode runtime in minutes, or <see langword="null"/> if unknown. A
    /// secondary discriminator against similarly-named entries with a different episode format.
    /// </param>
    /// <param name="expectedSeasonNumber">
    /// The season number of the specific episode the release is expected to be, or
    /// <see langword="null"/> if unavailable.
    /// </param>
    /// <param name="expectedEpisodeNumber">
    /// The season-relative episode number of the specific episode the release is expected to be,
    /// or <see langword="null"/> if unavailable.
    /// </param>
    /// <param name="expectedAbsoluteEpisodeNumber">
    /// The absolute (continuous, cross-season) episode number of the specific episode the
    /// release is expected to be, or <see langword="null"/> if Sonarr has no absolute numbering
    /// for it. Some releases (commonly anime) label episodes by absolute number instead of
    /// season/episode, which can diverge significantly from <paramref name="expectedSeasonNumber"/>/
    /// <paramref name="expectedEpisodeNumber"/> for multi-season shows.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <remarks>
    /// Honors the per-tick AI time budget and circuit breaker (<see cref="IAiImportBudget"/>):
    /// the call may be skipped without contacting Ollama at all. The deadline for the Ollama
    /// call itself is enforced by a per-request <see cref="CancellationTokenSource"/>, not by
    /// the HttpClient's own timeout (see the plan's "Timeout Mechanism" section).
    /// </remarks>
    Task<OllamaClassificationResponse> ClassifyAsync(
        string releaseTitle,
        string seriesTitle,
        IReadOnlyList<string> seriesAliases,
        int? seriesFirstAiredYear,
        string? expectedEpisodeTitle,
        DateOnly? expectedEpisodeAirDate,
        int? seriesRuntimeMinutes,
        int? expectedSeasonNumber,
        int? expectedEpisodeNumber,
        int? expectedAbsoluteEpisodeNumber,
        CancellationToken cancellationToken);
}
