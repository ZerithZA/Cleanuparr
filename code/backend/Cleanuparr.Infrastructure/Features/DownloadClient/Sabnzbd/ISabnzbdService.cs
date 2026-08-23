namespace Cleanuparr.Infrastructure.Features.DownloadClient.Sabnzbd;

/// <summary>
/// SABnzbd-specific download service contract. Mirrors <see cref="IDownloadService"/>; no
/// SABnzbd-specific extra members are currently needed.
/// </summary>
public interface ISabnzbdService : IDownloadService, IDisposable
{
}
