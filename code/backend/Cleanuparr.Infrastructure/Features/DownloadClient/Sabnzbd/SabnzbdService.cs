using Cleanuparr.Domain.Entities.HealthCheck;
using Cleanuparr.Infrastructure.Events.Interfaces;
using Cleanuparr.Infrastructure.Features.Files;
using Cleanuparr.Infrastructure.Features.ItemStriker;
using Cleanuparr.Infrastructure.Features.MalwareBlocker;
using Cleanuparr.Infrastructure.Http;
using Cleanuparr.Infrastructure.Interceptors;
using Cleanuparr.Infrastructure.Services.Interfaces;
using Cleanuparr.Persistence.Models.Configuration;
using Microsoft.Extensions.Logging;

namespace Cleanuparr.Infrastructure.Features.DownloadClient.Sabnzbd;

public partial class SabnzbdService : DownloadService, ISabnzbdService
{
    protected readonly ISabnzbdClientWrapper _client;

    public SabnzbdService(
        ILogger<SabnzbdService> logger,
        IFilenameEvaluator filenameEvaluator,
        IStriker striker,
        IDryRunInterceptor dryRunInterceptor,
        IHardLinkFileService hardLinkFileService,
        IDynamicHttpClientProvider httpClientProvider,
        IEventPublisher eventPublisher,
        IBlocklistProvider blocklistProvider,
        DownloadClientConfig downloadClientConfig,
        IQueueRuleEvaluator queueRuleEvaluator,
        ISeedingRuleEvaluator seedingRuleEvaluator
    ) : base(
        logger, filenameEvaluator, striker, dryRunInterceptor, hardLinkFileService,
        httpClientProvider, eventPublisher, blocklistProvider, downloadClientConfig, queueRuleEvaluator, seedingRuleEvaluator
    )
    {
        var sabnzbdClient = new SabnzbdClient(downloadClientConfig, _httpClient);
        _client = new SabnzbdClientWrapper(sabnzbdClient);
    }

    // Internal constructor for testing
    internal SabnzbdService(
        ILogger<SabnzbdService> logger,
        IFilenameEvaluator filenameEvaluator,
        IStriker striker,
        IDryRunInterceptor dryRunInterceptor,
        IHardLinkFileService hardLinkFileService,
        IDynamicHttpClientProvider httpClientProvider,
        IEventPublisher eventPublisher,
        IBlocklistProvider blocklistProvider,
        DownloadClientConfig downloadClientConfig,
        IQueueRuleEvaluator queueRuleEvaluator,
        ISeedingRuleEvaluator seedingRuleEvaluator,
        ISabnzbdClientWrapper clientWrapper
    ) : base(
        logger, filenameEvaluator, striker, dryRunInterceptor, hardLinkFileService,
        httpClientProvider, eventPublisher, blocklistProvider, downloadClientConfig, queueRuleEvaluator, seedingRuleEvaluator
    )
    {
        _client = clientWrapper;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// SABnzbd authenticates every call via the <c>apikey</c> query parameter rather than a
    /// session login, so there is nothing to do here beyond confirming an API key is configured.
    /// </remarks>
    public override Task LoginAsync()
    {
        if (string.IsNullOrEmpty(_downloadClientConfig.ApiKey))
        {
            _logger.LogDebug("No API key configured for client {clientId}", _downloadClientConfig.Id);
        }

        return Task.CompletedTask;
    }

    public override async Task<HealthCheckResult> HealthCheckAsync()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            string? version = await _client.GetVersionAsync();

            if (string.IsNullOrEmpty(version))
            {
                stopwatch.Stop();

                return new HealthCheckResult
                {
                    IsHealthy = false,
                    ErrorMessage = "SABnzbd did not return a version",
                    ResponseTime = stopwatch.Elapsed
                };
            }

            _logger.LogDebug("Health check: Successfully connected to SABnzbd client {clientId}", _downloadClientConfig.Id);

            stopwatch.Stop();

            return new HealthCheckResult
            {
                IsHealthy = true,
                ResponseTime = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogWarning(ex, "Health check failed for SABnzbd client {clientId}", _downloadClientConfig.Id);

            return new HealthCheckResult
            {
                IsHealthy = false,
                ErrorMessage = $"Connection failed: {ex.Message}",
                ResponseTime = stopwatch.Elapsed
            };
        }
    }

    public override void Dispose()
    {
    }
}
