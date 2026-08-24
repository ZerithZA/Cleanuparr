using System.IO;
using System.Net;
using System.Text;
using Cleanuparr.Domain.Entities.Arr;
using Cleanuparr.Domain.Entities.Arr.Queue;
using Cleanuparr.Domain.Entities.Sonarr;
using Cleanuparr.Domain.Enums;
using Cleanuparr.Infrastructure.Features.Arr;
using Cleanuparr.Infrastructure.Features.Context;
using Cleanuparr.Infrastructure.Features.ItemStriker;
using Cleanuparr.Infrastructure.Features.Ollama;
using Cleanuparr.Infrastructure.Interceptors;
using Cleanuparr.Infrastructure.Tests.TestHelpers;
using Cleanuparr.Persistence.Models.Configuration.Arr;
using Cleanuparr.Persistence.Models.Configuration.QueueCleaner;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Cleanuparr.Infrastructure.Tests.Features.Arr;

public class SonarrClientTests
{
    private readonly ILogger<SonarrClient> _logger;
    private readonly IStriker _striker;
    private readonly IDryRunInterceptor _dryRunInterceptor;
    private readonly IOllamaClient _ollamaClient;
    private readonly IAiImportBudget _aiImportBudget;
    private readonly IMemoryCache _cache;
    private readonly FakeHttpMessageHandler _httpMessageHandler;
    private readonly TestSonarrClient _client;
    private readonly ArrInstance _arrInstance;

    public SonarrClientTests()
    {
        var logger = _logger = Substitute.For<ILogger<SonarrClient>>();
        _striker = Substitute.For<IStriker>();
        _dryRunInterceptor = Substitute.For<IDryRunInterceptor>();
        _ollamaClient = Substitute.For<IOllamaClient>();
        _aiImportBudget = Substitute.For<IAiImportBudget>();
        _cache = new MemoryCache(new MemoryCacheOptions());
        _httpMessageHandler = new FakeHttpMessageHandler();

        var httpClient = new HttpClient(_httpMessageHandler);
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(httpClient);

        _client = new TestSonarrClient(logger, httpClientFactory, _striker, _dryRunInterceptor, _ollamaClient, _aiImportBudget, _cache);
        _arrInstance = new ArrInstance
        {
            Name = "sonarr",
            ArrConfig = new ArrConfig { Type = InstanceType.Sonarr },
            Url = new Uri("http://localhost:8989/"),
            ApiKey = "api-key",
        };

        _dryRunInterceptor.IsDryRunEnabled().Returns(false);
        _dryRunInterceptor
            .InterceptAsync<HttpResponseMessage>(Arg.Any<Func<Task<HttpResponseMessage>>>(), Arg.Any<string?>())
            .Returns(async ci =>
            {
                Func<Task<HttpResponseMessage>> action = ci.Arg<Func<Task<HttpResponseMessage>>>();
                return await action();
            });

        // Default: budget allows Ollama calls.
        _aiImportBudget.CanCallOllama().Returns(true);
    }

    #region Queue URL overrides (via GetQueueItemsAsync / DeleteQueueItemAsync / HealthCheckAsync)

    [Fact]
    public async Task GetQueueItemsAsync_BuildsSonarrSpecificQuery()
    {
        // Arrange
        _httpMessageHandler.SetupResponse((_, _) => Task.FromResult(JsonResponse(
            new QueueListResponse { TotalRecords = 0, Records = Array.Empty<QueueRecord>() })));

        // Act
        await _client.GetQueueItemsAsync(_arrInstance, 1);

        // Assert
        var request = _httpMessageHandler.CapturedRequests.ShouldHaveSingleItem();
        request.RequestUri!.AbsolutePath.ShouldBe("/api/v3/queue");
        request.RequestUri.Query.ShouldBe("?page=1&pageSize=200&includeUnknownSeriesItems=true&includeSeries=true&includeEpisode=true");
    }

    [Fact]
    public async Task DeleteQueueItemAsync_UsesV3QueuePath()
    {
        // Arrange
        _httpMessageHandler.SetupResponse(HttpStatusCode.OK);

        // Act
        await _client.DeleteQueueItemAsync(_arrInstance, BuildRecord(123), removeFromClient: true, changeCategory: false, DeleteReason.FailedImport);

        // Assert
        var request = _httpMessageHandler.CapturedRequests.ShouldHaveSingleItem();
        request.RequestUri!.AbsolutePath.ShouldBe("/api/v3/queue/123");
    }

    [Fact]
    public async Task HealthCheckAsync_UsesV3SystemStatus()
    {
        // Arrange
        _httpMessageHandler.SetupResponse(HttpStatusCode.OK);

        // Act
        await _client.HealthCheckAsync(_arrInstance);

        // Assert
        var request = _httpMessageHandler.CapturedRequests.ShouldHaveSingleItem();
        request.RequestUri!.AbsolutePath.ShouldBe("/api/v3/system/status");
    }

    #endregion

    #region HasContentId

    [Fact]
    public void HasContentId_BothSeriesAndEpisodeSet_ReturnsTrue()
    {
        var record = new QueueRecord { Id = 1, Title = "t", DownloadId = "h", Protocol = "torrent", SeriesId = 5, EpisodeId = 9 };
        _client.HasContentId(record).ShouldBeTrue();
    }

    [Fact]
    public void HasContentId_SeriesIdZero_ReturnsFalse()
    {
        var record = new QueueRecord { Id = 1, Title = "t", DownloadId = "h", Protocol = "torrent", SeriesId = 0, EpisodeId = 9 };
        _client.HasContentId(record).ShouldBeFalse();
    }

    [Fact]
    public void HasContentId_EpisodeIdZero_ReturnsFalse()
    {
        var record = new QueueRecord { Id = 1, Title = "t", DownloadId = "h", Protocol = "torrent", SeriesId = 5, EpisodeId = 0 };
        _client.HasContentId(record).ShouldBeFalse();
    }

    #endregion

    #region SearchItemsAsync

    [Fact]
    public async Task SearchItemsAsync_NullItems_ReturnsEmpty()
    {
        // Act
        var ids = await _client.SearchItemsAsync(_arrInstance, null);

        // Assert
        ids.ShouldBeEmpty();
        _httpMessageHandler.CapturedRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task SearchItemsAsync_EmptyItems_ReturnsEmpty()
    {
        // Act
        var ids = await _client.SearchItemsAsync(_arrInstance, new HashSet<SearchItem>());

        // Assert
        ids.ShouldBeEmpty();
        _httpMessageHandler.CapturedRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task SearchItemsAsync_SeriesSearch_PostsSeriesCommandToCommandEndpoint()
    {
        // Arrange
        RouteResponses(commandIdForPost: 42);
        var items = new HashSet<SearchItem>
        {
            new SeriesSearchItem { Id = 100, SeriesId = 100, SearchType = SeriesSearchType.Series },
        };

        // Act
        var ids = await _client.SearchItemsAsync(_arrInstance, items);

        // Assert
        ids.ShouldBe(new long[] { 42 });
        var post = _httpMessageHandler.CapturedRequests.First(r => r.Method == HttpMethod.Post);
        post.RequestUri!.AbsolutePath.ShouldBe("/api/v3/command");
        var body = _httpMessageHandler.CapturedRequestBodies[_httpMessageHandler.CapturedRequests.IndexOf(post)];
        body.ShouldNotBeNull();
        body!.ShouldContain("\"name\":\"SeriesSearch\"", Case.Insensitive);
        body!.ShouldContain("\"seriesId\":100", Case.Insensitive);
    }

    [Fact]
    public async Task SearchItemsAsync_SeasonSearch_PostsSeasonCommandWithSeriesAndSeason()
    {
        // Arrange
        RouteResponses(commandIdForPost: 7);
        var items = new HashSet<SearchItem>
        {
            new SeriesSearchItem { Id = 3, SeriesId = 100, SearchType = SeriesSearchType.Season },
        };

        // Act
        var ids = await _client.SearchItemsAsync(_arrInstance, items);

        // Assert
        ids.ShouldBe(new long[] { 7 });
        var post = _httpMessageHandler.CapturedRequests.First(r => r.Method == HttpMethod.Post);
        var body = _httpMessageHandler.CapturedRequestBodies[_httpMessageHandler.CapturedRequests.IndexOf(post)];
        body.ShouldNotBeNull();
        body!.ShouldContain("\"name\":\"SeasonSearch\"", Case.Insensitive);
        body!.ShouldContain("\"seriesId\":100", Case.Insensitive);
        body!.ShouldContain("\"seasonNumber\":3", Case.Insensitive);
    }

    [Fact]
    public async Task SearchItemsAsync_MultipleEpisodes_BundlesIntoSingleCommand()
    {
        // Arrange
        RouteResponses(commandIdForPost: 99);
        var items = new HashSet<SearchItem>
        {
            new SeriesSearchItem { Id = 1, SeriesId = 10, SearchType = SeriesSearchType.Episode },
            new SeriesSearchItem { Id = 2, SeriesId = 10, SearchType = SeriesSearchType.Episode },
            new SeriesSearchItem { Id = 3, SeriesId = 10, SearchType = SeriesSearchType.Episode },
        };

        // Act
        var ids = await _client.SearchItemsAsync(_arrInstance, items);

        // Assert
        ids.ShouldBe(new long[] { 99 });
        var posts = _httpMessageHandler.CapturedRequests.Where(r => r.Method == HttpMethod.Post).ToList();
        posts.Count.ShouldBe(1);
        var bodyIndex = _httpMessageHandler.CapturedRequests.IndexOf(posts[0]);
        var body = _httpMessageHandler.CapturedRequestBodies[bodyIndex];
        body.ShouldNotBeNull();
        body!.ShouldContain("\"name\":\"EpisodeSearch\"", Case.Insensitive);
        body!.ShouldContain("\"episodeIds\":[1,2,3]", Case.Insensitive);
    }

    [Fact]
    public async Task SearchItemsAsync_SeriesThenEpisode_PostsBothCommands()
    {
        // Arrange
        RouteResponses(commandIdForPost: 11);
        var items = new HashSet<SearchItem>
        {
            new SeriesSearchItem { Id = 10, SeriesId = 10, SearchType = SeriesSearchType.Series },
            new SeriesSearchItem { Id = 1, SeriesId = 10, SearchType = SeriesSearchType.Episode },
        };

        // Act
        var ids = await _client.SearchItemsAsync(_arrInstance, items);

        // Assert
        ids.ShouldBe(new long[] { 11, 11 });
        var posts = _httpMessageHandler.CapturedRequests.Where(r => r.Method == HttpMethod.Post).ToList();
        posts.Count.ShouldBe(2);
        var bodies = posts
            .Select(p => _httpMessageHandler.CapturedRequestBodies[_httpMessageHandler.CapturedRequests.IndexOf(p)])
            .ToList();
        bodies.ShouldContain(b => b != null && b.Contains("\"name\":\"SeriesSearch\"", StringComparison.OrdinalIgnoreCase) && !b.Contains("episodeIds", StringComparison.OrdinalIgnoreCase));
        bodies.ShouldContain(b => b != null && b.Contains("\"name\":\"EpisodeSearch\"", StringComparison.OrdinalIgnoreCase) && b.Contains("\"episodeIds\":[1]", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SearchItemsAsync_EpisodesAroundOtherTypes_BundlesEpisodesIntoSingleCommand()
    {
        // Arrange
        RouteResponses(commandIdForPost: 33);
        var items = new HashSet<SearchItem>
        {
            new SeriesSearchItem { Id = 1, SeriesId = 10, SearchType = SeriesSearchType.Episode },
            new SeriesSearchItem { Id = 4, SeriesId = 10, SearchType = SeriesSearchType.Season },
            new SeriesSearchItem { Id = 2, SeriesId = 10, SearchType = SeriesSearchType.Episode },
        };

        // Act
        await _client.SearchItemsAsync(_arrInstance, items);

        // Assert
        var posts = _httpMessageHandler.CapturedRequests.Where(r => r.Method == HttpMethod.Post).ToList();
        posts.Count.ShouldBe(2);
        var episodeBody = posts
            .Select(p => _httpMessageHandler.CapturedRequestBodies[_httpMessageHandler.CapturedRequests.IndexOf(p)])
            .Single(b => b != null && b.Contains("\"name\":\"EpisodeSearch\"", StringComparison.OrdinalIgnoreCase));
        episodeBody!.ShouldContain("\"episodeIds\":[1,2]", Case.Insensitive);
    }

    [Fact]
    public async Task SearchItemsAsync_DryRun_ReturnsEmptyAndDoesNotPost()
    {
        // Arrange — interceptor returns null on dry run
        _dryRunInterceptor
            .InterceptAsync<HttpResponseMessage>(Arg.Any<Func<Task<HttpResponseMessage>>>(), Arg.Any<string?>())
            .Returns((HttpResponseMessage?)null);
        // Set up GETs that ComputeCommandLogContext might fire (series lookup)
        _httpMessageHandler.SetupResponse((req, _) => Task.FromResult(JsonNullResponse()));

        var items = new HashSet<SearchItem>
        {
            new SeriesSearchItem { Id = 5, SeriesId = 5, SearchType = SeriesSearchType.Series },
        };

        // Act
        var ids = await _client.SearchItemsAsync(_arrInstance, items);

        // Assert
        ids.ShouldBeEmpty();
        _httpMessageHandler.CapturedRequests.ShouldNotContain(r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task SearchItemsAsync_ServerErrorOnPost_ThrowsAndLogsError()
    {
        // Arrange — interceptor passes through; GET log-context lookups return null body; POST 500
        _httpMessageHandler.SetupResponse((req, _) =>
        {
            if (req.Method == HttpMethod.Post)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }
            return Task.FromResult(JsonNullResponse());
        });

        var items = new HashSet<SearchItem>
        {
            new SeriesSearchItem { Id = 5, SeriesId = 5, SearchType = SeriesSearchType.Series },
        };

        // Act / Assert
        await Should.ThrowAsync<HttpRequestException>(() => _client.SearchItemsAsync(_arrInstance, items));
    }

    #endregion

    #region StreamAllSeriesAsync / GetAllTagsAsync / GetEpisodes / EpisodeFiles / QualityProfiles / Scores

    [Fact]
    public async Task StreamAllSeriesAsync_BuildsCorrectUriAndDeserializesList()
    {
        // Arrange
        _httpMessageHandler.SetupResponse((_, _) => Task.FromResult(JsonResponse(new[]
        {
            new SearchableSeries { Id = 1, Title = "Show", QualityProfileId = 2, Tags = new List<long>() },
        })));

        // Act
        List<SearchableSeries> result = [];
        await foreach (SearchableSeries series in _client.StreamAllSeriesAsync(_arrInstance))
        {
            result.Add(series);
        }

        // Assert
        result.Count.ShouldBe(1);
        result[0].Title.ShouldBe("Show");
        var request = _httpMessageHandler.CapturedRequests.ShouldHaveSingleItem();
        request.RequestUri!.AbsolutePath.ShouldBe("/api/v3/series");
        request.Headers.GetValues("x-api-key").ShouldHaveSingleItem().ShouldBe("api-key");
    }

    [Fact]
    public async Task StreamAllSeriesAsync_NullBody_ReturnsEmpty()
    {
        // Arrange
        _httpMessageHandler.SetupResponse((_, _) => Task.FromResult(JsonNullResponse()));

        // Act
        List<SearchableSeries> result = [];
        await foreach (SearchableSeries series in _client.StreamAllSeriesAsync(_arrInstance))
        {
            result.Add(series);
        }

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetAllTagsAsync_DeserializesListAndUsesV3Path()
    {
        // Arrange
        _httpMessageHandler.SetupResponse((_, _) => Task.FromResult(JsonResponse(new[]
        {
            new Tag { Id = 1, Label = "Anime" },
            new Tag { Id = 2, Label = "HD" },
        })));

        // Act
        var tags = await _client.GetAllTagsAsync(_arrInstance);

        // Assert
        tags.Count.ShouldBe(2);
        var request = _httpMessageHandler.CapturedRequests.ShouldHaveSingleItem();
        request.RequestUri!.AbsolutePath.ShouldBe("/api/v3/tag");
    }

    [Fact]
    public async Task GetEpisodesAsync_BuildsSeriesIdQuery()
    {
        // Arrange
        _httpMessageHandler.SetupResponse((_, _) => Task.FromResult(JsonResponse(Array.Empty<SearchableEpisode>())));

        // Act
        await _client.GetEpisodesAsync(_arrInstance, seriesId: 42);

        // Assert
        var request = _httpMessageHandler.CapturedRequests.ShouldHaveSingleItem();
        request.RequestUri!.AbsolutePath.ShouldBe("/api/v3/episode");
        request.RequestUri.Query.ShouldBe("?seriesId=42");
    }

    [Fact]
    public async Task GetEpisodeFilesAsync_BuildsSeriesIdQuery()
    {
        // Arrange
        _httpMessageHandler.SetupResponse((_, _) => Task.FromResult(JsonResponse(Array.Empty<ArrEpisodeFile>())));

        // Act
        await _client.GetEpisodeFilesAsync(_arrInstance, seriesId: 7);

        // Assert
        var request = _httpMessageHandler.CapturedRequests.ShouldHaveSingleItem();
        request.RequestUri!.AbsolutePath.ShouldBe("/api/v3/episodefile");
        request.RequestUri.Query.ShouldBe("?seriesId=7");
    }

    [Fact]
    public async Task GetQualityProfilesAsync_DeserializesList()
    {
        // Arrange
        _httpMessageHandler.SetupResponse((_, _) => Task.FromResult(JsonResponse(new[]
        {
            new ArrQualityProfile { Id = 1, Name = "HD", CutoffFormatScore = 100 },
        })));

        // Act
        var profiles = await _client.GetQualityProfilesAsync(_arrInstance);

        // Assert
        profiles.Count.ShouldBe(1);
        var request = _httpMessageHandler.CapturedRequests.ShouldHaveSingleItem();
        request.RequestUri!.AbsolutePath.ShouldBe("/api/v3/qualityprofile");
    }

    [Fact]
    public async Task GetEpisodeFileScoresAsync_EmptyList_MakesNoRequests()
    {
        // Act
        var scores = await _client.GetEpisodeFileScoresAsync(_arrInstance, new List<long>());

        // Assert
        scores.ShouldBeEmpty();
        _httpMessageHandler.CapturedRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetEpisodeFileScoresAsync_OverHundred_BatchesIntoMultipleRequests()
    {
        // Arrange — 150 ids should produce 2 batches (100 + 50)
        var ids = Enumerable.Range(1, 150).Select(i => (long)i).ToList();
        _httpMessageHandler.SetupResponse((_, _) => Task.FromResult(JsonResponse(Array.Empty<MediaFileScore>())));

        // Act
        await _client.GetEpisodeFileScoresAsync(_arrInstance, ids);

        // Assert
        _httpMessageHandler.CapturedRequests.Count.ShouldBe(2);
        _httpMessageHandler.CapturedRequests.ShouldAllBe(r => r.RequestUri!.AbsolutePath == "/api/v3/episodefile");
    }

    [Fact]
    public async Task GetEpisodeFileScoresAsync_MergesScoresFromResponses()
    {
        // Arrange
        _httpMessageHandler.SetupResponse((_, _) => Task.FromResult(JsonResponse(new[]
        {
            new MediaFileScore { Id = 1, CustomFormatScore = 50 },
            new MediaFileScore { Id = 2, CustomFormatScore = -30 },
        })));

        // Act
        var scores = await _client.GetEpisodeFileScoresAsync(_arrInstance, new List<long> { 1, 2 });

        // Assert
        scores.Count.ShouldBe(2);
        scores[1].ShouldBe(50);
        scores[2].ShouldBe(-30);
    }

    #endregion

    #region Helpers

    private void RouteResponses(long commandIdForPost)
    {
        _httpMessageHandler.SetupResponse((req, _) =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath.EndsWith("/command"))
            {
                return Task.FromResult(JsonResponse(new { id = commandIdForPost }));
            }
            // GET log-context calls (/series/{id}, /episode?...) return null so log context bails out
            return Task.FromResult(JsonNullResponse());
        });
    }

    private static QueueRecord BuildRecord(long id) => new()
    {
        Id = id,
        Title = $"item-{id}",
        DownloadId = id.ToString(),
        Protocol = "torrent",
    };

    private static HttpResponseMessage JsonResponse<T>(T body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage JsonNullResponse() => new(HttpStatusCode.OK)
    {
        Content = new StringContent("null", Encoding.UTF8, "application/json"),
    };

    #endregion

    #region TryManualImportAsync

    [Fact]
    public async Task TryManualImportAsync_SingleCandidate_IssuesManualImportCommand()
    {
        // Arrange
        var record = BuildRecord(1);
        var candidate = new SonarrManualImportCandidate
        {
            Path = "/downloads/show/episode.mkv",
            FolderName = "show",
            SeriesId = 1781,
            Episodes = [new SonarrManualImportEpisode { Id = 91707, HasFile = false }],
            ReleaseType = "singleEpisode",
            DownloadId = record.DownloadId,
        };

        _httpMessageHandler.SetupResponse((req, _) =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath.EndsWith("/manualimport"))
            {
                return Task.FromResult(JsonResponse(new[] { candidate }));
            }

            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath.EndsWith("/command"))
            {
                return Task.FromResult(JsonResponse(new { id = 1L }));
            }

            return Task.FromResult(JsonNullResponse());
        });

        // Act
        bool result = await _client.TryManualImportAsyncPublic(_arrInstance, record);

        // Assert
        result.ShouldBeTrue();
        var candidateListRequest = _httpMessageHandler.CapturedRequests.ShouldContain(
            r => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.EndsWith("/manualimport"));
        candidateListRequest.RequestUri!.Query.ShouldBe($"?downloadId={record.DownloadId}");

        var commandRequest = _httpMessageHandler.CapturedRequests.ShouldContain(
            r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/command"));
        commandRequest.RequestUri!.AbsolutePath.ShouldBe("/api/v3/command");
    }

    [Fact]
    public async Task TryManualImportAsync_MultipleCandidates_SkipsWithoutIssuingCommand()
    {
        // Arrange
        var record = BuildRecord(2);
        var candidates = new[]
        {
            new SonarrManualImportCandidate { Path = "/a.mkv", SeriesId = 1, DownloadId = record.DownloadId },
            new SonarrManualImportCandidate { Path = "/b.mkv", SeriesId = 1, DownloadId = record.DownloadId },
        };

        _httpMessageHandler.SetupResponse((_, _) => Task.FromResult(JsonResponse(candidates)));

        // Act
        bool result = await _client.TryManualImportAsyncPublic(_arrInstance, record);

        // Assert
        result.ShouldBeFalse();
        _httpMessageHandler.CapturedRequests.ShouldNotContain(r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task TryManualImportAsync_NoCandidates_SkipsWithoutIssuingCommand()
    {
        // Arrange
        var record = BuildRecord(3);
        _httpMessageHandler.SetupResponse((_, _) =>
            Task.FromResult(JsonResponse(Array.Empty<SonarrManualImportCandidate>())));

        // Act
        bool result = await _client.TryManualImportAsyncPublic(_arrInstance, record);

        // Assert
        result.ShouldBeFalse();
        _httpMessageHandler.CapturedRequests.ShouldNotContain(r => r.Method == HttpMethod.Post);
    }

    #endregion

    #region TryAiAssistedImportAsync

    private static void SetQueueCleanerConfig(QueueCleanerConfig config) => ContextProvider.Set(config);

    private const string TargetMessagePrefix = "Found matching series via grab history";

    private static QueueRecord BuildAiCandidateRecord(
        long id = 1,
        bool episodeHasFile = false,
        string trackedDownloadState = "importBlocked",
        string? message = TargetMessagePrefix + ", but release was matched to series by ID")
    {
        return new QueueRecord
        {
            Id = id,
            Title = $"item-{id}",
            DownloadId = id.ToString(),
            Protocol = "usenet",
            SeriesId = 1781,
            EpisodeHasFile = episodeHasFile,
            TrackedDownloadStatus = "warning",
            TrackedDownloadState = trackedDownloadState,
            StatusMessages = message is null
                ? new List<TrackedDownloadStatusMessage> { new() { Title = $"item-{id}", Messages = ["some other message"] } }
                : new List<TrackedDownloadStatusMessage> { new() { Title = $"item-{id}", Messages = [message] } },
        };
    }

    private static QueueCleanerConfig BuildAiImportEnabledConfig(int confidenceThreshold = 75, bool ignorePrivate = false, int skipBudget = 3) => new()
    {
        FailedImport = new FailedImportConfig { IgnorePrivate = ignorePrivate, MaxStrikes = 3, PatternMode = PatternMode.Exclude },
        AiImport = new AiImportConfig
        {
            Enabled = true,
            ConfidenceThreshold = confidenceThreshold,
            TargetMessagePrefix = TargetMessagePrefix,
            SkipBudget = skipBudget,
        },
    };

    private void SetupSeriesResponse(string title = "Show Title", params string[] aliases)
    {
        _httpMessageHandler.SetupResponse((req, _) =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath.Contains("/series/"))
            {
                return Task.FromResult(JsonResponse(new
                {
                    id = 1781,
                    title,
                    alternateTitles = aliases.Select(a => new { title = a }).ToArray(),
                }));
            }

            return Task.FromResult(JsonNullResponse());
        });
    }

    private void SetupOllamaSuccess(bool match, int confidence, string reasoning = "reasoning") =>
        _ollamaClient
            .ClassifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new OllamaClassificationResponse(OllamaClassificationOutcome.Success, new OllamaClassificationResult(match, confidence, reasoning)));

    // AC-7: WhisparrV2Client leak guard.
    [Fact]
    public async Task TryAiAssistedImportAsync_WhisparrV2Instance_ReturnsSkippedWithZeroOllamaCalls()
    {
        // Arrange
        var whisparrClient = new WhisparrV2Client(
            Substitute.For<ILogger<WhisparrV2Client>>(),
            Substitute.For<IHttpClientFactory>(),
            _striker,
            _dryRunInterceptor,
            _ollamaClient,
            _aiImportBudget,
            _cache);
        SetQueueCleanerConfig(BuildAiImportEnabledConfig());
        var instance = new ArrInstance
        {
            Name = "whisparr",
            ArrConfig = new ArrConfig { Type = InstanceType.Whisparr },
            Version = 2,
            Url = new Uri("http://localhost:6969/"),
            ApiKey = "key",
        };
        var record = BuildAiCandidateRecord();

        // Act
        var outcome = await whisparrClient.TryAiAssistedImportAsync(instance, record, isPrivateDownload: false);

        // Assert
        outcome.ShouldBe(AiImportOutcome.Skipped);
        await _ollamaClient.DidNotReceive().ClassifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    // AC-7b: SportarrClient leak guard.
    [Fact]
    public async Task TryAiAssistedImportAsync_SportarrInstance_ReturnsSkippedWithZeroOllamaCalls()
    {
        // Arrange
        var sportarrClient = new SportarrClient(
            Substitute.For<ILogger<SportarrClient>>(),
            Substitute.For<IHttpClientFactory>(),
            _striker,
            _dryRunInterceptor,
            _ollamaClient,
            _aiImportBudget,
            _cache);
        SetQueueCleanerConfig(BuildAiImportEnabledConfig());
        var instance = new ArrInstance
        {
            Name = "sportarr",
            ArrConfig = new ArrConfig { Type = InstanceType.Sportarr },
            Url = new Uri("http://localhost:6970/"),
            ApiKey = "key",
        };
        var record = BuildAiCandidateRecord();

        // Act
        var outcome = await sportarrClient.TryAiAssistedImportAsync(instance, record, isPrivateDownload: false);

        // Assert
        outcome.ShouldBe(AiImportOutcome.Skipped);
        await _ollamaClient.DidNotReceive().ClassifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    // AC-8: non-Sonarr real instance types.
    [Theory]
    [InlineData(InstanceType.Radarr)]
    [InlineData(InstanceType.Lidarr)]
    [InlineData(InstanceType.Readarr)]
    [InlineData(InstanceType.Whisparr)]
    [InlineData(InstanceType.Sportarr)]
    [InlineData(InstanceType.LazyLibrarian)]
    public async Task TryAiAssistedImportAsync_NonSonarrInstanceType_ReturnsSkippedWithZeroOllamaCalls(InstanceType instanceType)
    {
        // Arrange
        SetQueueCleanerConfig(BuildAiImportEnabledConfig());
        var instance = new ArrInstance
        {
            Name = "instance",
            ArrConfig = new ArrConfig { Type = instanceType },
            Url = new Uri("http://localhost:8989/"),
            ApiKey = "key",
        };
        var record = BuildAiCandidateRecord();

        // Act
        var outcome = await _client.TryAiAssistedImportAsync(instance, record, isPrivateDownload: false);

        // Assert
        outcome.ShouldBe(AiImportOutcome.Skipped);
        await _ollamaClient.DidNotReceive().ClassifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    // AC-13: private tracker + IgnorePrivate.
    [Fact]
    public async Task TryAiAssistedImportAsync_IgnorePrivateAndIsPrivate_ReturnsSkippedWithZeroOllamaCalls()
    {
        // Arrange
        SetQueueCleanerConfig(BuildAiImportEnabledConfig(ignorePrivate: true));
        var record = BuildAiCandidateRecord();

        // Act
        var outcome = await _client.TryAiAssistedImportAsync(_arrInstance, record, isPrivateDownload: true);

        // Assert
        outcome.ShouldBe(AiImportOutcome.Skipped);
        await _ollamaClient.DidNotReceive().ClassifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    // Feature disabled.
    [Fact]
    public async Task TryAiAssistedImportAsync_FeatureDisabled_ReturnsSkippedWithZeroOllamaCalls()
    {
        // Arrange
        var config = BuildAiImportEnabledConfig();
        config.AiImport = config.AiImport with { Enabled = false };
        SetQueueCleanerConfig(config);
        var record = BuildAiCandidateRecord();

        // Act
        var outcome = await _client.TryAiAssistedImportAsync(_arrInstance, record, isPrivateDownload: false);

        // Assert
        outcome.ShouldBe(AiImportOutcome.Skipped);
        await _ollamaClient.DidNotReceive().ClassifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    // AC-6 / AC-48-adjacent: non-candidate message does not invoke the AI path.
    [Fact]
    public async Task TryAiAssistedImportAsync_NonCandidateMessage_ReturnsSkippedWithZeroOllamaCalls()
    {
        // Arrange
        SetQueueCleanerConfig(BuildAiImportEnabledConfig());
        var record = BuildAiCandidateRecord(message: "No files found are eligible for import in ...");

        // Act
        var outcome = await _client.TryAiAssistedImportAsync(_arrInstance, record, isPrivateDownload: false);

        // Assert
        outcome.ShouldBe(AiImportOutcome.Skipped);
        await _ollamaClient.DidNotReceive().ClassifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    // AC-49: message must live in .Messages[], not .Title.
    [Fact]
    public async Task TryAiAssistedImportAsync_TargetPrefixOnlyInTitle_ReturnsSkippedWithZeroOllamaCalls()
    {
        // Arrange
        SetQueueCleanerConfig(BuildAiImportEnabledConfig());
        var record = BuildAiCandidateRecord() with
        {
            StatusMessages = new List<TrackedDownloadStatusMessage>
            {
                new() { Title = TargetMessagePrefix + " some release name", Messages = ["unrelated"] },
            },
        };

        // Act
        var outcome = await _client.TryAiAssistedImportAsync(_arrInstance, record, isPrivateDownload: false);

        // Assert
        outcome.ShouldBe(AiImportOutcome.Skipped);
        await _ollamaClient.DidNotReceive().ClassifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    // AC-48: candidacy must not depend on TrackedDownloadState.
    [Theory]
    [InlineData("downloading")]
    [InlineData("importPending")]
    [InlineData("importFailed")]
    [InlineData("someUnknownFutureState")]
    public async Task TryAiAssistedImportAsync_CandidacyIgnoresTrackedDownloadState(string trackedDownloadState)
    {
        // Arrange
        SetQueueCleanerConfig(BuildAiImportEnabledConfig());
        SetupSeriesResponse();
        SetupOllamaSuccess(match: true, confidence: 90);
        var record = BuildAiCandidateRecord(trackedDownloadState: trackedDownloadState);
        RouteManualImportAndSeries();

        // Act
        await _client.TryAiAssistedImportAsync(_arrInstance, record, isPrivateDownload: false);

        // Assert — Ollama was invoked regardless of trackedDownloadState.
        await _ollamaClient.Received(1).ClassifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    // AC-14: dry run short-circuits before any HTTP call.
    [Fact]
    public async Task TryAiAssistedImportAsync_DryRun_ReturnsSkippedBeforeAnyHttpCall()
    {
        // Arrange
        SetQueueCleanerConfig(BuildAiImportEnabledConfig());
        _dryRunInterceptor.IsDryRunEnabled().Returns(true);
        var record = BuildAiCandidateRecord();

        // Act
        var outcome = await _client.TryAiAssistedImportAsync(_arrInstance, record, isPrivateDownload: false);

        // Assert
        outcome.ShouldBe(AiImportOutcome.Skipped);
        await _ollamaClient.DidNotReceive().ClassifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        _httpMessageHandler.CapturedRequests.ShouldBeEmpty();
    }

    // AC-15: dry-run log only fires for genuine candidates (behavioural proxy: outcome/Ollama calls
    // differ between a candidate and a non-candidate under dry run).
    [Fact]
    public async Task TryAiAssistedImportAsync_DryRun_NonCandidate_MakesNoHttpCallsEither()
    {
        // Arrange
        SetQueueCleanerConfig(BuildAiImportEnabledConfig());
        _dryRunInterceptor.IsDryRunEnabled().Returns(true);
        var record = BuildAiCandidateRecord(message: "unrelated message");

        // Act
        var outcome = await _client.TryAiAssistedImportAsync(_arrInstance, record, isPrivateDownload: false);

        // Assert — candidacy guard (guard 5) runs before the dry-run guard (guard 6), so a
        // non-candidate never reaches — and never logs — the dry-run branch.
        outcome.ShouldBe(AiImportOutcome.Skipped);
        _httpMessageHandler.CapturedRequests.ShouldBeEmpty();
    }

    // Circuit breaker / tick budget exhausted.
    [Fact]
    public async Task TryAiAssistedImportAsync_BudgetExhausted_ReturnsSkippedWithZeroOllamaCalls()
    {
        // Arrange
        SetQueueCleanerConfig(BuildAiImportEnabledConfig());
        _aiImportBudget.CanCallOllama().Returns(false);
        var record = BuildAiCandidateRecord();

        // Act
        var outcome = await _client.TryAiAssistedImportAsync(_arrInstance, record, isPrivateDownload: false);

        // Assert
        outcome.ShouldBe(AiImportOutcome.Skipped);
        await _ollamaClient.DidNotReceive().ClassifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    // Low confidence -> FallThrough.
    [Fact]
    public async Task TryAiAssistedImportAsync_LowConfidence_ReturnsFallThroughWithoutImporting()
    {
        // Arrange
        SetQueueCleanerConfig(BuildAiImportEnabledConfig(confidenceThreshold: 75));
        SetupSeriesResponse();
        SetupOllamaSuccess(match: true, confidence: 50);
        var record = BuildAiCandidateRecord();

        // Act
        var outcome = await _client.TryAiAssistedImportAsync(_arrInstance, record, isPrivateDownload: false);

        // Assert
        outcome.ShouldBe(AiImportOutcome.FallThrough);
        _httpMessageHandler.CapturedRequests.ShouldNotContain(r => r.Method == HttpMethod.Post);
    }

    // No match -> FallThrough.
    [Fact]
    public async Task TryAiAssistedImportAsync_NoMatch_ReturnsFallThroughWithoutImporting()
    {
        // Arrange
        SetQueueCleanerConfig(BuildAiImportEnabledConfig());
        SetupSeriesResponse();
        SetupOllamaSuccess(match: false, confidence: 100);
        var record = BuildAiCandidateRecord();

        // Act
        var outcome = await _client.TryAiAssistedImportAsync(_arrInstance, record, isPrivateDownload: false);

        // Assert
        outcome.ShouldBe(AiImportOutcome.FallThrough);
        _httpMessageHandler.CapturedRequests.ShouldNotContain(r => r.Method == HttpMethod.Post);
    }

    // AC-33: confidence exactly at the threshold (75) with match: true yields Imported.
    [Fact]
    public async Task TryAiAssistedImportAsync_ConfidenceExactlyAtThreshold_ReturnsImported()
    {
        // Arrange
        SetQueueCleanerConfig(BuildAiImportEnabledConfig(confidenceThreshold: 75));
        var record = BuildAiCandidateRecord();
        RouteManualImportAndSeries();
        SetupOllamaSuccess(match: true, confidence: 75);

        // Act
        var outcome = await _client.TryAiAssistedImportAsync(_arrInstance, record, isPrivateDownload: false);

        // Assert
        outcome.ShouldBe(AiImportOutcome.Imported);
        _httpMessageHandler.CapturedRequests.ShouldContain(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/command"));
    }

    // AC-33: confidence one point below the threshold (74) with match: true yields FallThrough.
    [Fact]
    public async Task TryAiAssistedImportAsync_ConfidenceOneBelowThreshold_ReturnsFallThrough()
    {
        // Arrange
        SetQueueCleanerConfig(BuildAiImportEnabledConfig(confidenceThreshold: 75));
        SetupSeriesResponse();
        SetupOllamaSuccess(match: true, confidence: 74);
        var record = BuildAiCandidateRecord();

        // Act
        var outcome = await _client.TryAiAssistedImportAsync(_arrInstance, record, isPrivateDownload: false);

        // Assert
        outcome.ShouldBe(AiImportOutcome.FallThrough);
        _httpMessageHandler.CapturedRequests.ShouldNotContain(r => r.Method == HttpMethod.Post);
    }

    // AC-22: idempotency key format is DownloadId + instance.Url, matching CacheKeys.
    // DownloadMarkedForRemoval's existing convention. Asserted behaviourally: a record whose
    // DownloadId differs only by case still hits the same cache entry (ToLowerInvariant), while a
    // different Url does not (covered by AC-22b above).
    [Fact]
    public async Task TryAiAssistedImportAsync_AfterImported_DownloadIdCaseInsensitive_SubsequentTickReturnsSkipped()
    {
        // Arrange
        SetQueueCleanerConfig(BuildAiImportEnabledConfig());
        var record = BuildAiCandidateRecord() with { DownloadId = "abc123def456" };
        RouteManualImportAndSeries();
        SetupOllamaSuccess(match: true, confidence: 90);

        var first = await _client.TryAiAssistedImportAsync(_arrInstance, record, isPrivateDownload: false);
        first.ShouldBe(AiImportOutcome.Imported);
        _ollamaClient.ClearReceivedCalls();

        var upperCaseIdRecord = record with { DownloadId = record.DownloadId.ToUpperInvariant() };

        // Act
        var second = await _client.TryAiAssistedImportAsync(_arrInstance, upperCaseIdRecord, isPrivateDownload: false);

        // Assert
        second.ShouldBe(AiImportOutcome.Skipped);
        await _ollamaClient.DidNotReceive().ClassifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    // AC-50 / AC-50b: EpisodeHasFile suppresses the import but classification still happens/logs.
    [Fact]
    public async Task TryAiAssistedImportAsync_EpisodeHasFile_ClassifiesButDoesNotImport()
    {
        // Arrange
        SetQueueCleanerConfig(BuildAiImportEnabledConfig());
        SetupSeriesResponse();
        SetupOllamaSuccess(match: true, confidence: 95);
        var record = BuildAiCandidateRecord(episodeHasFile: true);

        // Act
        var outcome = await _client.TryAiAssistedImportAsync(_arrInstance, record, isPrivateDownload: false);

        // Assert — classification WAS performed (Ollama called), but no manual-import command issued.
        outcome.ShouldBe(AiImportOutcome.FallThrough);
        await _ollamaClient.Received(1).ClassifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        _httpMessageHandler.CapturedRequests.ShouldNotContain(r => r.Method == HttpMethod.Post);
    }

    // High confidence + no existing file -> Imported.
    [Fact]
    public async Task TryAiAssistedImportAsync_HighConfidenceNoExistingFile_IssuesManualImportAndReturnsImported()
    {
        // Arrange
        SetQueueCleanerConfig(BuildAiImportEnabledConfig());
        var record = BuildAiCandidateRecord();
        RouteManualImportAndSeries();
        SetupOllamaSuccess(match: true, confidence: 90);

        // Act
        var outcome = await _client.TryAiAssistedImportAsync(_arrInstance, record, isPrivateDownload: false);

        // Assert
        outcome.ShouldBe(AiImportOutcome.Imported);
        _httpMessageHandler.CapturedRequests.ShouldContain(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/command"));
    }

    // AC-21: idempotency — a subsequent tick for the same DownloadId+Url after Imported returns Skipped.
    [Fact]
    public async Task TryAiAssistedImportAsync_AfterImported_SubsequentTickReturnsSkippedWithoutCallingOllama()
    {
        // Arrange
        SetQueueCleanerConfig(BuildAiImportEnabledConfig());
        var record = BuildAiCandidateRecord();
        RouteManualImportAndSeries();
        SetupOllamaSuccess(match: true, confidence: 90);

        var first = await _client.TryAiAssistedImportAsync(_arrInstance, record, isPrivateDownload: false);
        first.ShouldBe(AiImportOutcome.Imported);
        _ollamaClient.ClearReceivedCalls();
        _httpMessageHandler.CapturedRequests.Clear();

        // Act
        var second = await _client.TryAiAssistedImportAsync(_arrInstance, record, isPrivateDownload: false);

        // Assert
        second.ShouldBe(AiImportOutcome.Skipped);
        await _ollamaClient.DidNotReceive().ClassifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        _httpMessageHandler.CapturedRequests.ShouldBeEmpty();
    }

    // AC-22b: two distinct Sonarr instances sharing a DownloadId do not collide.
    [Fact]
    public async Task TryAiAssistedImportAsync_TwoInstancesSameDownloadId_DoNotSuppressEachOther()
    {
        // Arrange
        SetQueueCleanerConfig(BuildAiImportEnabledConfig());
        var record = BuildAiCandidateRecord();
        RouteManualImportAndSeries();
        SetupOllamaSuccess(match: true, confidence: 90);

        var instanceA = _arrInstance;
        var instanceB = new ArrInstance
        {
            Name = "sonarr-b",
            ArrConfig = new ArrConfig { Type = InstanceType.Sonarr },
            Url = new Uri("http://localhost:8990/"),
            ApiKey = "api-key-b",
        };

        var resultA = await _client.TryAiAssistedImportAsync(instanceA, record, isPrivateDownload: false);
        resultA.ShouldBe(AiImportOutcome.Imported);

        // Act
        var resultB = await _client.TryAiAssistedImportAsync(instanceB, record, isPrivateDownload: false);

        // Assert — instance B's import is not suppressed by instance A's cache entry.
        resultB.ShouldBe(AiImportOutcome.Imported);
        await _ollamaClient.Received(2).ClassifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    // AC-23/AC-24: consecutive-skip budget.
    [Fact]
    public async Task TryAiAssistedImportAsync_SkipBudgetExhausted_BypassesAiPathEntirely()
    {
        // Arrange — every classification attempt fails at the transport level, exhausting the
        // per-DownloadId skip budget (default in this test: 3).
        SetQueueCleanerConfig(BuildAiImportEnabledConfig(skipBudget: 3));
        SetupSeriesResponse();
        _ollamaClient
            .ClassifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new OllamaClassificationResponse(OllamaClassificationOutcome.TransportFailure));
        var record = BuildAiCandidateRecord();

        for (int i = 0; i < 3; i++)
        {
            var outcome = await _client.TryAiAssistedImportAsync(_arrInstance, record, isPrivateDownload: false);
            outcome.ShouldBe(AiImportOutcome.Skipped);
        }

        _ollamaClient.ClearReceivedCalls();

        // Act — the 4th consecutive attempt should bypass Ollama entirely (skip budget exhausted).
        var finalOutcome = await _client.TryAiAssistedImportAsync(_arrInstance, record, isPrivateDownload: false);

        // Assert
        finalOutcome.ShouldBe(AiImportOutcome.Skipped);
        await _ollamaClient.DidNotReceive().ClassifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    // AC-24: the consecutive-skip counter resets on a FallThrough/Imported outcome.
    [Fact]
    public async Task TryAiAssistedImportAsync_ConsecutiveSkipCounter_ResetsOnFallThrough()
    {
        // Arrange
        SetQueueCleanerConfig(BuildAiImportEnabledConfig(skipBudget: 3));
        SetupSeriesResponse();
        var record = BuildAiCandidateRecord();

        // Two transport failures (2 of 3 skip budget consumed)...
        _ollamaClient
            .ClassifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new OllamaClassificationResponse(OllamaClassificationOutcome.TransportFailure));
        await _client.TryAiAssistedImportAsync(_arrInstance, record, isPrivateDownload: false);
        await _client.TryAiAssistedImportAsync(_arrInstance, record, isPrivateDownload: false);

        // ...then a successful low-confidence classification (FallThrough) resets the counter.
        SetupOllamaSuccess(match: true, confidence: 10);
        var fallThroughOutcome = await _client.TryAiAssistedImportAsync(_arrInstance, record, isPrivateDownload: false);
        fallThroughOutcome.ShouldBe(AiImportOutcome.FallThrough);

        // Two more transport failures should not exhaust the budget, since the counter reset.
        _ollamaClient
            .ClassifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new OllamaClassificationResponse(OllamaClassificationOutcome.TransportFailure));
        await _client.TryAiAssistedImportAsync(_arrInstance, record, isPrivateDownload: false);
        await _client.TryAiAssistedImportAsync(_arrInstance, record, isPrivateDownload: false);
        _ollamaClient.ClearReceivedCalls();

        // Act — the 3rd failure since the reset should still call Ollama (budget not yet exhausted).
        _ollamaClient
            .ClassifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new OllamaClassificationResponse(OllamaClassificationOutcome.TransportFailure));
        await _client.TryAiAssistedImportAsync(_arrInstance, record, isPrivateDownload: false);

        // Assert
        await _ollamaClient.Received(1).ClassifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    // AC-25: skip-budget exhaustion is logged once at Warning level, naming the record and
    // reason, and does not recur every subsequent tick (i.e. once the budget is already
    // exhausted, later attempts that bypass Ollama entirely must not log the warning again).
    [Fact]
    public async Task TryAiAssistedImportAsync_SkipBudgetExhausted_LogsWarningOnceNotOnSubsequentTicks()
    {
        // Arrange
        SetQueueCleanerConfig(BuildAiImportEnabledConfig(skipBudget: 3));
        SetupSeriesResponse();
        _ollamaClient
            .ClassifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new OllamaClassificationResponse(OllamaClassificationOutcome.TransportFailure));
        var record = BuildAiCandidateRecord();

        for (int i = 0; i < 3; i++)
        {
            await _client.TryAiAssistedImportAsync(_arrInstance, record, isPrivateDownload: false);
        }

        // Act — two further ticks after the budget is already exhausted.
        await _client.TryAiAssistedImportAsync(_arrInstance, record, isPrivateDownload: false);
        await _client.TryAiAssistedImportAsync(_arrInstance, record, isPrivateDownload: false);

        // Assert — the "skip budget exhausted" warning fires exactly once (on the 4th attempt,
        // the first to observe the budget as exhausted), not again on the 5th.
        _logger.ReceivedLogContaining(LogLevel.Warning, "skip budget exhausted", count: 1);
    }

    private void RouteManualImportAndSeries()
    {
        var candidate = new SonarrManualImportCandidate
        {
            Path = "/downloads/show/episode.mkv",
            FolderName = "show",
            SeriesId = 1781,
            Episodes = [new SonarrManualImportEpisode { Id = 91707, HasFile = false }],
            ReleaseType = "singleEpisode",
            DownloadId = "1",
        };

        _httpMessageHandler.SetupResponse((req, _) =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath.Contains("/series/"))
            {
                return Task.FromResult(JsonResponse(new { id = 1781, title = "Show Title", alternateTitles = Array.Empty<object>() }));
            }

            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath.EndsWith("/manualimport"))
            {
                return Task.FromResult(JsonResponse(new[] { candidate }));
            }

            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath.EndsWith("/command"))
            {
                return Task.FromResult(JsonResponse(new { id = 1L }));
            }

            return Task.FromResult(JsonNullResponse());
        });
    }

    #endregion

    private sealed class TestSonarrClient : SonarrClient
    {
        public TestSonarrClient(
            ILogger<SonarrClient> logger,
            IHttpClientFactory httpClientFactory,
            IStriker striker,
            IDryRunInterceptor dryRunInterceptor,
            IOllamaClient ollamaClient,
            IAiImportBudget aiImportBudget,
            IMemoryCache cache
        ) : base(logger, httpClientFactory, striker, dryRunInterceptor, ollamaClient, aiImportBudget, cache)
        {
        }

        public Task<bool> TryManualImportAsyncPublic(ArrInstance arrInstance, QueueRecord record) =>
            TryManualImportAsync(arrInstance, record);
    }
}
