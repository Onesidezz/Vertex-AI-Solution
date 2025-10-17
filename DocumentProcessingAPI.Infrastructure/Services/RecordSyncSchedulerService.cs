using DocumentProcessingAPI.Infrastructure.Jobs;
using Microsoft.Extensions.Logging;
using Quartz;

namespace DocumentProcessingAPI.Infrastructure.Services
{
    /// <summary>
    /// Service to manage the Record Sync scheduler
    /// Provides methods to start, stop, pause, and monitor the sync job
    /// </summary>
    public class RecordSyncSchedulerService
    {
        private readonly IScheduler _scheduler;
        private readonly ILogger<RecordSyncSchedulerService> _logger;
        private const string JOB_KEY = "record-sync-job";
        private const string TRIGGER_KEY = "record-sync-trigger";
        private const string GROUP_NAME = "content-manager-sync";

        public RecordSyncSchedulerService(ISchedulerFactory schedulerFactory, ILogger<RecordSyncSchedulerService> logger)
        {
            _scheduler = schedulerFactory.GetScheduler().Result;
            _logger = logger;
        }

        /// <summary>
        /// Get scheduler status and job information
        /// </summary>
        public async Task<SchedulerStatusDto> GetStatusAsync()
        {
            try
            {
                var jobKey = new JobKey(JOB_KEY, GROUP_NAME);
                var triggerKey = new TriggerKey(TRIGGER_KEY, GROUP_NAME);

                var isSchedulerRunning = _scheduler.IsStarted;
                var jobExists = await _scheduler.CheckExists(jobKey);

                if (!jobExists)
                {
                    return new SchedulerStatusDto
                    {
                        IsRunning = false,
                        IsJobScheduled = false,
                        Message = "Job is not scheduled"
                    };
                }

                var trigger = await _scheduler.GetTrigger(triggerKey) as ICronTrigger;
                var jobDetail = await _scheduler.GetJobDetail(jobKey);
                var triggerState = await _scheduler.GetTriggerState(triggerKey);

                // Get job data
                var dataMap = jobDetail?.JobDataMap;
                var searchString = dataMap?.GetString("SearchString") ?? "*";
                var enableSync = dataMap?.GetBooleanValue("EnableSync") ?? true;

                // Get next fire times
                var nextFireTime = await _scheduler.GetTrigger(triggerKey);
                var previousFireTime = nextFireTime?.GetPreviousFireTimeUtc();

                return new SchedulerStatusDto
                {
                    IsRunning = isSchedulerRunning,
                    IsJobScheduled = true,
                    IsPaused = triggerState == TriggerState.Paused,
                    IsEnabled = enableSync,
                    CronExpression = trigger?.CronExpressionString ?? "",
                    SearchString = searchString,
                    NextRunTime = nextFireTime?.GetNextFireTimeUtc()?.DateTime,
                    PreviousRunTime = previousFireTime?.DateTime,
                    Message = triggerState == TriggerState.Paused ? "Job is paused" : "Job is active"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get scheduler status");
                return new SchedulerStatusDto
                {
                    IsRunning = false,
                    IsJobScheduled = false,
                    Message = $"Error: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Pause the scheduled job
        /// </summary>
        public async Task<bool> PauseJobAsync()
        {
            try
            {
                var triggerKey = new TriggerKey(TRIGGER_KEY, GROUP_NAME);
                await _scheduler.PauseTrigger(triggerKey);
                _logger.LogInformation("⏸️ Record sync job paused");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to pause job");
                return false;
            }
        }

        /// <summary>
        /// Resume the scheduled job
        /// </summary>
        public async Task<bool> ResumeJobAsync()
        {
            try
            {
                var triggerKey = new TriggerKey(TRIGGER_KEY, GROUP_NAME);
                await _scheduler.ResumeTrigger(triggerKey);
                _logger.LogInformation("▶️ Record sync job resumed");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resume job");
                return false;
            }
        }

        /// <summary>
        /// Trigger the job immediately (outside of schedule)
        /// </summary>
        public async Task<bool> TriggerJobNowAsync()
        {
            try
            {
                var jobKey = new JobKey(JOB_KEY, GROUP_NAME);
                await _scheduler.TriggerJob(jobKey);
                _logger.LogInformation("🚀 Record sync job triggered manually");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to trigger job");
                return false;
            }
        }

        /// <summary>
        /// Update the cron schedule
        /// </summary>
        public async Task<bool> UpdateScheduleAsync(string cronExpression, string? searchString = null)
        {
            try
            {
                var triggerKey = new TriggerKey(TRIGGER_KEY, GROUP_NAME);
                var jobKey = new JobKey(JOB_KEY, GROUP_NAME);

                // Validate cron expression
                if (!CronExpression.IsValidExpression(cronExpression))
                {
                    _logger.LogWarning("Invalid cron expression: {CronExpression}", cronExpression);
                    return false;
                }

                // Get existing job
                var jobDetail = await _scheduler.GetJobDetail(jobKey);
                if (jobDetail == null)
                {
                    _logger.LogWarning("Job not found: {JobKey}", jobKey);
                    return false;
                }

                // Update search string if provided
                if (!string.IsNullOrEmpty(searchString))
                {
                    jobDetail.JobDataMap.Put("SearchString", searchString);
                    await _scheduler.AddJob(jobDetail, true);
                }

                // Create new trigger with updated schedule
                var newTrigger = TriggerBuilder.Create()
                    .WithIdentity(triggerKey)
                    .WithCronSchedule(cronExpression)
                    .WithDescription($"Sync Content Manager records: {cronExpression}")
                    .Build();

                await _scheduler.RescheduleJob(triggerKey, newTrigger);

                _logger.LogInformation("✅ Schedule updated to: {CronExpression}", cronExpression);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update schedule");
                return false;
            }
        }

        /// <summary>
        /// Enable or disable the sync
        /// </summary>
        public async Task<bool> SetSyncEnabledAsync(bool enabled)
        {
            try
            {
                var jobKey = new JobKey(JOB_KEY, GROUP_NAME);
                var jobDetail = await _scheduler.GetJobDetail(jobKey);

                if (jobDetail == null)
                {
                    _logger.LogWarning("Job not found: {JobKey}", jobKey);
                    return false;
                }

                jobDetail.JobDataMap.Put("EnableSync", enabled);
                await _scheduler.AddJob(jobDetail, true);

                _logger.LogInformation(enabled ? "✅ Sync enabled" : "⏸️ Sync disabled");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set sync enabled state");
                return false;
            }
        }

        /// <summary>
        /// Get job execution history (last result)
        /// </summary>
        public async Task<JobExecutionResult?> GetLastExecutionResultAsync()
        {
            try
            {
                var jobKey = new JobKey(JOB_KEY, GROUP_NAME);
                var currentlyExecuting = await _scheduler.GetCurrentlyExecutingJobs();

                var executingJob = currentlyExecuting.FirstOrDefault(j => j.JobDetail.Key.Equals(jobKey));
                if (executingJob != null && executingJob.Result is JobExecutionResult result)
                {
                    return result;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get last execution result");
                return null;
            }
        }
    }

    /// <summary>
    /// DTO for scheduler status
    /// </summary>
    public class SchedulerStatusDto
    {
        public bool IsRunning { get; set; }
        public bool IsJobScheduled { get; set; }
        public bool IsPaused { get; set; }
        public bool IsEnabled { get; set; }
        public string CronExpression { get; set; } = string.Empty;
        public string SearchString { get; set; } = "*";
        public DateTime? NextRunTime { get; set; }
        public DateTime? PreviousRunTime { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
