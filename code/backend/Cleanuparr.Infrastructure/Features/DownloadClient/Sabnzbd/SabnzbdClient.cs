using System.Text.Json;
using Cleanuparr.Domain.Entities.Sabnzbd.Response;
using Cleanuparr.Domain.Exceptions;
using Cleanuparr.Infrastructure.Json;
using Cleanuparr.Persistence.Models.Configuration;

namespace Cleanuparr.Infrastructure.Features.DownloadClient.Sabnzbd;

/// <summary>
/// Low-level REST client for communicating with SABnzbd's JSON API.
/// </summary>
/// <remarks>
/// SABnzbd authenticates every request via an <c>apikey</c> query parameter rather than a session
/// or basic auth header. Every call below is a GET (or POST for file uploads) against the single
/// <c>/api</c> endpoint, distinguished by the <c>mode</c> query parameter.
/// </remarks>
public sealed class SabnzbdClient
{
    private readonly DownloadClientConfig _config;
    private readonly HttpClient _httpClient;

    public SabnzbdClient(DownloadClientConfig config, HttpClient httpClient)
    {
        _config = config;
        _httpClient = httpClient;
    }

    /// <summary>
    /// Gets the SABnzbd version, used as a lightweight health/auth check.
    /// </summary>
    public async Task<string?> GetVersionAsync()
    {
        var response = await SendRequest<SabnzbdVersionResponse>("version");
        return response?.Version;
    }

    /// <summary>
    /// Gets the active queue (jobs that are downloading, queued, paused, or otherwise in progress).
    /// </summary>
    public async Task<SabnzbdQueue> GetQueueAsync()
    {
        var response = await SendRequest<SabnzbdQueueResponse>("queue");
        return response?.Queue ?? new SabnzbdQueue();
    }

    /// <summary>
    /// Gets the history (jobs that have completed or failed).
    /// </summary>
    /// <param name="limit">Maximum number of history entries to return. SABnzbd defaults to 0 (no limit) when not specified.</param>
    public async Task<SabnzbdHistory> GetHistoryAsync(int limit = 0)
    {
        var parameters = new Dictionary<string, string>();

        if (limit > 0)
        {
            parameters["limit"] = limit.ToString();
        }

        var response = await SendRequest<SabnzbdHistoryResponse>("history", parameters);
        return response?.History ?? new SabnzbdHistory();
    }

    /// <summary>
    /// Adds an NZB by URL.
    /// </summary>
    /// <returns>The nzo_id(s) assigned to the newly added job(s).</returns>
    public async Task<List<string>> AddByUrlAsync(string url, string? category = null)
    {
        var parameters = new Dictionary<string, string> { ["name"] = url };

        if (!string.IsNullOrWhiteSpace(category))
        {
            parameters["cat"] = category;
        }

        var response = await SendRequest<SabnzbdBasicResponse>("addurl", parameters);
        EnsureSuccess(response, "addurl");
        return response?.NzoIds ?? [];
    }

    /// <summary>
    /// Removes a job from the queue or history.
    /// </summary>
    /// <param name="nzoId">The nzo_id of the job to delete.</param>
    /// <param name="deleteFiles">Whether to also delete the downloaded/incomplete files on disk.</param>
    /// <param name="fromHistory">Whether the job lives in history (mode=history) instead of the queue (mode=queue).</param>
    public async Task DeleteAsync(string nzoId, bool deleteFiles, bool fromHistory)
    {
        var parameters = new Dictionary<string, string>
        {
            ["name"] = "delete",
            ["value"] = nzoId,
            ["del_files"] = deleteFiles ? "1" : "0",
        };

        string mode = fromHistory ? "history" : "queue";
        var response = await SendRequest<SabnzbdBasicResponse>(mode, parameters);
        EnsureSuccess(response, mode);
    }

    /// <summary>
    /// Changes the category of a queued job.
    /// </summary>
    public async Task ChangeCategoryAsync(string nzoId, string category)
    {
        var parameters = new Dictionary<string, string>
        {
            ["name"] = "change_cat",
            ["value"] = nzoId,
            ["value2"] = category,
        };

        var response = await SendRequest<SabnzbdBasicResponse>("queue", parameters);
        EnsureSuccess(response, "change_cat");
    }

    /// <summary>
    /// Creates a category. SABnzbd has no dedicated "create category" API call;
    /// categories are defined in server config, so this adds the category to the config.
    /// </summary>
    public async Task CreateCategoryAsync(string name)
    {
        var parameters = new Dictionary<string, string>
        {
            ["name"] = "set_config",
            ["section"] = "categories",
            ["keyword"] = name,
        };

        var response = await SendRequest<SabnzbdBasicResponse>("set_config", parameters);
        EnsureSuccess(response, "set_config");
    }

    private static void EnsureSuccess(SabnzbdBasicResponse? response, string mode)
    {
        if (response is { Status: false })
        {
            throw new SabnzbdClientException($"SABnzbd '{mode}' call failed: {response.Error ?? "unknown error"}");
        }
    }

    private async Task<T?> SendRequest<T>(string mode, Dictionary<string, string>? parameters = null)
    {
        UriBuilder uriBuilder = new(_config.Url);
        uriBuilder.Path = $"{uriBuilder.Path.TrimEnd('/')}/api";

        var query = new List<string>
        {
            $"mode={Uri.EscapeDataString(mode)}",
            $"apikey={Uri.EscapeDataString(_config.ApiKey ?? string.Empty)}",
            "output=json",
        };

        if (parameters is not null)
        {
            foreach (var (key, value) in parameters)
            {
                query.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");
            }
        }

        uriBuilder.Query = string.Join("&", query);

        HttpResponseMessage responseMessage;

        try
        {
            responseMessage = await _httpClient.GetAsync(uriBuilder.Uri);
        }
        catch (HttpRequestException exception)
        {
            throw new SabnzbdClientException($"failed to reach SABnzbd for mode '{mode}'", exception);
        }

        responseMessage.EnsureSuccessStatusCode();

        string responseJson = await responseMessage.Content.ReadAsStringAsync();

        try
        {
            return JsonSerializer.Deserialize<T>(responseJson, CleanuparrJsonOptions.ExternalApiRead);
        }
        catch (JsonException exception)
        {
            throw new SabnzbdClientException(
                $"failed to deserialize SABnzbd response for mode '{mode}': {Truncate(responseJson)}",
                exception);
        }
    }

    private static string Truncate(string responseJson) =>
        responseJson.Length > 2000 ? responseJson[..2000] : responseJson;
}
