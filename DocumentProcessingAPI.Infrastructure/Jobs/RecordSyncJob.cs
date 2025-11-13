using DocumentProcessingAPI.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Quartz;

namespace DocumentProcessingAPI.Infrastructure.Jobs
{
    /// <summary>
    /// Quartz job that syncs Content Manager records on a schedule
    /// Processes all records and generates embeddings automatically
    /// </summary>
    [DisallowConcurrentExecution] // Prevent multiple instances from running simultaneously
    public class RecordSyncJob : IJob
    {
        private readonly IRecordEmbeddingService _recordEmbeddingService;
        private readonly ILogger<RecordSyncJob> _logger;

        public RecordSyncJob(
            IRecordEmbeddingService recordEmbeddingService,
            ILogger<RecordSyncJob> logger)
        {
            _recordEmbeddingService = recordEmbeddingService;
            _logger = logger;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            var jobId = context.FireInstanceId;
            var scheduledTime = context.ScheduledFireTimeUtc?.ToLocalTime();
            var actualTime = context.FireTimeUtc.ToLocalTime();

            _logger.LogInformation("========================================");
            _logger.LogInformation("🔄 Content Manager Record Sync Job Started");
            _logger.LogInformation("Job ID: {JobId}", jobId);
            _logger.LogInformation("Scheduled Time: {ScheduledTime}", scheduledTime);
            _logger.LogInformation("Actual Start Time: {ActualTime}", actualTime);
            _logger.LogInformation("========================================");

            try
            {
                // Get search string from job data map (if provided)
                var dataMap = context.MergedJobDataMap;
                var searchString = dataMap.GetString("SearchString") ?? "*";
                var enableSync = dataMap.GetBooleanValue("EnableSync");

                if (!enableSync)
                {
                    _logger.LogInformation("⏸️ Record sync is disabled. Skipping job execution.");
                    return;
                }

                _logger.LogInformation("📋 Processing records with search criteria: {SearchString}", searchString);

                // Process all records from Content Manager with cancellation support
                var processedCount = await _recordEmbeddingService.ProcessAllRecordsAsync(searchString, context.CancellationToken);

                var duration = DateTime.UtcNow - context.FireTimeUtc;

                _logger.LogInformation("========================================");
                _logger.LogInformation("✅ Content Manager Record Sync Job Completed");
                _logger.LogInformation("Records Processed: {ProcessedCount}", processedCount);
                _logger.LogInformation("Duration: {Duration}", duration);
                _logger.LogInformation("Next Scheduled Run: {NextRun}", context.NextFireTimeUtc?.ToLocalTime());
                _logger.LogInformation("========================================");

                // Store result in job context for monitoring
                context.Result = new JobExecutionResult
                {
                    Success = true,
                    RecordsProcessed = processedCount,
                    Duration = duration,
                    CompletedAt = DateTime.UtcNow,
                    Message = $"Successfully processed {processedCount} records"
                };
            }
            catch (OperationCanceledException ex)
            {
                var duration = DateTime.UtcNow - context.FireTimeUtc;

                _logger.LogWarning("⚠️ Content Manager Record Sync Job was cancelled");
                _logger.LogWarning("Duration before cancellation: {Duration}", duration);

                // Store cancellation in job context
                context.Result = new JobExecutionResult
                {
                    Success = false,
                    RecordsProcessed = 0,
                    Duration = duration,
                    CompletedAt = DateTime.UtcNow,
                    Message = "Job was cancelled",
                    ErrorDetails = ex.ToString()
                };

                // Don't refire - cancellation is intentional
                throw new JobExecutionException(ex, refireImmediately: false);
            }
            catch (Exception ex)
            {
                var duration = DateTime.UtcNow - context.FireTimeUtc;

                _logger.LogError(ex, "❌ Content Manager Record Sync Job Failed");
                _logger.LogError("Error: {ErrorMessage}", ex.Message);
                _logger.LogError("Duration before failure: {Duration}", duration);

                // Store error in job context
                context.Result = new JobExecutionResult
                {
                    Success = false,
                    RecordsProcessed = 0,
                    Duration = duration,
                    CompletedAt = DateTime.UtcNow,
                    Message = $"Job failed: {ex.Message}",
                    ErrorDetails = ex.ToString()
                };

                // Optionally rethrow to let Quartz handle retry logic
                throw new JobExecutionException(ex, refireImmediately: false);
            }
        }
    }

    /// <summary>
    /// Result of job execution for monitoring
    /// </summary>
    public class JobExecutionResult
    {
        public bool Success { get; set; }
        public int RecordsProcessed { get; set; }
        public TimeSpan Duration { get; set; }
        public DateTime CompletedAt { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? ErrorDetails { get; set; }
    }
}
