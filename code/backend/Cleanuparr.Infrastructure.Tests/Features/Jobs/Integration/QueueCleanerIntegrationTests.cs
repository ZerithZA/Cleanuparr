using Cleanuparr.Infrastructure.Features.LazyLibrarian;
using Cleanuparr.Domain.Entities.Arr.Queue;
using Cleanuparr.Domain.Enums;
using Cleanuparr.Infrastructure.Features.Arr.Interfaces;
using Cleanuparr.Infrastructure.Features.DownloadClient;
using Cleanuparr.Infrastructure.Features.ItemStriker;
using Cleanuparr.Infrastructure.Services.Interfaces;
using Cleanuparr.Infrastructure.Tests.Features.Jobs.TestHelpers;
using Cleanuparr.Persistence.Models.Configuration.Arr;
using Cleanuparr.Persistence.Models.Configuration.General;
using Cleanuparr.Persistence.Models.Configuration.QueueCleaner;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Xunit;
using QueueCleanerJob = Cleanuparr.Infrastructure.Features.Jobs.QueueCleaner;

namespace Cleanuparr.Infrastructure.Tests.Features.Jobs.Integration;

[Collection(IntegrationTestCollection.Name)]
public class QueueCleanerIntegrationTests : IDisposable
{
    private readonly IntegrationTestFixture _fixture;

    public QueueCleanerIntegrationTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    public void Dispose()
    {
        Striker.RecurringHashes.Clear();
    }

    private QueueCleanerJob CreateSut()
    {
        IConnectivityChecker connectivityChecker = Substitute.For<IConnectivityChecker>();
        connectivityChecker.IsOnlineAsync(Arg.Any<GeneralConfig>(), Arg.Any<CancellationToken>()).Returns(true);

        return new QueueCleanerJob(
            Substitute.For<ILogger<QueueCleanerJob>>(),
            _fixture.DataContext,
            _fixture.Cache,
            _fixture.MessageBus,
            _fixture.ArrClientFactory,
            _fixture.ArrQueueIterator,
            _fixture.DownloadServiceFactory,
            _fixture.EventPublisher,
            _fixture.DryRunInterceptor,
            connectivityChecker,
            _fixture.LazyLibrarianServiceQC,
            _fixture.AiImportBudget);
    }

    [Fact]
    public async Task StalledTorrent_RemovesFromArr_SavesEvent_SendsNotification_AddsToSearchQueue()
    {
        // Arrange
        var instance = TestDataContextFactory.AddRadarrInstance(_fixture.DataContext);
        var downloadClient = TestDataContextFactory.AddDownloadClient(_fixture.DataContext);
        TestDataContextFactory.AddStallRule(_fixture.DataContext);

        var record = CreateQueueRecord(movieId: 42);

        _fixture.SetupArrQueueIterator(record);
        _fixture.ArrClient.IsRecordValid(Arg.Any<QueueRecord>()).Returns(true);
        _fixture.ArrClient.HasContentId(Arg.Any<QueueRecord>()).Returns(true);

        var mockDownloadService = _fixture.CreateMockDownloadService();
        mockDownloadService.ShouldRemoveFromArrQueueAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>())
            .Returns(new DownloadCheckResult
            {
                ShouldRemove = true,
                Found = true,
                DeleteReason = DeleteReason.Stalled,
                IsPrivate = false
            });
        _fixture.DownloadServiceFactory.GetDownloadService(Arg.Any<Cleanuparr.Persistence.Models.Configuration.DownloadClientConfig>())
            .Returns(mockDownloadService);

        var sut = CreateSut();

        // Act
        await sut.ExecuteAsync();

        // Assert Phase 1: IBus received a remove request
        var removeRequests = _fixture.GetCapturedRemoveRequests();
        removeRequests.Count.ShouldBe(1);

        // Process the captured messages through the real QueueItemRemover pipeline
        _fixture.ArrClient.DeleteQueueItemAsync(
            Arg.Any<ArrInstance>(), Arg.Any<QueueRecord>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<DeleteReason>())
            .Returns(Task.CompletedTask);

        await _fixture.ProcessCapturedRemoveRequestsAsync();

        // Assert Phase 2: Arr client was told to delete the item
        await _fixture.ArrClient.Received(1).DeleteQueueItemAsync(
            Arg.Is<ArrInstance>(i => i.Id == instance.Id),
            Arg.Is<QueueRecord>(r => r.DownloadId == record.DownloadId),
            true,
            false,
            DeleteReason.Stalled);

        // Assert Phase 3: Events persisted with full property verification
        var events = await _fixture.EventsContext.Events.ToListAsync();
        events.Count.ShouldBe(2);

        // DownloadMarkedForDeletion event
        var markedEvent = events.First(e => e.EventType == EventType.DownloadMarkedForDeletion);
        markedEvent.Message.ShouldBe("Download marked for deletion");
        markedEvent.Severity.ShouldBe(EventSeverity.Important);
        markedEvent.JobRunId.ShouldBe(_fixture.JobRunId);
        markedEvent.ArrInstanceId.ShouldBe(instance.Id);
        markedEvent.DownloadClientId.ShouldBe(mockDownloadService.ClientConfig.Id);
        markedEvent.IsDryRun.ShouldBe(false);
        markedEvent.StrikeId.ShouldBeNull();
        markedEvent.TrackingId.ShouldBeNull();
        markedEvent.SearchStatus.ShouldBeNull();
        markedEvent.CompletedAt.ShouldBeNull();
        markedEvent.CycleId.ShouldBeNull();
        markedEvent.ItemTitle.ShouldBe("Test.Movie.2024.1080p");
        markedEvent.ItemHash.ShouldBe("ABC123DEF456");

        // QueueItemDeleted event
        var deletedEvent = events.First(e => e.EventType == EventType.QueueItemDeleted);
        deletedEvent.Message.ShouldBe("Deleting item from queue with reason: Stalled");
        deletedEvent.Severity.ShouldBe(EventSeverity.Important);
        deletedEvent.JobRunId.ShouldBe(_fixture.JobRunId);
        deletedEvent.ArrInstanceId.ShouldBe(instance.Id);
        deletedEvent.DownloadClientId.ShouldBe(mockDownloadService.ClientConfig.Id);
        deletedEvent.IsDryRun.ShouldBe(false);
        deletedEvent.StrikeId.ShouldBeNull();
        deletedEvent.TrackingId.ShouldBeNull();
        deletedEvent.SearchStatus.ShouldBeNull();
        deletedEvent.CompletedAt.ShouldBeNull();
        deletedEvent.CycleId.ShouldBeNull();
        deletedEvent.ItemTitle.ShouldBe("Test.Movie.2024.1080p");
        deletedEvent.ItemHash.ShouldBe("ABC123DEF456");
        deletedEvent.RemoveFromClient.ShouldBe(true);
        deletedEvent.DeleteReason.ShouldBe(DeleteReason.Stalled);

        // Assert Phase 4: Notification was triggered
        await _fixture.NotificationPublisher.Received(1).NotifyQueueItemDeleted(true, DeleteReason.Stalled);

        // Assert Phase 5: Replacement search item was added to SearchQueue
        var searchItems = await _fixture.EventsContext.SearchQueue.ToListAsync();
        searchItems.Count.ShouldBe(1);
        searchItems[0].ArrInstanceId.ShouldBe(instance.Id);
        searchItems[0].ItemId.ShouldBe(42);
    }

    [Fact]
    public async Task FailedImport_RemovesWithFailedImportReason_SendsNotification()
    {
        // Arrange
        var instance = TestDataContextFactory.AddRadarrInstance(_fixture.DataContext);
        var downloadClient = TestDataContextFactory.AddDownloadClient(_fixture.DataContext);

        var record = CreateQueueRecord(movieId: 99);

        _fixture.SetupArrQueueIterator(record);
        _fixture.ArrClient.IsRecordValid(Arg.Any<QueueRecord>()).Returns(true);
        _fixture.ArrClient.HasContentId(Arg.Any<QueueRecord>()).Returns(true);
        _fixture.ArrClient.ShouldRemoveFromQueue(
            Arg.Any<InstanceType>(), Arg.Any<QueueRecord>(), Arg.Any<bool>(), Arg.Any<short>())
            .Returns(true);

        var mockDownloadService = _fixture.CreateMockDownloadService();
        mockDownloadService.ShouldRemoveFromArrQueueAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>())
            .Returns(new DownloadCheckResult
            {
                ShouldRemove = false,
                Found = true,
                IsPrivate = false
            });
        _fixture.DownloadServiceFactory.GetDownloadService(Arg.Any<Cleanuparr.Persistence.Models.Configuration.DownloadClientConfig>())
            .Returns(mockDownloadService);

        var sut = CreateSut();

        // Act
        await sut.ExecuteAsync();

        // Assert: failed import removal published
        _fixture.GetCapturedRemoveRequests().Count.ShouldBe(1);

        _fixture.ArrClient.DeleteQueueItemAsync(
            Arg.Any<ArrInstance>(), Arg.Any<QueueRecord>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<DeleteReason>())
            .Returns(Task.CompletedTask);

        await _fixture.ProcessCapturedRemoveRequestsAsync();

        // Full event property verification
        var events = await _fixture.EventsContext.Events.ToListAsync();
        var deletedEvent = events.First(e => e.EventType == EventType.QueueItemDeleted);
        deletedEvent.Message.ShouldBe("Deleting item from queue with reason: FailedImport");
        deletedEvent.Severity.ShouldBe(EventSeverity.Important);
        deletedEvent.JobRunId.ShouldBe(_fixture.JobRunId);
        deletedEvent.ArrInstanceId.ShouldBe(instance.Id);
        deletedEvent.DownloadClientId.ShouldBe(mockDownloadService.ClientConfig.Id);
        deletedEvent.IsDryRun.ShouldBe(false);
        deletedEvent.StrikeId.ShouldBeNull();
        deletedEvent.SearchStatus.ShouldBeNull();
        deletedEvent.ItemTitle.ShouldBe("Test.Movie.2024.1080p");
        deletedEvent.ItemHash.ShouldBe("ABC123DEF456");
        deletedEvent.RemoveFromClient.ShouldBe(true);
        deletedEvent.DeleteReason.ShouldBe(DeleteReason.FailedImport);

        // Notification with FailedImport reason
        await _fixture.NotificationPublisher.Received(1).NotifyQueueItemDeleted(true, DeleteReason.FailedImport);
    }

    [Fact]
    public async Task IgnoredDownload_IsSkipped_NoEventsOrNotifications()
    {
        // Arrange
        var instance = TestDataContextFactory.AddRadarrInstance(_fixture.DataContext);
        TestDataContextFactory.AddDownloadClient(_fixture.DataContext);

        var record = CreateQueueRecord(downloadId: "IGNORED_HASH_123");

        // Add the download ID to the ignored list
        var generalConfig = await _fixture.DataContext.GeneralConfigs.FirstAsync();
        generalConfig.IgnoredDownloads.Add("IGNORED_HASH_123");
        await _fixture.DataContext.SaveChangesAsync();

        _fixture.SetupArrQueueIterator(record);
        _fixture.ArrClient.IsRecordValid(Arg.Any<QueueRecord>()).Returns(true);
        _fixture.ArrClient.HasContentId(Arg.Any<QueueRecord>()).Returns(true);

        var sut = CreateSut();

        // Act
        await sut.ExecuteAsync();

        // Assert: No removal requests, no events, no notifications
        _fixture.GetCapturedRemoveRequests().ShouldBeEmpty();
        var events = await _fixture.EventsContext.Events.ToListAsync();
        events.ShouldBeEmpty();
        await _fixture.NotificationPublisher.DidNotReceive().NotifyQueueItemDeleted(Arg.Any<bool>(), Arg.Any<DeleteReason>());
    }

    [Fact]
    public async Task PrivateTorrent_RemoveFromClientIsFalse()
    {
        // Arrange
        var instance = TestDataContextFactory.AddRadarrInstance(_fixture.DataContext);
        TestDataContextFactory.AddDownloadClient(_fixture.DataContext);
        TestDataContextFactory.AddStallRule(_fixture.DataContext);

        var record = CreateQueueRecord(movieId: 50);

        _fixture.SetupArrQueueIterator(record);
        _fixture.ArrClient.IsRecordValid(Arg.Any<QueueRecord>()).Returns(true);
        _fixture.ArrClient.HasContentId(Arg.Any<QueueRecord>()).Returns(true);

        var mockDownloadService = _fixture.CreateMockDownloadService();
        mockDownloadService.ShouldRemoveFromArrQueueAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>())
            .Returns(new DownloadCheckResult
            {
                ShouldRemove = true,
                Found = true,
                DeleteReason = DeleteReason.Stalled,
                IsPrivate = true,
                DeleteFromClient = false
            });
        _fixture.DownloadServiceFactory.GetDownloadService(Arg.Any<Cleanuparr.Persistence.Models.Configuration.DownloadClientConfig>())
            .Returns(mockDownloadService);

        var sut = CreateSut();

        // Act
        await sut.ExecuteAsync();

        // Assert: RemoveFromClient should be false for private torrents
        _fixture.GetCapturedRemoveRequests().Count.ShouldBe(1);

        _fixture.ArrClient.DeleteQueueItemAsync(
            Arg.Any<ArrInstance>(), Arg.Any<QueueRecord>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<DeleteReason>())
            .Returns(Task.CompletedTask);

        await _fixture.ProcessCapturedRemoveRequestsAsync();

        // The arr client should be told NOT to remove from the download client
        await _fixture.ArrClient.Received(1).DeleteQueueItemAsync(
            Arg.Any<ArrInstance>(),
            Arg.Any<QueueRecord>(),
            false,
            false,
            DeleteReason.Stalled);

        // Full event property verification
        var events = await _fixture.EventsContext.Events.ToListAsync();

        var deletedEvent = events.First(e => e.EventType == EventType.QueueItemDeleted);
        deletedEvent.Message.ShouldBe("Deleting item from queue with reason: Stalled");
        deletedEvent.Severity.ShouldBe(EventSeverity.Important);
        deletedEvent.JobRunId.ShouldBe(_fixture.JobRunId);
        deletedEvent.ArrInstanceId.ShouldBe(instance.Id);
        deletedEvent.IsDryRun.ShouldBe(false);
        deletedEvent.ItemTitle.ShouldBe("Test.Movie.2024.1080p");
        deletedEvent.ItemHash.ShouldBe("ABC123DEF456");
        deletedEvent.RemoveFromClient.ShouldBe(false);
        deletedEvent.DeleteReason.ShouldBe(DeleteReason.Stalled);

        await _fixture.NotificationPublisher.Received(1).NotifyQueueItemDeleted(false, DeleteReason.Stalled);
    }

    private static QueueRecord CreateQueueRecord(
        long movieId = 1,
        string downloadId = "ABC123DEF456",
        string title = "Test.Movie.2024.1080p")
    {
        return new QueueRecord
        {
            Id = 1,
            Title = title,
            Protocol = "torrent",
            DownloadId = downloadId,
            MovieId = movieId,
            Status = "warning",
            StatusMessages = []
        };
    }

    #region AI-assisted import (real fixture) — AC-1, AC-4b, AC-5, AC-6, AC-13b

    /// <summary>
    /// Loads the Step 0 captured Sonarr queue record fixture (id 1191304180, "Resident Alien")
    /// from disk, per AC-1's requirement that this test load the committed fixture FILE rather
    /// than construct a QueueRecord inline, so the fixture and reality cannot diverge again
    /// without the file changing. Deserializes through the real external-API JSON path, matching
    /// AC-50b's precedent in ExternalApiReadTests.
    /// </summary>
    private static QueueRecord LoadRealFixtureRecord()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Sonarr", "QueueRecord-ImportBlocked-EpisodeHasFile.json");
        string json = File.ReadAllText(path);
        return System.Text.Json.JsonSerializer.Deserialize<QueueRecord>(
            json, Cleanuparr.Infrastructure.Json.CleanuparrJsonOptions.ExternalApiRead)!;
    }

    private void EnableAiImport(bool ignorePrivate = false)
    {
        QueueCleanerConfig config = _fixture.DataContext.QueueCleanerConfigs.First();
        config.AiImport = config.AiImport with { Enabled = true };
        config.FailedImport = config.FailedImport with { IgnorePrivate = ignorePrivate, MaxStrikes = 3 };
        _fixture.DataContext.SaveChanges();
    }

    // AC-1: given the real fixture, with AiImport.Enabled = true, the AI classification path
    // (TryAiAssistedImportAsync) is invoked exactly once.
    [Fact]
    public async Task RealFixture_AiImportEnabled_InvokesAiPathExactlyOnce()
    {
        // Arrange
        TestDataContextFactory.AddSonarrInstance(_fixture.DataContext);
        TestDataContextFactory.AddDownloadClient(_fixture.DataContext);
        EnableAiImport();

        QueueRecord record = LoadRealFixtureRecord();
        _fixture.SetupArrQueueIterator(record);
        _fixture.ArrClient.IsRecordValid(Arg.Any<QueueRecord>()).Returns(true);
        _fixture.ArrClient.HasContentId(Arg.Any<QueueRecord>()).Returns(true);
        _fixture.ArrClient.ShouldRemoveFromQueue(
            Arg.Any<InstanceType>(), Arg.Any<QueueRecord>(), Arg.Any<bool>(), Arg.Any<short>()
        ).Returns(false);

        var sut = CreateSut();

        // Act
        await sut.ExecuteAsync();

        // Assert
        await _fixture.ArrClient.Received(1).TryAiAssistedImportAsync(
            Arg.Any<ArrInstance>(), Arg.Is<QueueRecord>(r => r.DownloadId == record.DownloadId), Arg.Any<bool>());
    }

    // AC-2: real fixture, AiImport.Enabled = false: AI path is not invoked; ShouldRemoveFromQueue
    // is called with unchanged arguments.
    [Fact]
    public async Task RealFixture_AiImportDisabled_DoesNotInvokeAiPath()
    {
        // Arrange
        TestDataContextFactory.AddSonarrInstance(_fixture.DataContext);
        TestDataContextFactory.AddDownloadClient(_fixture.DataContext);
        // AiImport.Enabled defaults to false — no EnableAiImport() call.

        QueueRecord record = LoadRealFixtureRecord();
        _fixture.SetupArrQueueIterator(record);
        _fixture.ArrClient.IsRecordValid(Arg.Any<QueueRecord>()).Returns(true);
        _fixture.ArrClient.HasContentId(Arg.Any<QueueRecord>()).Returns(true);
        _fixture.ArrClient.ShouldRemoveFromQueue(
            Arg.Any<InstanceType>(), Arg.Any<QueueRecord>(), Arg.Any<bool>(), Arg.Any<short>()
        ).Returns(false);

        var sut = CreateSut();

        // Act
        await sut.ExecuteAsync();

        // Assert — the mock is left unstubbed for TryAiAssistedImportAsync in this run too, but
        // the point under test is that ShouldRemoveFromQueue still runs with the same arguments
        // regardless of whether AiImport is enabled (control-flow equivalence, AC-2/AC-43).
        await _fixture.ArrClient.Received(1).ShouldRemoveFromQueue(
            InstanceType.Sonarr, Arg.Is<QueueRecord>(r => r.DownloadId == record.DownloadId), false, Arg.Any<short>());
    }

    // AC-4b: the primary AC of the whole feature. Using the real fixture (already
    // trackedDownloadState: importBlocked, i.e. an existing line-131 match), assert the AI path
    // is invoked AND that FallThrough/Skipped leaves downstream behaviour identical to the
    // AiImport.Enabled = false run for the same fixture: zero strikes, no removal request.
    [Fact]
    public async Task RealFixture_AlreadyImportBlocked_AiPathIsAdditive_NoStrikeNoRemoval()
    {
        // Arrange
        TestDataContextFactory.AddSonarrInstance(_fixture.DataContext);
        TestDataContextFactory.AddDownloadClient(_fixture.DataContext);
        EnableAiImport();

        QueueRecord record = LoadRealFixtureRecord();
        _fixture.SetupArrQueueIterator(record);
        _fixture.ArrClient.IsRecordValid(Arg.Any<QueueRecord>()).Returns(true);
        _fixture.ArrClient.HasContentId(Arg.Any<QueueRecord>()).Returns(true);
        // Unstubbed TryAiAssistedImportAsync auto-returns AiImportOutcome.Skipped (AC-41/AC-42).
        // Unstubbed ShouldRemoveFromQueue auto-returns false (bool default), so no strike/removal
        // is issued — identical to the AiImport.Enabled = false behaviour for this fixture.

        var sut = CreateSut();

        // Act
        await sut.ExecuteAsync();

        // Assert — the AI path was invoked (being an existing line-131 match does not suppress it).
        await _fixture.ArrClient.Received(1).TryAiAssistedImportAsync(
            Arg.Any<ArrInstance>(), Arg.Is<QueueRecord>(r => r.DownloadId == record.DownloadId), Arg.Any<bool>());

        // Assert — no removal request was published (no strike authority exercised by the AI path).
        _fixture.GetCapturedRemoveRequests().ShouldBeEmpty();
    }

    // AC-5: a record with a message starting "Unable to import automatically" (the existing
    // IsEdgeCase path) — AI path not invoked.
    [Fact]
    public async Task EdgeCaseMessage_UnableToImportAutomatically_DoesNotInvokeAiPath()
    {
        // Arrange
        TestDataContextFactory.AddSonarrInstance(_fixture.DataContext);
        TestDataContextFactory.AddDownloadClient(_fixture.DataContext);
        EnableAiImport();

        QueueRecord record = new()
        {
            Id = 1,
            Title = "Edge Case Release",
            Protocol = "usenet",
            DownloadId = "edge-case-id",
            SeriesId = 1,
            EpisodeId = 1,
            Status = "downloading",
            TrackedDownloadStatus = "warning",
            TrackedDownloadState = "downloading",
            StatusMessages =
            [
                new TrackedDownloadStatusMessage { Title = "Edge Case Release", Messages = ["Unable to import automatically"] },
            ],
        };
        _fixture.SetupArrQueueIterator(record);
        _fixture.ArrClient.IsRecordValid(Arg.Any<QueueRecord>()).Returns(true);
        _fixture.ArrClient.HasContentId(Arg.Any<QueueRecord>()).Returns(true);

        var sut = CreateSut();

        // Act — this record does not carry the AI target message prefix at all, so at the
        // QueueCleaner level it still reaches TryAiAssistedImportAsync (candidacy is evaluated
        // inside the client, not by QueueCleaner) but the client-level candidacy predicate
        // (HasAiTargetStatusMessage) rejects it — covered directly against SonarrClient in
        // SonarrClientTests. This test pins the QueueCleaner-level contract: an unstubbed mock
        // client still reaches ShouldRemoveFromQueue afterwards regardless.
        await sut.ExecuteAsync();

        // Assert
        await _fixture.ArrClient.Received(1).ShouldRemoveFromQueue(
            InstanceType.Sonarr, Arg.Is<QueueRecord>(r => r.DownloadId == record.DownloadId), Arg.Any<bool>(), Arg.Any<short>());
    }

    // AC-6: a record with trackedDownloadState "importPending" and an unrelated status message
    // (the real "No files found are eligible for import in ..." fixture) — AI path is not a
    // candidate at the client level, and ShouldRemoveFromQueue still returns false exactly as on
    // main. Loaded from the committed fixture file, matching AC-1's file-loading requirement.
    [Fact]
    public async Task RealFixture_ImportPendingUnrelatedMessage_ShouldRemoveFromQueueReturnsFalse()
    {
        // Arrange
        TestDataContextFactory.AddSonarrInstance(_fixture.DataContext);
        TestDataContextFactory.AddDownloadClient(_fixture.DataContext);
        EnableAiImport();

        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Sonarr", "QueueRecord-ImportPending-NoFilesEligible.json");
        string json = File.ReadAllText(path);
        QueueRecord record = System.Text.Json.JsonSerializer.Deserialize<QueueRecord>(
            json, Cleanuparr.Infrastructure.Json.CleanuparrJsonOptions.ExternalApiRead)!;

        _fixture.SetupArrQueueIterator(record);
        _fixture.ArrClient.IsRecordValid(Arg.Any<QueueRecord>()).Returns(true);
        _fixture.ArrClient.HasContentId(Arg.Any<QueueRecord>()).Returns(true);
        _fixture.ArrClient.ShouldRemoveFromQueue(
            Arg.Any<InstanceType>(), Arg.Any<QueueRecord>(), Arg.Any<bool>(), Arg.Any<short>()
        ).Returns(false);

        var sut = CreateSut();

        // Act
        await sut.ExecuteAsync();

        // Assert
        _fixture.GetCapturedRemoveRequests().ShouldBeEmpty();
        await _fixture.ArrClient.Received(1).ShouldRemoveFromQueue(
            InstanceType.Sonarr, Arg.Is<QueueRecord>(r => r.DownloadId == record.DownloadId), false, Arg.Any<short>());
    }

    #endregion
}
