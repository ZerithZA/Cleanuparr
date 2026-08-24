using Cleanuparr.Infrastructure.Features.Arr.Interfaces;
using Cleanuparr.Infrastructure.Features.ItemStriker;
using Cleanuparr.Infrastructure.Features.Ollama;
using Cleanuparr.Infrastructure.Interceptors;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Cleanuparr.Infrastructure.Features.Arr;

public class WhisparrV2Client : SonarrClient, IWhisparrV2Client
{
    public WhisparrV2Client(
        ILogger<WhisparrV2Client> logger,
        IHttpClientFactory httpClientFactory,
        IStriker striker,
        IDryRunInterceptor dryRunInterceptor,
        IOllamaClient ollamaClient,
        IAiImportBudget aiImportBudget,
        IMemoryCache cache
    ) : base(logger, httpClientFactory, striker, dryRunInterceptor, ollamaClient, aiImportBudget, cache)
    {
    }
}
