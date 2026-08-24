using System.Text.Json;
using Cleanuparr.Api.Extensions;
using Cleanuparr.Api.Features.QueueCleaner.Contracts.Requests;
using Cleanuparr.Domain.Enums;
using Cleanuparr.Infrastructure.Features.Ollama;
using Cleanuparr.Infrastructure.Services.Interfaces;
using Cleanuparr.Infrastructure.Utilities;
using Cleanuparr.Persistence;
using Cleanuparr.Persistence.Models.Configuration;
using Cleanuparr.Persistence.Models.Configuration.QueueCleaner;
using Cleanuparr.Shared.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Cleanuparr.Api.Features.QueueCleaner.Controllers;

[ApiController]
[Route("api/configuration")]
[Authorize]
public sealed class QueueCleanerConfigController : ControllerBase
{
    /// <summary>
    /// Shared prefix for both AI-import cache key families written by <c>SonarrClient</c>
    /// (<c>ai_import_{downloadId}_{url}</c> decision entries and
    /// <c>ai_import_skips_{downloadId}_{url}</c> consecutive-skip counters). Purging by this
    /// prefix catches both families without the two key-building helpers needing to be shared
    /// or duplicated here.
    /// </summary>
    private const string AiImportCacheKeyPrefix = "ai_import_";

    /// <summary>
    /// Timeout for the Ollama <c>/api/tags</c> connectivity probe. This is a lightweight
    /// reachability check, not a classification call, so it does not need the full
    /// <c>AiImportConfig.TimeoutSeconds</c> budget.
    /// </summary>
    private static readonly TimeSpan OllamaConnectionTestTimeout = TimeSpan.FromSeconds(5);

    private readonly ILogger<QueueCleanerConfigController> _logger;
    private readonly DataContext _dataContext;
    private readonly IJobManagementService _jobManagementService;
    private readonly MemoryCache _memoryCache;
    private readonly IAiImportBudget _aiImportBudget;
    private readonly IHttpClientFactory _httpClientFactory;

    public QueueCleanerConfigController(
        ILogger<QueueCleanerConfigController> logger,
        DataContext dataContext,
        IJobManagementService jobManagementService,
        MemoryCache memoryCache,
        IAiImportBudget aiImportBudget,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _dataContext = dataContext;
        _jobManagementService = jobManagementService;
        _memoryCache = memoryCache;
        _aiImportBudget = aiImportBudget;
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet("queue_cleaner")]
    public async Task<IActionResult> GetQueueCleanerConfig()
    {
        await DataContext.Lock.WaitAsync();
        try
        {
            var config = await _dataContext.QueueCleanerConfigs
                .AsNoTracking()
                .FirstAsync();
            return Ok(config);
        }
        finally
        {
            DataContext.Lock.Release();
        }
    }

    [HttpPut("queue_cleaner")]
    public async Task<IActionResult> UpdateQueueCleanerConfig([FromBody] UpdateQueueCleanerConfigRequest newConfigDto)
    {
        await DataContext.Lock.WaitAsync();
        try
        {
            if (!string.IsNullOrEmpty(newConfigDto.CronExpression))
            {
                CronValidationHelper.ValidateCronExpression(newConfigDto.CronExpression);
            }

            var oldConfig = await _dataContext.QueueCleanerConfigs
                .FirstAsync();

            bool aiImportWasEnabled = oldConfig.AiImport.Enabled;

            oldConfig.Enabled = newConfigDto.Enabled;
            oldConfig.CronExpression = newConfigDto.CronExpression;
            oldConfig.UseAdvancedScheduling = newConfigDto.UseAdvancedScheduling;
            oldConfig.FailedImport = newConfigDto.FailedImport;
            oldConfig.AiImport = newConfigDto.AiImport;
            oldConfig.DownloadingMetadataMaxStrikes = newConfigDto.DownloadingMetadataMaxStrikes;
            oldConfig.ProcessNoContentId = newConfigDto.ProcessNoContentId;
            oldConfig.IgnoredDownloads = newConfigDto.IgnoredDownloads;

            oldConfig.Validate();

            await _dataContext.SaveChangesAsync();

            if (aiImportWasEnabled && !oldConfig.AiImport.Enabled)
            {
                await PurgeAiImportDecisionCache();
            }

            await UpdateJobSchedule(oldConfig, JobType.QueueCleaner);

            return Ok(new { Message = "QueueCleaner configuration updated successfully" });
        }
        finally
        {
            DataContext.Lock.Release();
        }
    }

    [HttpPost("queue_cleaner/ai_import/reset_breaker")]
    public IActionResult ResetAiImportCircuitBreaker()
    {
        _aiImportBudget.Reset();

        return Ok(new { Message = "AI import circuit breaker reset successfully" });
    }

    [HttpPost("queue_cleaner/ai_import/test_ollama")]
    public async Task<IActionResult> TestOllamaConnection([FromBody] TestOllamaConnectionRequest request)
    {
        Uri tagsUri;
        try
        {
            tagsUri = new Uri(new Uri(request.OllamaUrl), "/api/tags");
        }
        catch (UriFormatException ex)
        {
            return this.ProblemResult(StatusCodes.Status400BadRequest, $"Invalid Ollama URL: {ex.Message}");
        }

        HttpClient httpClient = _httpClientFactory.CreateClient(Constants.HttpClientOllamaName);

        using CancellationTokenSource cts = new(OllamaConnectionTestTimeout);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(tagsUri, cts.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "Ollama connectivity test to {Url} failed or timed out", request.OllamaUrl);
            return this.ProblemResult(StatusCodes.Status400BadRequest, $"Unable to reach Ollama at {request.OllamaUrl}: {ex.Message}");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return this.ProblemResult(
                    StatusCodes.Status400BadRequest,
                    $"Ollama responded with an unexpected status code: {(int)response.StatusCode}");
            }

            string body = await response.Content.ReadAsStringAsync();
            OllamaTagsResponse? tags;
            try
            {
                tags = JsonSerializer.Deserialize<OllamaTagsResponse>(body);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Ollama connectivity test to {Url} returned an unparsable response", request.OllamaUrl);
                return this.ProblemResult(StatusCodes.Status400BadRequest, "Connected, but Ollama returned an unexpected response");
            }

            List<string> models = tags?.Models?
                .Select(m => m.Name)
                .Where(name => !string.IsNullOrEmpty(name))
                .Select(name => name!)
                .ToList() ?? [];

            return Ok(new
            {
                Message = $"Connected — {models.Count} model(s) available",
                Models = models,
            });
        }
    }

    /// <summary>
    /// Purges all AI decision-cache and skip-counter entries (AC-38) when AI-assisted import is
    /// disabled, so a stale cached decision or consecutive-skip count cannot suppress the normal
    /// strike/import path after the feature is re-enabled. <see cref="SonarrClient"/> keys both
    /// entry families under the shared <c>ai_import_</c> prefix and never registers a bulk
    /// eviction token for them, so the app-wide singleton <see cref="MemoryCache"/> is enumerated
    /// via its <c>Keys</c> property and matching entries are removed individually.
    /// </summary>
    private Task PurgeAiImportDecisionCache()
    {
        List<object> keysToRemove = _memoryCache.Keys
            .Where(key => key is string s && s.StartsWith(AiImportCacheKeyPrefix, StringComparison.Ordinal))
            .ToList();

        foreach (object key in keysToRemove)
        {
            _memoryCache.Remove(key);
        }

        _logger.LogInformation(
            "purged {count} AI-assisted import cache entries after AiImport.Enabled transitioned to false",
            keysToRemove.Count);

        return Task.CompletedTask;
    }

    private async Task UpdateJobSchedule(IJobConfig config, JobType jobType)
    {
        if (config.Enabled)
        {
            if (!string.IsNullOrEmpty(config.CronExpression))
            {
                _logger.LogInformation("{name} is enabled, updating job schedule with cron expression: {CronExpression}",
                    jobType.ToString(), config.CronExpression);

                await _jobManagementService.StartJob(jobType, null, config.CronExpression);
            }
            else
            {
                _logger.LogWarning("{name} is enabled, but no cron expression was found in the configuration", jobType.ToString());
            }

            return;
        }

        _logger.LogInformation("{name} is disabled, stopping the job", jobType.ToString());
        await _jobManagementService.StopJob(jobType);
    }
}
