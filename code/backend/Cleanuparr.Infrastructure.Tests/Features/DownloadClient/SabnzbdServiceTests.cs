using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Cleanuparr.Domain.Entities.Sabnzbd.Response;
using Cleanuparr.Domain.Enums;
using Cleanuparr.Infrastructure.Features.Context;
using Cleanuparr.Infrastructure.Features.DownloadClient;
using Cleanuparr.Infrastructure.Features.DownloadClient.Sabnzbd;
using Cleanuparr.Persistence.Models.Configuration;
using Cleanuparr.Persistence.Models.Configuration.MalwareBlocker;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Cleanuparr.Infrastructure.Tests.Features.DownloadClient;

public class SabnzbdServiceTests : IClassFixture<SabnzbdServiceFixture>
{
    private readonly SabnzbdServiceFixture _fixture;

    public SabnzbdServiceTests(SabnzbdServiceFixture fixture)
    {
        _fixture = fixture;
        _fixture.ResetMocks();
    }

    private static SabnzbdQueueSlot MakeQueueSlot(
        string nzoId,
        string status = "Downloading",
        string category = "movies",
        string kbPerSec = "0",
        string percentage = "0",
        string mb = "1000",
        string mbLeft = "1000") => new()
    {
        NzoId = nzoId,
        Filename = $"Test Job {nzoId}",
        Category = category,
        Status = status,
        KbPerSec = kbPerSec,
        Percentage = percentage,
        Mb = mb,
        MbLeft = mbLeft,
    };

    private static SabnzbdHistorySlot MakeHistorySlot(
        string nzoId,
        string status = "Completed",
        string category = "movies") => new()
    {
        NzoId = nzoId,
        Name = $"Test Job {nzoId}",
        Category = category,
        Status = status,
    };

    private void StubQueueAndHistory(SabnzbdQueue? queue = null, SabnzbdHistory? history = null)
    {
        _fixture.ClientWrapper.GetQueueAsync().Returns(queue ?? new SabnzbdQueue());
        _fixture.ClientWrapper.GetHistoryAsync(Arg.Any<int>()).Returns(history ?? new SabnzbdHistory());
    }

    #region LoginAsync / HealthCheckAsync

    public class LoginAsyncScenarios : SabnzbdServiceTests
    {
        public LoginAsyncScenarios(SabnzbdServiceFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public async Task WithApiKeyConfigured_CompletesWithoutError()
        {
            SabnzbdService sut = _fixture.CreateSut();

            await Should.NotThrowAsync(() => sut.LoginAsync());
        }

        [Fact]
        public async Task WithoutApiKeyConfigured_StillCompletes()
        {
            var config = new DownloadClientConfig
            {
                Id = Guid.NewGuid(),
                Name = "No Key Client",
                TypeName = DownloadClientTypeName.SABnzbd,
                Type = DownloadClientType.Usenet,
                Enabled = true,
                Host = new Uri("http://localhost:8080"),
            };
            SabnzbdService sut = _fixture.CreateSut(config);

            await Should.NotThrowAsync(() => sut.LoginAsync());
        }
    }

    public class HealthCheckAsyncScenarios : SabnzbdServiceTests
    {
        public HealthCheckAsyncScenarios(SabnzbdServiceFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public async Task VersionReturned_ReportsHealthy()
        {
            SabnzbdService sut = _fixture.CreateSut();
            _fixture.ClientWrapper.GetVersionAsync().Returns("4.3.0");

            var result = await sut.HealthCheckAsync();

            result.IsHealthy.ShouldBeTrue();
            result.ErrorMessage.ShouldBeNull();
        }

        [Fact]
        public async Task EmptyVersion_ReportsUnhealthy()
        {
            SabnzbdService sut = _fixture.CreateSut();
            _fixture.ClientWrapper.GetVersionAsync().Returns(string.Empty);

            var result = await sut.HealthCheckAsync();

            result.IsHealthy.ShouldBeFalse();
            result.ErrorMessage.ShouldNotBeNullOrEmpty();
        }

        [Fact]
        public async Task NullVersion_ReportsUnhealthy()
        {
            SabnzbdService sut = _fixture.CreateSut();
            _fixture.ClientWrapper.GetVersionAsync().Returns((string?)null);

            var result = await sut.HealthCheckAsync();

            result.IsHealthy.ShouldBeFalse();
        }

        [Fact]
        public async Task ClientThrows_ReportsUnhealthy_WithErrorMessage()
        {
            SabnzbdService sut = _fixture.CreateSut();
            _fixture.ClientWrapper.GetVersionAsync().Returns(Task.FromException<string?>(new HttpRequestException("connection refused")));

            var result = await sut.HealthCheckAsync();

            result.IsHealthy.ShouldBeFalse();
            result.ErrorMessage.ShouldContain("connection refused");
        }
    }

    #endregion

    #region ShouldRemoveFromArrQueueAsync

    public class ShouldRemoveFromArrQueueAsync_NotFoundScenarios : SabnzbdServiceTests
    {
        public ShouldRemoveFromArrQueueAsync_NotFoundScenarios(SabnzbdServiceFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public async Task NotInQueueOrHistory_ReturnsEmptyResult()
        {
            const string nzoId = "nonexistent";
            SabnzbdService sut = _fixture.CreateSut();
            StubQueueAndHistory();

            var result = await sut.ShouldRemoveFromArrQueueAsync(nzoId, Array.Empty<string>());

            result.Found.ShouldBeFalse();
            result.ShouldRemove.ShouldBeFalse();
            result.DeleteReason.ShouldBe(DeleteReason.None);
        }
    }

    public class ShouldRemoveFromArrQueueAsync_IgnoredScenarios : SabnzbdServiceTests
    {
        public ShouldRemoveFromArrQueueAsync_IgnoredScenarios(SabnzbdServiceFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public async Task JobIsIgnoredByCategory_ReturnsFound_ButNotRemoved()
        {
            const string nzoId = "job-1";
            const string ignoredCategory = "ignored-category";
            SabnzbdService sut = _fixture.CreateSut();

            SabnzbdQueue queue = new() { Slots = [MakeQueueSlot(nzoId, category: ignoredCategory)] };
            StubQueueAndHistory(queue);

            var result = await sut.ShouldRemoveFromArrQueueAsync(nzoId, new[] { ignoredCategory });

            result.Found.ShouldBeTrue();
            result.ShouldRemove.ShouldBeFalse();
        }

        [Fact]
        public async Task JobIsIgnoredByHash_ReturnsFound_ButNotRemoved()
        {
            const string nzoId = "job-1";
            SabnzbdService sut = _fixture.CreateSut();

            SabnzbdQueue queue = new() { Slots = [MakeQueueSlot(nzoId)] };
            StubQueueAndHistory(queue);

            var result = await sut.ShouldRemoveFromArrQueueAsync(nzoId, new[] { nzoId });

            result.Found.ShouldBeTrue();
            result.ShouldRemove.ShouldBeFalse();
        }
    }

    public class ShouldRemoveFromArrQueueAsync_HistoryScenarios : SabnzbdServiceTests
    {
        public ShouldRemoveFromArrQueueAsync_HistoryScenarios(SabnzbdServiceFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public async Task FailedHistoryItem_RemovesFromClient()
        {
            const string nzoId = "job-failed";
            SabnzbdService sut = _fixture.CreateSut();

            SabnzbdHistory history = new() { Slots = [MakeHistorySlot(nzoId, status: "Failed")] };
            StubQueueAndHistory(history: history);

            var result = await sut.ShouldRemoveFromArrQueueAsync(nzoId, Array.Empty<string>());

            result.Found.ShouldBeTrue();
            result.ShouldRemove.ShouldBeTrue();
            result.DeleteReason.ShouldBe(DeleteReason.None);
            result.DeleteFromClient.ShouldBeTrue();
        }

        [Fact]
        public async Task CompletedHistoryItem_DoesNotRemove()
        {
            const string nzoId = "job-completed";
            SabnzbdService sut = _fixture.CreateSut();

            SabnzbdHistory history = new() { Slots = [MakeHistorySlot(nzoId, status: "Completed")] };
            StubQueueAndHistory(history: history);

            var result = await sut.ShouldRemoveFromArrQueueAsync(nzoId, Array.Empty<string>());

            result.Found.ShouldBeTrue();
            result.ShouldRemove.ShouldBeFalse();
        }

        [Fact]
        public async Task QueueTakesPrecedenceOverHistory_WhenPresentInBoth()
        {
            // A job present in the active queue should be evaluated as a queue item, not
            // fall through to the (stale) history entry for the same nzo_id.
            const string nzoId = "job-both";
            SabnzbdService sut = _fixture.CreateSut();

            SabnzbdQueue queue = new() { Slots = [MakeQueueSlot(nzoId, status: "Downloading", kbPerSec: "500")] };
            SabnzbdHistory history = new() { Slots = [MakeHistorySlot(nzoId, status: "Failed")] };
            StubQueueAndHistory(queue, history);

            _fixture.RuleEvaluator
                .EvaluateSlowRulesAsync(Arg.Any<SabnzbdItemWrapper>(), Arg.Any<Func<Task<bool>>?>())
                .Returns((false, DeleteReason.None, false, false));

            var result = await sut.ShouldRemoveFromArrQueueAsync(nzoId, Array.Empty<string>());

            // Would have been ShouldRemove=true/DeleteReason.None if history had been used instead.
            result.ShouldRemove.ShouldBeFalse();
        }
    }

    public class ShouldRemoveFromArrQueueAsync_SlowDownloadScenarios : SabnzbdServiceTests
    {
        public ShouldRemoveFromArrQueueAsync_SlowDownloadScenarios(SabnzbdServiceFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public async Task NotDownloading_SkipsSlowCheck()
        {
            const string nzoId = "job-1";
            SabnzbdService sut = _fixture.CreateSut();

            SabnzbdQueue queue = new() { Slots = [MakeQueueSlot(nzoId, status: "Paused", kbPerSec: "500")] };
            StubQueueAndHistory(queue);

            _fixture.RuleEvaluator
                .EvaluateStallRulesAsync(Arg.Any<SabnzbdItemWrapper>())
                .Returns((false, DeleteReason.None, false, false));

            var result = await sut.ShouldRemoveFromArrQueueAsync(nzoId, Array.Empty<string>());

            result.ShouldRemove.ShouldBeFalse();
            await _fixture.RuleEvaluator.DidNotReceive()
                .EvaluateSlowRulesAsync(Arg.Any<SabnzbdItemWrapper>(), Arg.Any<Func<Task<bool>>?>());
        }

        [Fact]
        public async Task ZeroSpeed_SkipsSlowCheck()
        {
            const string nzoId = "job-1";
            SabnzbdService sut = _fixture.CreateSut();

            SabnzbdQueue queue = new() { Slots = [MakeQueueSlot(nzoId, status: "Downloading", kbPerSec: "0")] };
            StubQueueAndHistory(queue);

            var result = await sut.ShouldRemoveFromArrQueueAsync(nzoId, Array.Empty<string>());

            result.ShouldRemove.ShouldBeFalse();
            await _fixture.RuleEvaluator.DidNotReceive()
                .EvaluateSlowRulesAsync(Arg.Any<SabnzbdItemWrapper>(), Arg.Any<Func<Task<bool>>?>());
        }

        [Fact]
        public async Task MatchesRule_RemovesFromQueue()
        {
            const string nzoId = "job-1";
            SabnzbdService sut = _fixture.CreateSut();

            SabnzbdQueue queue = new() { Slots = [MakeQueueSlot(nzoId, status: "Downloading", kbPerSec: "10")] };
            StubQueueAndHistory(queue);

            _fixture.RuleEvaluator
                .EvaluateSlowRulesAsync(Arg.Any<SabnzbdItemWrapper>(), Arg.Any<Func<Task<bool>>?>())
                .Returns((true, DeleteReason.SlowSpeed, true, false));

            var result = await sut.ShouldRemoveFromArrQueueAsync(nzoId, Array.Empty<string>());

            result.ShouldRemove.ShouldBeTrue();
            result.DeleteReason.ShouldBe(DeleteReason.SlowSpeed);
            result.DeleteFromClient.ShouldBeTrue();
        }
    }

    public class ShouldRemoveFromArrQueueAsync_StalledDownloadScenarios : SabnzbdServiceTests
    {
        public ShouldRemoveFromArrQueueAsync_StalledDownloadScenarios(SabnzbdServiceFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public async Task DownloadingWithZeroSpeed_IsStalled_MatchesRule_RemovesFromQueue()
        {
            const string nzoId = "job-1";
            SabnzbdService sut = _fixture.CreateSut();

            SabnzbdQueue queue = new() { Slots = [MakeQueueSlot(nzoId, status: "Downloading", kbPerSec: "0")] };
            StubQueueAndHistory(queue);

            _fixture.RuleEvaluator
                .EvaluateStallRulesAsync(Arg.Any<SabnzbdItemWrapper>())
                .Returns((true, DeleteReason.Stalled, true, false));

            var result = await sut.ShouldRemoveFromArrQueueAsync(nzoId, Array.Empty<string>());

            result.ShouldRemove.ShouldBeTrue();
            result.DeleteReason.ShouldBe(DeleteReason.Stalled);
            result.DeleteFromClient.ShouldBeTrue();
        }

        [Theory]
        [InlineData("Checking")]
        [InlineData("Verifying")]
        [InlineData("Extracting")]
        [InlineData("Repairing")]
        [InlineData("Moving")]
        [InlineData("Running Script")]
        public async Task PostProcessingState_IsNotStalled_SkipsCheck(string status)
        {
            const string nzoId = "job-1";
            SabnzbdService sut = _fixture.CreateSut();

            SabnzbdQueue queue = new() { Slots = [MakeQueueSlot(nzoId, status: status, kbPerSec: "0")] };
            StubQueueAndHistory(queue);

            var result = await sut.ShouldRemoveFromArrQueueAsync(nzoId, Array.Empty<string>());

            result.ShouldRemove.ShouldBeFalse();
            await _fixture.RuleEvaluator.DidNotReceive().EvaluateStallRulesAsync(Arg.Any<SabnzbdItemWrapper>());
        }

        [Fact]
        public async Task DownloadingWithSpeed_IsNotStalled_SkipsCheck()
        {
            const string nzoId = "job-1";
            SabnzbdService sut = _fixture.CreateSut();

            SabnzbdQueue queue = new() { Slots = [MakeQueueSlot(nzoId, status: "Downloading", kbPerSec: "500")] };
            StubQueueAndHistory(queue);

            _fixture.RuleEvaluator
                .EvaluateSlowRulesAsync(Arg.Any<SabnzbdItemWrapper>(), Arg.Any<Func<Task<bool>>?>())
                .Returns((false, DeleteReason.None, false, false));

            var result = await sut.ShouldRemoveFromArrQueueAsync(nzoId, Array.Empty<string>());

            result.ShouldRemove.ShouldBeFalse();
            await _fixture.RuleEvaluator.DidNotReceive().EvaluateStallRulesAsync(Arg.Any<SabnzbdItemWrapper>());
        }
    }

    public class ShouldRemoveFromArrQueueAsync_IntegrationScenarios : SabnzbdServiceTests
    {
        public ShouldRemoveFromArrQueueAsync_IntegrationScenarios(SabnzbdServiceFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public async Task BothChecksPass_DoesNotRemove()
        {
            const string nzoId = "job-1";
            SabnzbdService sut = _fixture.CreateSut();

            SabnzbdQueue queue = new() { Slots = [MakeQueueSlot(nzoId, status: "Downloading", kbPerSec: "5000")] };
            StubQueueAndHistory(queue);

            _fixture.RuleEvaluator
                .EvaluateSlowRulesAsync(Arg.Any<SabnzbdItemWrapper>(), Arg.Any<Func<Task<bool>>?>())
                .Returns((false, DeleteReason.None, false, false));

            var result = await sut.ShouldRemoveFromArrQueueAsync(nzoId, Array.Empty<string>());

            result.ShouldRemove.ShouldBeFalse();
            result.DeleteReason.ShouldBe(DeleteReason.None);
        }

        [Fact]
        public async Task IsPrivateAlwaysFalse()
        {
            const string nzoId = "job-1";
            SabnzbdService sut = _fixture.CreateSut();

            SabnzbdQueue queue = new() { Slots = [MakeQueueSlot(nzoId, status: "Downloading", kbPerSec: "5000")] };
            StubQueueAndHistory(queue);

            _fixture.RuleEvaluator
                .EvaluateSlowRulesAsync(Arg.Any<SabnzbdItemWrapper>(), Arg.Any<Func<Task<bool>>?>())
                .Returns((false, DeleteReason.None, false, false));

            var result = await sut.ShouldRemoveFromArrQueueAsync(nzoId, Array.Empty<string>());

            result.IsPrivate.ShouldBeFalse();
        }
    }

    #endregion

    #region BlockUnwantedFilesAsync

    public class BlockUnwantedFilesAsyncScenarios : SabnzbdServiceTests
    {
        public BlockUnwantedFilesAsyncScenarios(SabnzbdServiceFixture fixture) : base(fixture)
        {
        }

        private void SetMalwareBlockerContext(ContentBlockerConfig? config = null)
        {
            ContextProvider.Set(config ?? new ContentBlockerConfig());
            ContextProvider.Set(nameof(InstanceType), (object)InstanceType.Sonarr);

            _fixture.BlocklistProvider
                .GetBlocklistType(Arg.Any<InstanceType>())
                .Returns(BlocklistType.Blacklist);
            _fixture.BlocklistProvider
                .GetPatterns(Arg.Any<InstanceType>())
                .Returns(new ConcurrentBag<string>());
            _fixture.BlocklistProvider
                .GetRegexes(Arg.Any<InstanceType>())
                .Returns(new ConcurrentBag<Regex>());
        }

        [Fact]
        public async Task JobNotFound_ReturnsEmptyResult()
        {
            const string nzoId = "nonexistent";
            SabnzbdService sut = _fixture.CreateSut();
            StubQueueAndHistory();

            BlockFilesResult result = await sut.BlockUnwantedFilesAsync(nzoId, Array.Empty<string>());

            result.Found.ShouldBeFalse();
            result.ShouldRemove.ShouldBeFalse();
        }

        [Fact]
        public async Task JobIsIgnored_DoesNotEvaluateFilename()
        {
            const string nzoId = "job-1";
            const string ignoredCategory = "ignored-category";
            SabnzbdService sut = _fixture.CreateSut();

            SabnzbdQueue queue = new() { Slots = [MakeQueueSlot(nzoId, category: ignoredCategory)] };
            StubQueueAndHistory(queue);
            SetMalwareBlockerContext();

            BlockFilesResult result = await sut.BlockUnwantedFilesAsync(nzoId, new[] { ignoredCategory });

            result.Found.ShouldBeTrue();
            result.ShouldRemove.ShouldBeFalse();
            _fixture.FilenameEvaluator.DidNotReceiveWithAnyArgs()
                .IsValid(default!, default, default!, default!);
        }

        [Fact]
        public async Task JobNameMatchesBlocklist_MarksForRemoval()
        {
            const string nzoId = "job-malware";
            SabnzbdService sut = _fixture.CreateSut();

            SabnzbdQueue queue = new() { Slots = [MakeQueueSlot(nzoId)] };
            StubQueueAndHistory(queue);
            SetMalwareBlockerContext();

            _fixture.FilenameEvaluator
                .IsValid(Arg.Any<string>(), Arg.Any<BlocklistType>(), Arg.Any<ConcurrentBag<string>>(), Arg.Any<ConcurrentBag<Regex>>())
                .Returns(false);

            BlockFilesResult result = await sut.BlockUnwantedFilesAsync(nzoId, Array.Empty<string>());

            result.Found.ShouldBeTrue();
            result.ShouldRemove.ShouldBeTrue();
            result.DeleteReason.ShouldBe(DeleteReason.AtLeastOneFileBlocked);
        }

        [Fact]
        public async Task JobNameIsValid_DoesNotMarkForRemoval()
        {
            const string nzoId = "job-clean";
            SabnzbdService sut = _fixture.CreateSut();

            SabnzbdQueue queue = new() { Slots = [MakeQueueSlot(nzoId)] };
            StubQueueAndHistory(queue);
            SetMalwareBlockerContext();

            _fixture.FilenameEvaluator
                .IsValid(Arg.Any<string>(), Arg.Any<BlocklistType>(), Arg.Any<ConcurrentBag<string>>(), Arg.Any<ConcurrentBag<Regex>>())
                .Returns(true);

            BlockFilesResult result = await sut.BlockUnwantedFilesAsync(nzoId, Array.Empty<string>());

            result.Found.ShouldBeTrue();
            result.ShouldRemove.ShouldBeFalse();
            result.DeleteReason.ShouldBe(DeleteReason.None);
        }

        [Fact]
        public async Task IsPrivateAlwaysFalse()
        {
            const string nzoId = "job-1";
            SabnzbdService sut = _fixture.CreateSut();

            SabnzbdQueue queue = new() { Slots = [MakeQueueSlot(nzoId)] };
            StubQueueAndHistory(queue);
            SetMalwareBlockerContext();

            _fixture.FilenameEvaluator
                .IsValid(Arg.Any<string>(), Arg.Any<BlocklistType>(), Arg.Any<ConcurrentBag<string>>(), Arg.Any<ConcurrentBag<Regex>>())
                .Returns(true);

            BlockFilesResult result = await sut.BlockUnwantedFilesAsync(nzoId, Array.Empty<string>());

            result.IsPrivate.ShouldBeFalse();
        }
    }

    #endregion
}
