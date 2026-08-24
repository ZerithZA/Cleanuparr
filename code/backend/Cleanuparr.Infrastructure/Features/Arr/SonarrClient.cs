using System.Runtime.CompilerServices;
using System.Text;
using Cleanuparr.Domain.Entities.Arr;
using Cleanuparr.Domain.Entities.Arr.Queue;
using Cleanuparr.Domain.Entities.Sonarr;
using Cleanuparr.Domain.Enums;
using Cleanuparr.Infrastructure.Features.Arr.Interfaces;
using Cleanuparr.Infrastructure.Features.Context;
using Cleanuparr.Infrastructure.Features.ItemStriker;
using Cleanuparr.Infrastructure.Features.Ollama;
using Cleanuparr.Infrastructure.Helpers;
using Cleanuparr.Infrastructure.Interceptors;
using Cleanuparr.Persistence.Models.Configuration.Arr;
using Cleanuparr.Persistence.Models.Configuration.QueueCleaner;
using Cleanuparr.Shared.Helpers;
using System.Text.Json;
using Cleanuparr.Infrastructure.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Series = Cleanuparr.Domain.Entities.Sonarr.Series;

namespace Cleanuparr.Infrastructure.Features.Arr;

public class SonarrClient : ArrClient, ISonarrClient
{
    private readonly IOllamaClient _ollamaClient;
    private readonly IAiImportBudget _aiImportBudget;
    private readonly IMemoryCache _cache;

    public SonarrClient(
        ILogger<SonarrClient> logger,
        IHttpClientFactory httpClientFactory,
        IStriker striker,
        IDryRunInterceptor dryRunInterceptor,
        IOllamaClient ollamaClient,
        IAiImportBudget aiImportBudget,
        IMemoryCache cache
    ) : base(logger, httpClientFactory, striker, dryRunInterceptor)
    {
        _ollamaClient = ollamaClient;
        _aiImportBudget = aiImportBudget;
        _cache = cache;
    }

    protected override string GetSystemStatusUrlPath()
    {
        return "/api/v3/system/status";
    }
    
    protected override string GetQueueUrlPath()
    {
        return "/api/v3/queue";
    }

    protected override string GetQueueUrlQuery(int page)
    {
        return $"page={page}&pageSize=200&includeUnknownSeriesItems=true&includeSeries=true&includeEpisode=true";
    }

    protected override string GetQueueDeleteUrlPath(long recordId)
    {
        return $"/api/v3/queue/{recordId}";
    }

    public override async Task<List<long>> SearchItemsAsync(ArrInstance arrInstance, HashSet<SearchItem>? items)
    {
        if (items?.Count is null or 0)
        {
            return [];
        }

        List<long> commandIds = [];

        UriBuilder uriBuilder = new(arrInstance.Url);
        uriBuilder.Path = $"{uriBuilder.Path.TrimEnd('/')}/api/v3/command";

        foreach (SonarrCommand command in GetSearchCommands(items.Cast<SeriesSearchItem>().ToHashSet()))
        {
            using HttpRequestMessage request = new(HttpMethod.Post, uriBuilder.Uri);
            request.Content = new StringContent(
                JsonSerializer.Serialize(command, CleanuparrJsonOptions.Outbound),
                Encoding.UTF8,
                "application/json"
            );
            SetApiKey(request, arrInstance.ApiKey);

            string? logContext = await ComputeCommandLogContextAsync(arrInstance, command, command.SearchType);

            try
            {
                HttpResponseMessage? response = await _dryRunInterceptor.InterceptAsync(() => SendRequestAsync(request));

                if (response is not null)
                {
                    long? commandId = await ReadCommandIdAsync(response);
                    response.Dispose();

                    if (commandId.HasValue)
                    {
                        commandIds.Add(commandId.Value);
                    }
                }

                _logger.LogInformation("{log}", GetSearchLog(command.SearchType, arrInstance.Url, command, true, logContext));
            }
            catch
            {
                _logger.LogError("{log}", GetSearchLog(command.SearchType, arrInstance.Url, command, false, logContext));
                throw;
            }
        }

        return commandIds;
    }

    public override bool HasContentId(QueueRecord record) => record.EpisodeId is not 0 && record.SeriesId is not 0;

    private static string GetSearchLog(
        SeriesSearchType searchType,
        Uri instanceUrl,
        SonarrCommand command,
        bool success,
        string? logContext
    )
    {
        string status = success ? "triggered" : "failed";
        
        return searchType switch
        {
            SeriesSearchType.Episode =>
                $"episodes search {status} | {instanceUrl} | {logContext ?? $"episode ids: {string.Join(',', command.EpisodeIds)}"}",
            SeriesSearchType.Season =>
                $"season search {status} | {instanceUrl} | {logContext ?? $"season: {command.SeasonNumber} series id: {command.SeriesId}"}",
            SeriesSearchType.Series => $"series search {status} | {instanceUrl} | {logContext ?? $"series id: {command.SeriesId}"}",
            _ => throw new ArgumentOutOfRangeException(nameof(searchType), searchType, null)
        };
    }

    private async Task<string?> ComputeCommandLogContextAsync(ArrInstance arrInstance, SonarrCommand command, SeriesSearchType searchType)
    {
        try
        {
            StringBuilder log = new();

            if (searchType is SeriesSearchType.Episode)
            {
                var episodes = await GetEpisodesAsync(arrInstance, command.EpisodeIds);

                if (episodes?.Count is null or 0)
                {
                    return null;
                }

                var seriesIds = episodes
                    .Select(x => x.SeriesId)
                    .Distinct()
                    .ToList();

                List<Series> series = [];

                foreach (long id in seriesIds)
                {
                    Series? show = await GetSeriesAsync(arrInstance, id);

                    if (show is null)
                    {
                        return null;
                    }

                    series.Add(show);
                }

                foreach (var group in command.EpisodeIds.GroupBy(id => episodes.First(x => x.Id == id).SeriesId))
                {
                    var show = series.First(x => x.Id == group.Key);
                    var episode = episodes
                        .Where(ep => group.Any(x => x == ep.Id))
                        .OrderBy(x => x.SeasonNumber)
                        .ThenBy(x => x.EpisodeNumber)
                        .Select(x => $"S{x.SeasonNumber.ToString().PadLeft(2, '0')}E{x.EpisodeNumber.ToString().PadLeft(2, '0')}")
                        .ToList();

                    log.Append($"[{show.Title} {string.Join(',', episode)}]");
                }
            }

            if (searchType is SeriesSearchType.Season)
            {
                Series? show = await GetSeriesAsync(arrInstance, command.SeriesId.Value);

                if (show is null)
                {
                    return null;
                }

                log.Append($"[{show.Title} season {command.SeasonNumber}]");
            }

            if (searchType is SeriesSearchType.Series)
            {
                Series? show = await GetSeriesAsync(arrInstance, command.SeriesId.Value);

                if (show is null)
                {
                    return null;
                }

                log.Append($"[{show.Title}]");
            }

            return log.ToString();
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "failed to compute log context");
        }

        return null;
    }

    public async IAsyncEnumerable<SearchableSeries> StreamAllSeriesAsync(
        ArrInstance arrInstance,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        UriBuilder uriBuilder = new(arrInstance.Url);
        uriBuilder.Path = $"{uriBuilder.Path.TrimEnd('/')}/api/v3/series";

        using HttpRequestMessage request = new(HttpMethod.Get, uriBuilder.Uri);
        SetApiKey(request, arrInstance.ApiKey);

        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await foreach (SearchableSeries series in JsonStreamReader.StreamArrayAsync<SearchableSeries>(stream, cancellationToken))
        {
            yield return series;
        }
    }

    public override async Task<List<Tag>> GetAllTagsAsync(ArrInstance arrInstance)
    {
        UriBuilder uriBuilder = new(arrInstance.Url);
        uriBuilder.Path = $"{uriBuilder.Path.TrimEnd('/')}/api/v3/tag";
        using HttpRequestMessage request = new(HttpMethod.Get, uriBuilder.Uri);
        SetApiKey(request, arrInstance.ApiKey);
        
        using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        
        return await DeserializeStreamAsync<List<Tag>>(response) ?? [];
    }

    public async Task<List<SearchableEpisode>> GetEpisodesAsync(ArrInstance arrInstance, long seriesId, CancellationToken cancellationToken = default)
    {
        UriBuilder uriBuilder = new(arrInstance.Url);
        uriBuilder.Path = $"{uriBuilder.Path.TrimEnd('/')}/api/v3/episode";
        uriBuilder.Query = $"seriesId={seriesId}";

        using HttpRequestMessage request = new(HttpMethod.Get, uriBuilder.Uri);
        SetApiKey(request, arrInstance.ApiKey);

        using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await DeserializeStreamAsync<List<SearchableEpisode>>(response, cancellationToken) ?? [];
    }

    public async Task<List<ArrEpisodeFile>> GetEpisodeFilesAsync(ArrInstance arrInstance, long seriesId, CancellationToken cancellationToken = default)
    {
        UriBuilder uriBuilder = new(arrInstance.Url);
        uriBuilder.Path = $"{uriBuilder.Path.TrimEnd('/')}/api/v3/episodefile";
        uriBuilder.Query = $"seriesId={seriesId}";

        using HttpRequestMessage request = new(HttpMethod.Get, uriBuilder.Uri);
        SetApiKey(request, arrInstance.ApiKey);

        using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await DeserializeStreamAsync<List<ArrEpisodeFile>>(response, cancellationToken) ?? [];
    }

    public async Task<List<ArrQualityProfile>> GetQualityProfilesAsync(ArrInstance arrInstance)
    {
        UriBuilder uriBuilder = new(arrInstance.Url);
        uriBuilder.Path = $"{uriBuilder.Path.TrimEnd('/')}/api/v3/qualityprofile";

        using HttpRequestMessage request = new(HttpMethod.Get, uriBuilder.Uri);
        SetApiKey(request, arrInstance.ApiKey);

        using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        return await DeserializeStreamAsync<List<ArrQualityProfile>>(response) ?? [];
    }

    public async Task<Dictionary<long, int>> GetEpisodeFileScoresAsync(ArrInstance arrInstance, List<long> episodeFileIds)
    {
        Dictionary<long, int> scores = new();

        // Batch in chunks of 100 to avoid 414 URI Too Long
        foreach (long[] batch in episodeFileIds.Chunk(100))
        {
            UriBuilder uriBuilder = new(arrInstance.Url);
            uriBuilder.Path = $"{uriBuilder.Path.TrimEnd('/')}/api/v3/episodefile";
            uriBuilder.Query = string.Join('&', batch.Select(id => $"episodeFileIds={id}"));

            using HttpRequestMessage request = new(HttpMethod.Get, uriBuilder.Uri);
            SetApiKey(request, arrInstance.ApiKey);

            using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            List<MediaFileScore> files = await DeserializeStreamAsync<List<MediaFileScore>>(response) ?? [];

            foreach (MediaFileScore file in files)
            {
                scores[file.Id] = file.CustomFormatScore;
            }
        }

        return scores;
    }

    private async Task<List<Episode>?> GetEpisodesAsync(ArrInstance arrInstance, List<long> episodeIds)
    {
        UriBuilder uriBuilder = new(arrInstance.Url);
        uriBuilder.Path = $"{uriBuilder.Path.TrimEnd('/')}/api/v3/episode";
        uriBuilder.Query = string.Join('&', episodeIds.Select(x => $"episodeIds={x}"));

        using HttpRequestMessage request = new(HttpMethod.Get, uriBuilder.Uri);
        SetApiKey(request, arrInstance.ApiKey);

        using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        return await DeserializeStreamAsync<List<Episode>>(response);
    }

    protected async Task<Series?> GetSeriesAsync(ArrInstance arrInstance, long seriesId)
    {
        UriBuilder uriBuilder = new(arrInstance.Url);
        uriBuilder.Path = $"{uriBuilder.Path.TrimEnd('/')}/api/v3/series/{seriesId}";

        using HttpRequestMessage request = new(HttpMethod.Get, uriBuilder.Uri);
        SetApiKey(request, arrInstance.ApiKey);

        using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        return await DeserializeStreamAsync<Series>(response);
    }

    /// <summary>
    /// Attempts an AI-assisted manual import for a queue record stuck with the AI-import target
    /// status message (<see cref="AiImportConfig.TargetMessagePrefix"/>).
    /// </summary>
    /// <remarks>
    /// Guard order is deliberate and load-bearing (plan Step 5c) - each guard is an early return
    /// of <see cref="AiImportOutcome.Skipped"/>, cheapest/most-general first:
    /// <list type="number">
    /// <item>Not a Sonarr instance.</item>
    /// <item>Concrete type is not exactly <see cref="SonarrClient"/> (blocks <c>WhisparrV2Client</c>
    /// and <c>SportarrClient</c>, which both derive from this class).</item>
    /// <item>The feature is disabled.</item>
    /// <item>The download is on a private tracker the config says to ignore
    /// (shared <see cref="ArrClient.ShouldIgnoreForPrivateTracker"/> helper, not a re-typed
    /// predicate).</item>
    /// <item>The record is not an AI candidate - message-prefix only
    /// (<see cref="ArrClient.HasAiTargetStatusMessage"/>). Deliberately does not inspect
    /// <see cref="QueueRecord.TrackedDownloadState"/> in any form: a state term was carried from an
    /// earlier iteration of this feature as a proxy for candidacy and was found, against a live
    /// capture, to be exactly inverted for the target record shape - it would have made the gate
    /// fire for 0% of real records. Do not reintroduce one (AC-48).</item>
    /// <item>Dry run is enabled (checked only after candidacy, so dry-run logging is limited to
    /// genuine candidates rather than firing for every non-candidate record).</item>
    /// <item>The per-tick AI time budget is exhausted or the circuit breaker is open
    /// (<see cref="IAiImportBudget.CanCallOllama"/>).</item>
    /// <item>An idempotency cache hit for this <c>DownloadId</c> + <c>instance.Url</c> (a prior
    /// <see cref="AiImportOutcome.Imported"/> outcome), or the consecutive-skip budget for this
    /// download has been exhausted.</item>
    /// <item>The series lookup (<see cref="GetSeriesAsync"/>) fails.</item>
    /// <item>A deterministic token-overlap pre-filter (<see cref="HasTokenOverlap"/>): the release
    /// title must share at least one significant token with the series title or one of its
    /// aliases. This is a cheap, defense-in-depth sanity check that runs before ever spending an
    /// Ollama call - it does not replace the classification, it only rules out releases with
    /// essentially zero textual relationship to the assigned series.</item>
    /// </list>
    /// Only once every guard above has passed does this method call Ollama. After classification,
    /// if <c>Confidence</c> is below <see cref="AiImportConfig.ConfidenceThreshold"/> the record
    /// falls through. Then - and only then, deliberately after classification rather than before it
    /// - if <see cref="QueueRecord.EpisodeHasFile"/> is <see langword="true"/> the classification is
    /// still logged (so real data accumulates on this record shape) but the import is suppressed and
    /// this method returns <see cref="AiImportOutcome.FallThrough"/>: Sonarr's behaviour for a
    /// manual import into an episode that already has a file was never established, so importing
    /// over an existing file on the strength of a non-deterministic classification is avoided by
    /// default (AC-50). Otherwise the manual import is attempted.
    /// </remarks>
    public override async Task<AiImportOutcome> TryAiAssistedImportAsync(ArrInstance instance, QueueRecord record, bool isPrivateDownload)
    {
        if (instance.ArrConfig.Type is not InstanceType.Sonarr)
        {
            return AiImportOutcome.Skipped;
        }

        if (GetType() != typeof(SonarrClient))
        {
            // Blocks WhisparrV2Client and SportarrClient, both of which derive from SonarrClient.
            return AiImportOutcome.Skipped;
        }

        QueueCleanerConfig queueCleanerConfig = ContextProvider.Get<QueueCleanerConfig>();
        AiImportConfig aiImportConfig = queueCleanerConfig.AiImport;

        if (!aiImportConfig.Enabled)
        {
            return AiImportOutcome.Skipped;
        }

        if (ShouldIgnoreForPrivateTracker(queueCleanerConfig, isPrivateDownload))
        {
            _logger.LogDebug("skip AI-assisted import | download is private | {name}", record.Title);
            return AiImportOutcome.Skipped;
        }

        if (!HasAiTargetStatusMessage(record, aiImportConfig.TargetMessagePrefix))
        {
            return AiImportOutcome.Skipped;
        }

        if (await _dryRunInterceptor.IsDryRunEnabled())
        {
            _logger.LogInformation(
                "DRY RUN: would have attempted an AI-assisted import | {downloadId} | {title}",
                record.DownloadId,
                record.Title
            );
            return AiImportOutcome.Skipped;
        }

        if (!_aiImportBudget.CanCallOllama())
        {
            _logger.LogDebug("skip AI-assisted import | tick budget exhausted or breaker open | {name}", record.Title);
            return AiImportOutcome.Skipped;
        }

        string decisionCacheKey = AiImportDecisionCacheKey(record.DownloadId, instance.Url);

        if (_cache.TryGetValue(decisionCacheKey, out AiImportOutcome cachedOutcome) && cachedOutcome is AiImportOutcome.Imported)
        {
            _logger.LogDebug("skip AI-assisted import | already imported this tick window | {name}", record.Title);
            return AiImportOutcome.Skipped;
        }

        string skipCounterKey = AiImportSkipCounterCacheKey(record.DownloadId, instance.Url);
        int consecutiveSkips = _cache.TryGetValue(skipCounterKey, out int existingSkips) ? existingSkips : 0;

        if (consecutiveSkips >= aiImportConfig.SkipBudget)
        {
            string skipBudgetWarnedKey = AiImportSkipBudgetWarnedCacheKey(record.DownloadId, instance.Url);

            if (!_cache.TryGetValue(skipBudgetWarnedKey, out bool _))
            {
                _logger.LogWarning(
                    "AI-assisted import skip budget exhausted | {skipBudget} consecutive skips | {downloadId} | {title}",
                    aiImportConfig.SkipBudget,
                    record.DownloadId,
                    record.Title
                );
                _cache.Set(skipBudgetWarnedKey, true, Constants.DefaultCacheEntryOptions);
            }

            return AiImportOutcome.Skipped;
        }

        Series? series = await GetSeriesAsync(instance, record.SeriesId);

        if (series is null)
        {
            _logger.LogDebug("skip AI-assisted import | series lookup failed | {name}", record.Title);
            RecordAiImportSkip(skipCounterKey, consecutiveSkips);
            return AiImportOutcome.Skipped;
        }

        if (!HasTokenOverlap(record.Title, series.Title, series.AlternateTitles))
        {
            _logger.LogInformation(
                "skip AI-assisted import | release title has no token overlap with series title/aliases | {title} | {seriesTitle}",
                record.Title,
                series.Title
            );
            RecordAiImportSkip(skipCounterKey, consecutiveSkips);
            return AiImportOutcome.Skipped;
        }

        List<Episode>? episodes = await GetEpisodesAsync(instance, [record.EpisodeId]);
        Episode? episode = episodes?.FirstOrDefault(x => x.Id == record.EpisodeId);

        if (episode is null)
        {
            _logger.LogDebug(
                "AI-assisted import episode lookup failed | falling back to series-level classification only | {name}",
                record.Title
            );
        }

        OllamaClassificationResponse response = await _ollamaClient.ClassifyAsync(
            record.Title,
            series.Title,
            series.AlternateTitles.Select(x => x.Title).ToList(),
            series.Year is not 0 ? series.Year : null,
            episode?.Title,
            episode?.AirDate,
            series.Runtime is not 0 ? series.Runtime : null,
            episode?.SeasonNumber,
            episode?.EpisodeNumber,
            episode?.AbsoluteEpisodeNumber,
            CancellationToken.None
        );

        if (response.Outcome is not OllamaClassificationOutcome.Success || response.Result is null)
        {
            RecordAiImportSkip(skipCounterKey, consecutiveSkips);
            return AiImportOutcome.Skipped;
        }

        OllamaClassificationResult result = response.Result;

        // Any successful classification response - matched or not - resets the consecutive-skip
        // counter: the skip budget only tracks Ollama unavailability, not classification outcome.
        _cache.Remove(skipCounterKey);
        _cache.Remove(AiImportSkipBudgetWarnedCacheKey(record.DownloadId, instance.Url));

        if (!result.Match || result.Confidence < aiImportConfig.ConfidenceThreshold)
        {
            _logger.LogInformation(
                "AI-assisted import classification did not meet the confidence threshold | match={match} confidence={confidence} reasoning={reasoning} | {downloadId} | {title}",
                result.Match,
                result.Confidence,
                result.Reasoning,
                record.DownloadId,
                record.Title
            );
            return AiImportOutcome.FallThrough;
        }

        if (record.EpisodeHasFile)
        {
            // Guard runs AFTER classification, deliberately: the classification is still logged so
            // real data accumulates on this record shape, but Sonarr's behaviour for a manual
            // import into an episode that already has a file was never established, so the import
            // itself is conservatively suppressed (AC-50).
            _logger.LogInformation(
                "AI-assisted import classification matched but the episode already has a file, so the import was not performed | match={match} confidence={confidence} reasoning={reasoning} | {downloadId} | {title}",
                result.Match,
                result.Confidence,
                result.Reasoning,
                record.DownloadId,
                record.Title
            );
            return AiImportOutcome.FallThrough;
        }

        bool imported = await TryManualImportAsync(instance, record);

        if (!imported)
        {
            return AiImportOutcome.FallThrough;
        }

        _cache.Set(decisionCacheKey, AiImportOutcome.Imported, TimeSpan.FromHours(aiImportConfig.DecisionCacheTtlHours));

        return AiImportOutcome.Imported;
    }

    /// <summary>
    /// A cheap, deterministic sanity check that <paramref name="releaseTitle"/> shares at least
    /// one significant token with <paramref name="seriesTitle"/> or one of <paramref name="aliases"/>.
    /// </summary>
    /// <remarks>
    /// This is defense-in-depth, not a replacement for the Ollama classification: it only rules
    /// out releases with essentially zero textual relationship to the assigned series, guarding
    /// against wasting an Ollama call (and, as a safety net, against an occasional confident
    /// misfire from the model) on an obviously-irrelevant candidate. Matching is exact-token only
    /// after normalisation (lowercase, non-alphanumeric characters - including the "." release
    /// titles use as a word separator - treated as token boundaries); it does NOT account for
    /// transliteration/spelling variants of the same word (e.g. "Kokaku" vs "Koukaku" are
    /// different tokens and will NOT be considered overlapping by this method alone - such cases
    /// rely on the Ollama classification itself, which the caller invokes only after this filter
    /// passes). Tokens of length 2 or less are excluded as insignificant (season/episode markers
    /// like "s01"/"e06" and short connector words are still long enough to match deliberately,
    /// but this is not a fuzzy-matching library and does not claim precision here).
    /// </remarks>
    private static bool HasTokenOverlap(string releaseTitle, string seriesTitle, IReadOnlyList<SeriesAlternateTitle> aliases)
    {
        HashSet<string> releaseTokens = Tokenize(releaseTitle);

        if (releaseTokens.Count is 0)
        {
            return false;
        }

        if (releaseTokens.Overlaps(Tokenize(seriesTitle)))
        {
            return true;
        }

        foreach (SeriesAlternateTitle alias in aliases)
        {
            if (releaseTokens.Overlaps(Tokenize(alias.Title)))
            {
                return true;
            }
        }

        return false;
    }

    private static HashSet<string> Tokenize(string value)
    {
        return value
            .ToLowerInvariant()
            .Split(NonTokenCharacters, StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length > 2)
            .ToHashSet();
    }

    private static readonly char[] NonTokenCharacters = " .-_()[]{}!,:;'\"".ToCharArray();

    private void RecordAiImportSkip(string skipCounterKey, int currentSkips) =>
        _cache.Set(skipCounterKey, currentSkips + 1, Constants.DefaultCacheEntryOptions);

    private static string AiImportDecisionCacheKey(string downloadId, Uri instanceUrl) =>
        $"ai_import_{downloadId.ToLowerInvariant()}_{instanceUrl}";

    private static string AiImportSkipCounterCacheKey(string downloadId, Uri instanceUrl) =>
        $"ai_import_skips_{downloadId.ToLowerInvariant()}_{instanceUrl}";

    private static string AiImportSkipBudgetWarnedCacheKey(string downloadId, Uri instanceUrl) =>
        $"ai_import_skip_budget_warned_{downloadId.ToLowerInvariant()}_{instanceUrl}";

    /// <summary>
    /// Attempts a manual import for a queue record via Sonarr's manual-import candidate list and
    /// the <c>ManualImport</c> command. Assumes the caller has already decided this record is
    /// eligible for an AI-assisted import (private-tracker and candidacy checks happen upstream).
    /// </summary>
    /// <remarks>
    /// Multi-episode releases are not supported: if the candidate list contains more than one
    /// importable file for <paramref name="record"/>'s <see cref="QueueRecord.DownloadId"/>, this
    /// method logs and returns <see langword="false"/> without issuing the import command.
    /// </remarks>
    /// <returns><see langword="true"/> if the import command was issued; otherwise <see langword="false"/>.</returns>
    protected async Task<bool> TryManualImportAsync(ArrInstance arrInstance, QueueRecord record)
    {
        UriBuilder candidateListUriBuilder = new(arrInstance.Url);
        candidateListUriBuilder.Path = $"{candidateListUriBuilder.Path.TrimEnd('/')}/api/v3/manualimport";
        candidateListUriBuilder.Query = $"downloadId={Uri.EscapeDataString(record.DownloadId)}";

        using HttpRequestMessage candidateListRequest = new(HttpMethod.Get, candidateListUriBuilder.Uri);
        SetApiKey(candidateListRequest, arrInstance.ApiKey);

        using HttpResponseMessage candidateListResponse = await _httpClient.SendAsync(
            candidateListRequest,
            HttpCompletionOption.ResponseHeadersRead);
        candidateListResponse.EnsureSuccessStatusCode();

        List<SonarrManualImportCandidate> candidates =
            await DeserializeStreamAsync<List<SonarrManualImportCandidate>>(candidateListResponse) ?? [];

        if (candidates.Count > 1)
        {
            _logger.LogInformation(
                "skip manual import | multi-file releases are unsupported | {count} candidate files | {downloadId} | {title}",
                candidates.Count,
                record.DownloadId,
                record.Title
            );

            return false;
        }

        if (candidates.Count is 0)
        {
            _logger.LogDebug("skip manual import | no candidate files found | {downloadId} | {title}", record.DownloadId, record.Title);

            return false;
        }

        SonarrManualImportCandidate candidate = candidates[0];

        if (candidate.Series is null)
        {
            _logger.LogDebug(
                "skip manual import | candidate has no matched series | {downloadId} | {title}",
                record.DownloadId,
                record.Title
            );

            return false;
        }

        SonarrManualImportCommand command = new()
        {
            Files =
            [
                new SonarrManualImportCommandFile
                {
                    Path = candidate.Path,
                    FolderName = candidate.FolderName,
                    SeriesId = candidate.Series.Id,
                    EpisodeIds = candidate.Episodes.Select(x => x.Id).ToList(),
                    Quality = candidate.Quality,
                    Languages = candidate.Languages,
                    ReleaseType = candidate.ReleaseType,
                    DownloadId = candidate.DownloadId,
                }
            ],
        };

        UriBuilder commandUriBuilder = new(arrInstance.Url);
        commandUriBuilder.Path = $"{commandUriBuilder.Path.TrimEnd('/')}/api/v3/command";

        using HttpRequestMessage commandRequest = new(HttpMethod.Post, commandUriBuilder.Uri);
        commandRequest.Content = new StringContent(
            JsonSerializer.Serialize(command, CleanuparrJsonOptions.Outbound),
            Encoding.UTF8,
            "application/json"
        );
        SetApiKey(commandRequest, arrInstance.ApiKey);

        HttpResponseMessage? commandResponse = await _dryRunInterceptor.InterceptAsync(() => SendRequestAsync(commandRequest));
        commandResponse?.Dispose();

        _logger.LogInformation(
            "manual import triggered | {url} | {downloadId} | {title}",
            arrInstance.Url,
            record.DownloadId,
            record.Title
        );

        return true;
    }

    private List<SonarrCommand> GetSearchCommands(HashSet<SeriesSearchItem> items)
    {
        const string episodeSearch = "EpisodeSearch";
        const string seasonSearch = "SeasonSearch";
        const string seriesSearch = "SeriesSearch";

        List<SonarrCommand> commands = new();

        foreach (SeriesSearchItem item in items)
        {
            SonarrCommand command = item.SearchType is SeriesSearchType.Episode
                ? commands.FirstOrDefault(x => x.SearchType is SeriesSearchType.Episode) ?? new() { Name = episodeSearch, EpisodeIds = new() }
                : new();

            switch (item.SearchType)
            {
                case SeriesSearchType.Episode when command.EpisodeIds is null:
                    command.EpisodeIds = [item.Id];
                    break;

                case SeriesSearchType.Episode when command.EpisodeIds is not null:
                    command.EpisodeIds.Add(item.Id);
                    break;

                case SeriesSearchType.Season:
                    command.Name = seasonSearch;
                    command.SeasonNumber = item.Id;
                    command.SeriesId = ((SeriesSearchItem)item).SeriesId;
                    break;

                case SeriesSearchType.Series:
                    command.Name = seriesSearch;
                    command.SeriesId = item.Id;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(item.SearchType), item.SearchType, null);
            }

            if (item.SearchType is SeriesSearchType.Episode && commands.Any(x => x.SearchType is SeriesSearchType.Episode))
            {
                // only one command will be generated for episodes search
                continue;
            }

            command.SearchType = item.SearchType;
            commands.Add(command);
        }

        return commands;
    }
}