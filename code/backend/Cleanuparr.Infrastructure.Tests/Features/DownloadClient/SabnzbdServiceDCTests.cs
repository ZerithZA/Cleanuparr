using Cleanuparr.Domain.Entities;
using Cleanuparr.Domain.Entities.Sabnzbd.Response;
using Cleanuparr.Infrastructure.Features.DownloadClient.Sabnzbd;
using Cleanuparr.Persistence.Models.Configuration.DownloadCleaner;
using NSubstitute;
using Shouldly;
using Xunit;

// SABnzbd has no client-specific seeding rule type of its own (GetSeedingDownloads always
// returns empty, so its DownloadCleaner path only exercises category filtering), so the shared
// QBitSeedingRule record is reused here purely as a concrete ISeedingRule.

namespace Cleanuparr.Infrastructure.Tests.Features.DownloadClient;

public class SabnzbdServiceDCTests : IClassFixture<SabnzbdServiceFixture>
{
    private readonly SabnzbdServiceFixture _fixture;

    public SabnzbdServiceDCTests(SabnzbdServiceFixture fixture)
    {
        _fixture = fixture;
        _fixture.ResetMocks();
    }

    private static SabnzbdQueueSlot MakeQueueSlot(string nzoId, string category = "movies", string mb = "1000", string mbLeft = "0") => new()
    {
        NzoId = nzoId,
        Filename = $"Test Job {nzoId}",
        Category = category,
        Status = "Downloading",
        Mb = mb,
        MbLeft = mbLeft,
    };

    private static SabnzbdHistorySlot MakeHistorySlot(string nzoId, string category = "movies", string status = "Completed") => new()
    {
        NzoId = nzoId,
        Name = $"Test Job {nzoId}",
        Category = category,
        Status = status,
    };

    public class GetSeedingDownloads_Tests : SabnzbdServiceDCTests
    {
        public GetSeedingDownloads_Tests(SabnzbdServiceFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public async Task AlwaysReturnsEmptyList()
        {
            // Usenet downloads are never seeded.
            SabnzbdService sut = _fixture.CreateSut();

            var result = await sut.GetSeedingDownloads();

            result.ShouldBeEmpty();
        }
    }

    public class GetAllTorrentsLite_Tests : SabnzbdServiceDCTests
    {
        public GetAllTorrentsLite_Tests(SabnzbdServiceFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public async Task CombinesQueueAndHistory()
        {
            SabnzbdService sut = _fixture.CreateSut();

            _fixture.ClientWrapper.GetQueueAsync().Returns(new SabnzbdQueue { Slots = [MakeQueueSlot("q1")] });
            _fixture.ClientWrapper.GetHistoryAsync(Arg.Any<int>()).Returns(new SabnzbdHistory { Slots = [MakeHistorySlot("h1")] });

            var result = await sut.GetAllTorrentsLite();

            result.Count.ShouldBe(2);
            result.ShouldContain(x => x.Hash == "q1");
            result.ShouldContain(x => x.Hash == "h1");
        }

        [Fact]
        public async Task SkipsSlotsWithEmptyNzoId()
        {
            SabnzbdService sut = _fixture.CreateSut();

            _fixture.ClientWrapper.GetQueueAsync().Returns(new SabnzbdQueue
            {
                Slots = [MakeQueueSlot("") with { NzoId = "" }, MakeQueueSlot("q1")]
            });
            _fixture.ClientWrapper.GetHistoryAsync(Arg.Any<int>()).Returns(new SabnzbdHistory());

            var result = await sut.GetAllTorrentsLite();

            result.ShouldHaveSingleItem();
            result[0].Hash.ShouldBe("q1");
        }

        [Fact]
        public async Task ReturnsEmptyList_WhenQueueAndHistoryAreEmpty()
        {
            SabnzbdService sut = _fixture.CreateSut();

            _fixture.ClientWrapper.GetQueueAsync().Returns(new SabnzbdQueue());
            _fixture.ClientWrapper.GetHistoryAsync(Arg.Any<int>()).Returns(new SabnzbdHistory());

            var result = await sut.GetAllTorrentsLite();

            result.ShouldBeEmpty();
        }
    }

    public class FilterDownloadsToBeCleanedAsync_Tests : SabnzbdServiceDCTests
    {
        public FilterDownloadsToBeCleanedAsync_Tests(SabnzbdServiceFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public void MatchesCategories()
        {
            SabnzbdService sut = _fixture.CreateSut();
            var downloads = new List<ITorrentItemWrapper>
            {
                new SabnzbdItemWrapper(MakeQueueSlot("q1", category: "movies")),
                new SabnzbdItemWrapper(MakeQueueSlot("q2", category: "tv")),
                new SabnzbdItemWrapper(MakeQueueSlot("q3", category: "music")),
            };

            var rules = new List<ISeedingRule>
            {
                new QBitSeedingRule { Name = "movies", Categories = ["movies"] },
                new QBitSeedingRule { Name = "tv", Categories = ["tv"] },
            };

            var result = sut.FilterDownloadsToBeCleanedAsync(downloads, rules);

            result.ShouldNotBeNull();
            result.Count.ShouldBe(2);
            result.ShouldContain(x => x.Category == "movies");
            result.ShouldContain(x => x.Category == "tv");
        }

        [Fact]
        public void IsCaseInsensitive()
        {
            SabnzbdService sut = _fixture.CreateSut();
            var downloads = new List<ITorrentItemWrapper> { new SabnzbdItemWrapper(MakeQueueSlot("q1", category: "Movies")) };
            var rules = new List<ISeedingRule> { new QBitSeedingRule { Name = "movies", Categories = ["movies"] } };

            var result = sut.FilterDownloadsToBeCleanedAsync(downloads, rules);

            result.ShouldNotBeNull();
            result.ShouldHaveSingleItem();
        }

        [Fact]
        public void SkipsDownloadsWithEmptyHash()
        {
            SabnzbdService sut = _fixture.CreateSut();
            var downloads = new List<ITorrentItemWrapper>
            {
                new SabnzbdItemWrapper(MakeQueueSlot("q1", category: "movies") with { NzoId = "" }),
                new SabnzbdItemWrapper(MakeQueueSlot("q2", category: "movies")),
            };
            var rules = new List<ISeedingRule> { new QBitSeedingRule { Name = "movies", Categories = ["movies"] } };

            var result = sut.FilterDownloadsToBeCleanedAsync(downloads, rules);

            result.ShouldNotBeNull();
            result.ShouldHaveSingleItem();
            result[0].Hash.ShouldBe("q2");
        }

        [Fact]
        public void ReturnsEmptyList_WhenNoMatches()
        {
            SabnzbdService sut = _fixture.CreateSut();
            var downloads = new List<ITorrentItemWrapper> { new SabnzbdItemWrapper(MakeQueueSlot("q1", category: "music")) };
            var rules = new List<ISeedingRule> { new QBitSeedingRule { Name = "movies", Categories = ["movies"] } };

            var result = sut.FilterDownloadsToBeCleanedAsync(downloads, rules);

            result.ShouldNotBeNull();
            result.ShouldBeEmpty();
        }
    }

    public class FilterDownloadsToChangeCategoryAsync_Tests : SabnzbdServiceDCTests
    {
        public FilterDownloadsToChangeCategoryAsync_Tests(SabnzbdServiceFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public void IncludesMatchingCategories()
        {
            SabnzbdService sut = _fixture.CreateSut();
            var downloads = new List<ITorrentItemWrapper>
            {
                new SabnzbdItemWrapper(MakeQueueSlot("q1", category: "movies")),
                new SabnzbdItemWrapper(MakeQueueSlot("q2", category: "movies")),
            };
            var unlinkedConfig = new UnlinkedConfig { Id = Guid.NewGuid(), TargetCategory = "unlinked", Categories = ["movies"] };

            var result = sut.FilterDownloadsToChangeCategoryAsync(downloads, unlinkedConfig);

            result.ShouldNotBeNull();
            result.Count.ShouldBe(2);
        }

        [Fact]
        public void IsCaseInsensitive()
        {
            SabnzbdService sut = _fixture.CreateSut();
            var downloads = new List<ITorrentItemWrapper> { new SabnzbdItemWrapper(MakeQueueSlot("q1", category: "Movies")) };
            var unlinkedConfig = new UnlinkedConfig { Id = Guid.NewGuid(), Categories = ["movies"] };

            var result = sut.FilterDownloadsToChangeCategoryAsync(downloads, unlinkedConfig);

            result.ShouldNotBeNull();
            result.ShouldHaveSingleItem();
        }

        [Fact]
        public void SkipsDownloadsWithEmptyHash()
        {
            SabnzbdService sut = _fixture.CreateSut();
            var downloads = new List<ITorrentItemWrapper>
            {
                new SabnzbdItemWrapper(MakeQueueSlot("q1", category: "movies") with { NzoId = "" }),
                new SabnzbdItemWrapper(MakeQueueSlot("q2", category: "movies")),
            };
            var unlinkedConfig = new UnlinkedConfig { Id = Guid.NewGuid(), Categories = ["movies"] };

            var result = sut.FilterDownloadsToChangeCategoryAsync(downloads, unlinkedConfig);

            result.ShouldNotBeNull();
            result.ShouldHaveSingleItem();
            result[0].Hash.ShouldBe("q2");
        }

        [Fact]
        public void ReturnsEmpty_WhenNoCategoriesMatch()
        {
            SabnzbdService sut = _fixture.CreateSut();
            var downloads = new List<ITorrentItemWrapper> { new SabnzbdItemWrapper(MakeQueueSlot("q1", category: "tv")) };
            var unlinkedConfig = new UnlinkedConfig { Id = Guid.NewGuid(), Categories = ["movies"] };

            var result = sut.FilterDownloadsToChangeCategoryAsync(downloads, unlinkedConfig);

            result.ShouldNotBeNull();
            result.ShouldBeEmpty();
        }
    }

    public class DeleteDownload_Tests : SabnzbdServiceDCTests
    {
        public DeleteDownload_Tests(SabnzbdServiceFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public async Task QueueItem_DeletesWithFromHistoryFalse()
        {
            SabnzbdService sut = _fixture.CreateSut();
            var wrapper = new SabnzbdItemWrapper(MakeQueueSlot("q1"));

            _fixture.ClientWrapper
                .DeleteAsync("q1", true, false)
                .Returns(Task.CompletedTask);

            await sut.DeleteDownload(wrapper, true);

            await _fixture.ClientWrapper.Received(1).DeleteAsync("q1", true, false);
        }

        [Fact]
        public async Task HistoryItem_DeletesWithFromHistoryTrue()
        {
            SabnzbdService sut = _fixture.CreateSut();
            var wrapper = new SabnzbdItemWrapper(MakeHistorySlot("h1"));

            _fixture.ClientWrapper
                .DeleteAsync("h1", false, true)
                .Returns(Task.CompletedTask);

            await sut.DeleteDownload(wrapper, false);

            await _fixture.ClientWrapper.Received(1).DeleteAsync("h1", false, true);
        }
    }

    public class CreateCategoryAsync_Tests : SabnzbdServiceDCTests
    {
        public CreateCategoryAsync_Tests(SabnzbdServiceFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public async Task CallsClientCreateCategory()
        {
            SabnzbdService sut = _fixture.CreateSut();

            _fixture.ClientWrapper.CreateCategoryAsync("new-category").Returns(Task.CompletedTask);

            await sut.CreateCategoryAsync("new-category");

            await _fixture.ClientWrapper.Received(1).CreateCategoryAsync("new-category");
        }

        [Fact]
        public async Task ClientThrows_DoesNotPropagateException()
        {
            SabnzbdService sut = _fixture.CreateSut();

            _fixture.ClientWrapper.CreateCategoryAsync("bad-category")
                .Returns(Task.FromException(new InvalidOperationException("rejected")));

            await Should.NotThrowAsync(() => sut.CreateCategoryAsync("bad-category"));
        }
    }

    public class ChangeTorrentCategoryAsync_Tests : SabnzbdServiceDCTests
    {
        public ChangeTorrentCategoryAsync_Tests(SabnzbdServiceFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public async Task ChangesCategory_AndPublishesEvent()
        {
            SabnzbdService sut = _fixture.CreateSut();
            var wrapper = new SabnzbdItemWrapper(MakeQueueSlot("q1", category: "movies"));

            await sut.ChangeTorrentCategoryAsync(wrapper, "cleanuparr-dead", useTag: false);

            await _fixture.ClientWrapper.Received(1).ChangeCategoryAsync("q1", "cleanuparr-dead");
            await _fixture.EventPublisher.Received(1).PublishCategoryChanged("movies", "cleanuparr-dead", false);
            wrapper.Category.ShouldBe("cleanuparr-dead");
        }

        [Fact]
        public async Task UseTagTrue_IsIgnored_StillChangesCategory()
        {
            SabnzbdService sut = _fixture.CreateSut();
            var wrapper = new SabnzbdItemWrapper(MakeQueueSlot("q1", category: "movies"));

            await sut.ChangeTorrentCategoryAsync(wrapper, "cleanuparr-dead", useTag: true);

            await _fixture.ClientWrapper.Received(1).ChangeCategoryAsync("q1", "cleanuparr-dead");
        }
    }

    public class ChangeCategoryForNoHardLinksAsync_Tests : SabnzbdServiceDCTests
    {
        public ChangeCategoryForNoHardLinksAsync_Tests(SabnzbdServiceFixture fixture) : base(fixture)
        {
        }

        private static UnlinkedConfig MakeUnlinkedConfig() => new()
        {
            Id = Guid.NewGuid(),
            UseTag = false,
            TargetCategory = "unlinked",
        };

        [Fact]
        public async Task NullDownloads_DoesNothing()
        {
            SabnzbdService sut = _fixture.CreateSut();

            await sut.ChangeCategoryForNoHardLinksAsync(null, MakeUnlinkedConfig());

            await _fixture.ClientWrapper.DidNotReceive().ChangeCategoryAsync(Arg.Any<string>(), Arg.Any<string>());
        }

        [Fact]
        public async Task EmptyDownloads_DoesNothing()
        {
            SabnzbdService sut = _fixture.CreateSut();

            await sut.ChangeCategoryForNoHardLinksAsync(new List<ITorrentItemWrapper>(), MakeUnlinkedConfig());

            await _fixture.ClientWrapper.DidNotReceive().ChangeCategoryAsync(Arg.Any<string>(), Arg.Any<string>());
        }

        [Fact]
        public async Task MissingSavePath_SkipsDownload()
        {
            SabnzbdService sut = _fixture.CreateSut();
            var wrapper = new SabnzbdItemWrapper(MakeQueueSlot("q1", category: "movies") with { Storage = null });
            var downloads = new List<ITorrentItemWrapper> { wrapper };

            await sut.ChangeCategoryForNoHardLinksAsync(downloads, MakeUnlinkedConfig());

            await _fixture.ClientWrapper.DidNotReceive().ChangeCategoryAsync(Arg.Any<string>(), Arg.Any<string>());
        }

        [Fact]
        public async Task NoHardlinks_ChangesCategory_AndPublishesEvent()
        {
            SabnzbdService sut = _fixture.CreateSut();
            var wrapper = new SabnzbdItemWrapper(MakeQueueSlot("q1", category: "movies") with { Storage = "/downloads/job" });
            var downloads = new List<ITorrentItemWrapper> { wrapper };

            _fixture.HardLinkFileService.GetHardLinkCount(Arg.Any<string>(), Arg.Any<bool>()).Returns(0);

            await sut.ChangeCategoryForNoHardLinksAsync(downloads, MakeUnlinkedConfig());

            await _fixture.ClientWrapper.Received(1).ChangeCategoryAsync("q1", "unlinked");
            await _fixture.EventPublisher.Received(1).PublishCategoryChanged("movies", "unlinked", false);
            wrapper.Category.ShouldBe("unlinked");
        }

        [Fact]
        public async Task HasHardlinks_SkipsDownload()
        {
            SabnzbdService sut = _fixture.CreateSut();
            var wrapper = new SabnzbdItemWrapper(MakeQueueSlot("q1", category: "movies") with { Storage = "/downloads/job" });
            var downloads = new List<ITorrentItemWrapper> { wrapper };

            _fixture.HardLinkFileService.GetHardLinkCount(Arg.Any<string>(), Arg.Any<bool>()).Returns(2);

            await sut.ChangeCategoryForNoHardLinksAsync(downloads, MakeUnlinkedConfig());

            await _fixture.ClientWrapper.DidNotReceive().ChangeCategoryAsync(Arg.Any<string>(), Arg.Any<string>());
        }

        [Fact]
        public async Task FileNotFound_SkipsDownload()
        {
            SabnzbdService sut = _fixture.CreateSut();
            var wrapper = new SabnzbdItemWrapper(MakeQueueSlot("q1", category: "movies") with { Storage = "/downloads/job" });
            var downloads = new List<ITorrentItemWrapper> { wrapper };

            _fixture.HardLinkFileService.GetHardLinkCount(Arg.Any<string>(), Arg.Any<bool>()).Returns(-1);

            await sut.ChangeCategoryForNoHardLinksAsync(downloads, MakeUnlinkedConfig());

            await _fixture.ClientWrapper.DidNotReceive().ChangeCategoryAsync(Arg.Any<string>(), Arg.Any<string>());
        }
    }

    public class GetClaimedPathsAsync_Tests : SabnzbdServiceDCTests
    {
        public GetClaimedPathsAsync_Tests(SabnzbdServiceFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public async Task FallsBackToSavePathAndName()
        {
            SabnzbdService sut = _fixture.CreateSut();
            var wrapper = new SabnzbdItemWrapper(MakeQueueSlot("q1") with { Storage = "/downloads", Filename = "some-show" });

            IReadOnlyList<string> claimed = await sut.GetClaimedPathsAsync(new ITorrentItemWrapper[] { wrapper });

            claimed.ShouldContain("/downloads/some-show");
        }
    }
}
