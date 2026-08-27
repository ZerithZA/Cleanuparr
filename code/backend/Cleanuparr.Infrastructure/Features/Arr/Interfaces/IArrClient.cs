using Cleanuparr.Domain.Entities.Arr;
using Cleanuparr.Domain.Entities.Arr.Queue;
using Cleanuparr.Domain.Enums;
using Cleanuparr.Infrastructure.Features.Ollama;
using Cleanuparr.Persistence.Models.Configuration.Arr;

namespace Cleanuparr.Infrastructure.Features.Arr.Interfaces;

public interface IArrClient
{
    Task<QueueListResponse> GetQueueItemsAsync(ArrInstance arrInstance, int page);

    /// <summary>
    /// Determines whether a queue record should be removed from the *arr queue (and struck)
    /// due to a failed import.
    /// </summary>
    /// <param name="instanceType">The type of the *arr instance hosting the queue item.</param>
    /// <param name="record">The queue record to evaluate.</param>
    /// <param name="isPrivateDownload">Whether the download is from a private tracker.</param>
    /// <param name="arrMaxStrikes">The configured max strikes for this *arr instance.</param>
    /// <param name="bypassFailedImportPatternFilter">
    /// When <c>true</c>, the caller has already determined (via AI-assisted import) that this
    /// record cannot be resolved, so the user-configured failed-import pattern
    /// inclusion/exclusion check is bypassed entirely. The record is still subject to the same
    /// failed-import candidate state check (warning + importBlocked/importPending/
    /// importFailed, etc.) and the same strike-counting/max-strikes mechanics - only the
    /// pattern-based gate is skipped. Defaults to <c>false</c>, preserving existing behavior.
    /// </param>
    Task<bool> ShouldRemoveFromQueue(InstanceType instanceType, QueueRecord record, bool isPrivateDownload, short arrMaxStrikes, bool bypassFailedImportPatternFilter = false);

    /// <summary>
    /// Attempts an AI-assisted manual import for a queue record that failed automatic import.
    /// Meaningfully implemented only by <see cref="Cleanuparr.Infrastructure.Features.Arr.SonarrClient"/>;
    /// every other implementer returns <see cref="AiImportOutcome.Skipped"/> unconditionally via
    /// the base implementation on the abstract <c>ArrClient</c> class.
    /// </summary>
    /// <param name="instance">The *arr instance hosting the queue item.</param>
    /// <param name="record">The queue record to evaluate.</param>
    /// <param name="isPrivateDownload">Whether the download is from a private tracker.</param>
    Task<AiImportOutcome> TryAiAssistedImportAsync(ArrInstance instance, QueueRecord record, bool isPrivateDownload);

    /// <summary>
    /// Removes a queue item from the *arr instance.
    /// </summary>
    /// <param name="arrInstance">The *arr instance hosting the queue item.</param>
    /// <param name="record">The queue record to remove.</param>
    /// <param name="removeFromClient">When true, also delete the download from the download client. Ignored when <paramref name="changeCategory"/> is true.</param>
    /// <param name="changeCategory">When true, instructs the *arr to change the download's category to the post-import category instead of removing it from the download client. Mutually exclusive with <paramref name="removeFromClient"/>.</param>
    /// <param name="deleteReason">Reason for removal, used for logging and event publishing.</param>
    Task DeleteQueueItemAsync(ArrInstance arrInstance, QueueRecord record, bool removeFromClient, bool changeCategory, DeleteReason deleteReason);

    /// <summary>
    /// Triggers a search for the specified items and returns the arr command IDs
    /// </summary>
    Task<List<long>> SearchItemsAsync(ArrInstance arrInstance, HashSet<SearchItem>? items);

    /// <summary>
    /// Triggers a search for a single item and returns the arr command ID
    /// </summary>
    Task<long> SearchItemAsync(ArrInstance arrInstance, SearchItem item);

    /// <summary>
    /// Gets the status of an arr command by its ID
    /// </summary>
    Task<ArrCommandStatus> GetCommandStatusAsync(ArrInstance arrInstance, long commandId);

    /// <summary>
    /// Gets every command the arr instance currently knows about
    /// </summary>
    Task<List<ArrCommandStatus>> GetCommandsAsync(ArrInstance arrInstance);

    bool IsRecordValid(QueueRecord record);

    /// <summary>
    /// Checks whether the record has an id (movie id, tv show id etc.)
    /// </summary>
    /// <param name="record">The record to check</param>
    /// <returns>True if the record has an id, false otherwise</returns>
    bool HasContentId(QueueRecord record);

    /// <summary>
    /// Tests the connection to an Arr instance
    /// </summary>
    /// <param name="arrInstance">The instance to test connection to</param>
    /// <returns>Task that completes when the connection test is done</returns>
    Task HealthCheckAsync(ArrInstance arrInstance);

    /// <summary>
    /// Returns the number of items actively downloading (SizeLeft > 0) across all queue pages.
    /// Items that are completed, import-blocked, or otherwise finished are not counted.
    /// </summary>
    Task<int> GetActiveDownloadCountAsync(ArrInstance arrInstance);

    Task<List<Tag>> GetAllTagsAsync(ArrInstance arrInstance);
}