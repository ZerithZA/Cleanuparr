using Cleanuparr.Domain.Entities.Sabnzbd.Response;
using Cleanuparr.Infrastructure.Features.DownloadClient.Sabnzbd;
using Shouldly;
using Xunit;

namespace Cleanuparr.Infrastructure.Tests.Features.DownloadClient;

public class SabnzbdItemWrapperTests
{
    #region Constructors

    [Fact]
    public void Constructor_WithNullQueueSlot_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => new SabnzbdItemWrapper((SabnzbdQueueSlot)null!));
    }

    [Fact]
    public void Constructor_WithNullHistorySlot_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => new SabnzbdItemWrapper((SabnzbdHistorySlot)null!));
    }

    [Fact]
    public void Constructor_WithQueueSlot_IsNotHistoryItem()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdQueueSlot());

        wrapper.IsHistoryItem.ShouldBeFalse();
    }

    [Fact]
    public void Constructor_WithHistorySlot_IsHistoryItem()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdHistorySlot());

        wrapper.IsHistoryItem.ShouldBeTrue();
    }

    #endregion

    #region Hash / Name / Status

    [Fact]
    public void Hash_QueueSlot_ReturnsNzoId()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdQueueSlot { NzoId = "SABnzbd_nzo_abc123" });

        wrapper.Hash.ShouldBe("SABnzbd_nzo_abc123");
    }

    [Fact]
    public void Hash_HistorySlot_ReturnsNzoId()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdHistorySlot { NzoId = "SABnzbd_nzo_xyz789" });

        wrapper.Hash.ShouldBe("SABnzbd_nzo_xyz789");
    }

    [Fact]
    public void Name_QueueSlot_ReturnsFilename()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdQueueSlot { Filename = "My.Movie.2024" });

        wrapper.Name.ShouldBe("My.Movie.2024");
    }

    [Fact]
    public void Name_HistorySlot_ReturnsName()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdHistorySlot { Name = "My.Show.S01E01" });

        wrapper.Name.ShouldBe("My.Show.S01E01");
    }

    [Fact]
    public void Status_QueueSlot_ReturnsQueueStatus()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdQueueSlot { Status = "Downloading" });

        wrapper.Status.ShouldBe("Downloading");
    }

    [Fact]
    public void Status_HistorySlot_ReturnsHistoryStatus()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdHistorySlot { Status = "Failed" });

        wrapper.Status.ShouldBe("Failed");
    }

    #endregion

    #region Stubbed torrent-only fields

    [Fact]
    public void IsPrivate_AlwaysFalse()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdQueueSlot());

        wrapper.IsPrivate.ShouldBeFalse();
    }

    [Fact]
    public void Ratio_AlwaysZero()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdQueueSlot());

        wrapper.Ratio.ShouldBe(0);
    }

    [Fact]
    public void SeederCount_AlwaysNull()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdQueueSlot());

        wrapper.SeederCount.ShouldBeNull();
    }

    [Fact]
    public void SeedingTimeSeconds_AlwaysZero()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdQueueSlot());

        wrapper.SeedingTimeSeconds.ShouldBe(0);
    }

    [Fact]
    public void TrackerDomains_AlwaysEmpty()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdQueueSlot());

        wrapper.TrackerDomains.ShouldBeEmpty();
    }

    [Fact]
    public void Tags_AlwaysEmpty()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdQueueSlot());

        wrapper.Tags.ShouldBeEmpty();
    }

    #endregion

    #region Size / DownloadedBytes / DownloadSpeed

    [Fact]
    public void Size_QueueSlot_ConvertsMbToBytes()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdQueueSlot { Mb = "1024" });

        wrapper.Size.ShouldBe(1024L * 1024 * 1024);
    }

    [Fact]
    public void Size_QueueSlot_UnparsableMb_ReturnsZero()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdQueueSlot { Mb = "not-a-number" });

        wrapper.Size.ShouldBe(0);
    }

    [Fact]
    public void Size_HistorySlot_ReturnsBytes()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdHistorySlot { Bytes = 123456789 });

        wrapper.Size.ShouldBe(123456789);
    }

    [Fact]
    public void DownloadedBytes_QueueSlot_ComputesFromMbMinusMbLeft()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdQueueSlot { Mb = "1000", MbLeft = "400" });

        wrapper.DownloadedBytes.ShouldBe(600L * 1024 * 1024);
    }

    [Fact]
    public void DownloadedBytes_QueueSlot_NeverNegative()
    {
        // mbleft can momentarily exceed mb due to SABnzbd rounding; must clamp at zero.
        var wrapper = new SabnzbdItemWrapper(new SabnzbdQueueSlot { Mb = "100", MbLeft = "150" });

        wrapper.DownloadedBytes.ShouldBe(0);
    }

    [Fact]
    public void DownloadedBytes_HistorySlot_ReturnsBytes()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdHistorySlot { Bytes = 987654 });

        wrapper.DownloadedBytes.ShouldBe(987654);
    }

    [Fact]
    public void DownloadSpeed_QueueSlot_ConvertsKbPerSecToBytesPerSec()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdQueueSlot { KbPerSec = "512" });

        wrapper.DownloadSpeed.ShouldBe(512L * 1024);
    }

    [Fact]
    public void DownloadSpeed_HistorySlot_AlwaysZero()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdHistorySlot());

        wrapper.DownloadSpeed.ShouldBe(0);
    }

    #endregion

    #region CompletionPercentage

    [Theory]
    [InlineData("0", 0.0)]
    [InlineData("50", 50.0)]
    [InlineData("100", 100.0)]
    public void CompletionPercentage_QueueSlot_ParsesPercentage(string percentage, double expected)
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdQueueSlot { Percentage = percentage });

        wrapper.CompletionPercentage.ShouldBe(expected);
    }

    [Fact]
    public void CompletionPercentage_HistorySlot_Completed_Returns100()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdHistorySlot { Status = "Completed" });

        wrapper.CompletionPercentage.ShouldBe(100.0);
    }

    [Fact]
    public void CompletionPercentage_HistorySlot_Failed_ReturnsZero()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdHistorySlot { Status = "Failed" });

        wrapper.CompletionPercentage.ShouldBe(0.0);
    }

    #endregion

    #region Eta / ParseTimeLeft

    [Theory]
    [InlineData("1:30:00", 5400)]
    [InlineData("0:05:00", 300)]
    [InlineData("45:00", 2700)]
    [InlineData("0:00:00", 0)]
    public void Eta_QueueSlot_ParsesTimeLeft(string timeLeft, long expectedSeconds)
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdQueueSlot { TimeLeft = timeLeft });

        wrapper.Eta.ShouldBe(expectedSeconds);
    }

    [Fact]
    public void Eta_QueueSlot_EmptyTimeLeft_ReturnsZero()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdQueueSlot { TimeLeft = "" });

        wrapper.Eta.ShouldBe(0);
    }

    [Fact]
    public void Eta_QueueSlot_UnparsableTimeLeft_ReturnsZero()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdQueueSlot { TimeLeft = "garbage" });

        wrapper.Eta.ShouldBe(0);
    }

    [Fact]
    public void Eta_HistorySlot_AlwaysZero()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdHistorySlot());

        wrapper.Eta.ShouldBe(0);
    }

    #endregion

    #region LastActivityTime

    [Fact]
    public void LastActivityTime_HistorySlot_WithCompletedTimestamp_ReturnsDateTime()
    {
        long unixSeconds = 1700000000;
        var wrapper = new SabnzbdItemWrapper(new SabnzbdHistorySlot { Completed = unixSeconds });

        wrapper.LastActivityTime.ShouldBe(DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime);
    }

    [Fact]
    public void LastActivityTime_HistorySlot_ZeroCompleted_ReturnsNull()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdHistorySlot { Completed = 0 });

        wrapper.LastActivityTime.ShouldBeNull();
    }

    [Fact]
    public void LastActivityTime_QueueSlot_AlwaysNull()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdQueueSlot());

        wrapper.LastActivityTime.ShouldBeNull();
    }

    #endregion

    #region Category / SavePath

    [Fact]
    public void Category_QueueSlot_GetSet()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdQueueSlot { Category = "movies" });

        wrapper.Category.ShouldBe("movies");

        wrapper.Category = "tv";

        wrapper.Category.ShouldBe("tv");
    }

    [Fact]
    public void Category_HistorySlot_IsReadOnly_SetIsNoOp()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdHistorySlot { Category = "movies" });

        wrapper.Category = "tv";

        // History slots are terminal records; the setter silently no-ops rather than throwing.
        wrapper.Category.ShouldBe("movies");
    }

    [Fact]
    public void SavePath_QueueSlot_ReturnsStorage()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdQueueSlot { Storage = "/downloads/incomplete/job" });

        wrapper.SavePath.ShouldBe("/downloads/incomplete/job");
    }

    [Fact]
    public void SavePath_HistorySlot_ReturnsStorage()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdHistorySlot { Storage = "/downloads/complete/job" });

        wrapper.SavePath.ShouldBe("/downloads/complete/job");
    }

    [Fact]
    public void SavePath_NullStorage_ReturnsEmptyString()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdQueueSlot { Storage = null });

        wrapper.SavePath.ShouldBe(string.Empty);
    }

    #endregion

    #region IsDownloading / IsStalled

    [Theory]
    [InlineData("Downloading", true)]
    [InlineData("Queued", false)]
    [InlineData("Paused", false)]
    [InlineData("Checking", false)]
    public void IsDownloading_QueueSlot_ReturnsExpected(string status, bool expected)
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdQueueSlot { Status = status });

        wrapper.IsDownloading().ShouldBe(expected);
    }

    [Fact]
    public void IsDownloading_HistorySlot_AlwaysFalse()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdHistorySlot { Status = "Completed" });

        wrapper.IsDownloading().ShouldBeFalse();
    }

    [Fact]
    public void IsStalled_DownloadingWithZeroSpeed_ReturnsTrue()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdQueueSlot { Status = "Downloading", KbPerSec = "0" });

        wrapper.IsStalled().ShouldBeTrue();
    }

    [Fact]
    public void IsStalled_DownloadingWithSpeed_ReturnsFalse()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdQueueSlot { Status = "Downloading", KbPerSec = "500" });

        wrapper.IsStalled().ShouldBeFalse();
    }

    [Theory]
    [InlineData("Checking")]
    [InlineData("QuickCheck")]
    [InlineData("Verifying")]
    [InlineData("Repairing")]
    [InlineData("Extracting")]
    [InlineData("Moving")]
    [InlineData("Running Script")]
    public void IsStalled_PostProcessingState_ReturnsFalse(string status)
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdQueueSlot { Status = status, KbPerSec = "0" });

        wrapper.IsStalled().ShouldBeFalse();
    }

    [Fact]
    public void IsStalled_HistorySlot_AlwaysFalse()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdHistorySlot());

        wrapper.IsStalled().ShouldBeFalse();
    }

    #endregion

    #region IsIgnored

    [Fact]
    public void IsIgnored_EmptyList_ReturnsFalse()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdQueueSlot { NzoId = "job1", Category = "movies" });

        wrapper.IsIgnored(Array.Empty<string>()).ShouldBeFalse();
    }

    [Fact]
    public void IsIgnored_MatchingHash_ReturnsTrue()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdQueueSlot { NzoId = "job1" });

        wrapper.IsIgnored(new[] { "job1" }).ShouldBeTrue();
    }

    [Fact]
    public void IsIgnored_MatchingCategory_ReturnsTrue()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdQueueSlot { NzoId = "job1", Category = "movies" });

        wrapper.IsIgnored(new[] { "movies" }).ShouldBeTrue();
    }

    [Fact]
    public void IsIgnored_CaseInsensitive_ReturnsTrue()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdQueueSlot { NzoId = "JOB1" });

        wrapper.IsIgnored(new[] { "job1" }).ShouldBeTrue();
    }

    [Fact]
    public void IsIgnored_NotMatching_ReturnsFalse()
    {
        var wrapper = new SabnzbdItemWrapper(new SabnzbdQueueSlot { NzoId = "job1", Category = "movies" });

        wrapper.IsIgnored(new[] { "notmatching" }).ShouldBeFalse();
    }

    #endregion
}
