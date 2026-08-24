using System.Text.Json.Serialization;

namespace Cleanuparr.Infrastructure.Features.Ollama;

/// <summary>
/// The DATA payload sent to Ollama as the "user" message content, serialised as JSON.
/// This is never string-concatenated into the instruction/system prompt - it is always
/// delivered as a separate, clearly-scoped data field so that untrusted release titles and
/// series aliases cannot be interpreted as instructions (AC-28, AC-29).
/// </summary>
public sealed class OllamaClassificationRequestData
{
    [JsonPropertyName("releaseTitle")]
    public required string ReleaseTitle { get; init; }

    [JsonPropertyName("seriesTitle")]
    public required string SeriesTitle { get; init; }

    [JsonPropertyName("seriesAliases")]
    public required IReadOnlyList<string> SeriesAliases { get; init; }

    /// <summary>
    /// The year the target series first aired, or <see langword="null"/> if unknown. Used as a
    /// discriminator against similarly-named entries from a different era of the same franchise.
    /// </summary>
    [JsonPropertyName("seriesFirstAiredYear")]
    public int? SeriesFirstAiredYear { get; init; }

    /// <summary>
    /// The title of the specific episode the release is expected to be, or <see langword="null"/>
    /// if unavailable.
    /// </summary>
    [JsonPropertyName("expectedEpisodeTitle")]
    public string? ExpectedEpisodeTitle { get; init; }

    /// <summary>
    /// The air date of the specific episode the release is expected to be, as an ISO 8601 date
    /// string, or <see langword="null"/> if unavailable.
    /// </summary>
    [JsonPropertyName("expectedEpisodeAirDate")]
    public string? ExpectedEpisodeAirDate { get; init; }

    /// <summary>
    /// The series' typical episode runtime in minutes, or <see langword="null"/> if unknown.
    /// A secondary discriminator against similarly-named entries with a different episode
    /// format (for example, a full-length series versus a short-form spin-off).
    /// </summary>
    [JsonPropertyName("seriesRuntimeMinutes")]
    public int? SeriesRuntimeMinutes { get; init; }

    /// <summary>
    /// The season number of the specific episode the release is expected to be, or
    /// <see langword="null"/> if unavailable.
    /// </summary>
    [JsonPropertyName("expectedSeasonNumber")]
    public int? ExpectedSeasonNumber { get; init; }

    /// <summary>
    /// The season-relative episode number of the specific episode the release is expected to
    /// be, or <see langword="null"/> if unavailable.
    /// </summary>
    [JsonPropertyName("expectedEpisodeNumber")]
    public int? ExpectedEpisodeNumber { get; init; }

    /// <summary>
    /// The absolute (continuous, cross-season) episode number of the specific episode the
    /// release is expected to be, or <see langword="null"/> if Sonarr has no absolute numbering
    /// for it. Some releases (commonly anime) label episodes by absolute number instead of
    /// season/episode, which can diverge significantly from <see cref="ExpectedSeasonNumber"/>/
    /// <see cref="ExpectedEpisodeNumber"/> for multi-season shows.
    /// </summary>
    [JsonPropertyName("expectedAbsoluteEpisodeNumber")]
    public int? ExpectedAbsoluteEpisodeNumber { get; init; }
}
