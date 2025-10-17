using DocumentProcessingAPI.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace DocumentProcessingAPI.API.Controllers
{
    /// <summary>
    /// API controller for managing the Content Manager record sync scheduler
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class RecordSyncSchedulerController : ControllerBase
    {
        private readonly RecordSyncSchedulerService _schedulerService;
        private readonly ILogger<RecordSyncSchedulerController> _logger;

        public RecordSyncSchedulerController(
            RecordSyncSchedulerService schedulerService,
            ILogger<RecordSyncSchedulerController> logger)
        {
            _schedulerService = schedulerService;
            _logger = logger;
        }

        /// <summary>
        /// Get current scheduler status
        /// </summary>
        [HttpGet("status")]
        public async Task<ActionResult<SchedulerStatusDto>> GetStatus()
        {
            try
            {
                var status = await _schedulerService.GetStatusAsync();
                return Ok(status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get scheduler status");
                return StatusCode(500, new { error = "Failed to get scheduler status", message = ex.Message });
            }
        }

        /// <summary>
        /// Pause the scheduled sync job
        /// </summary>
        [HttpPost("pause")]
        public async Task<ActionResult> PauseJob()
        {
            try
            {
                var result = await _schedulerService.PauseJobAsync();
                if (result)
                {
                    return Ok(new { success = true, message = "Job paused successfully" });
                }
                else
                {
                    return BadRequest(new { success = false, message = "Failed to pause job" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to pause job");
                return StatusCode(500, new { error = "Failed to pause job", message = ex.Message });
            }
        }

        /// <summary>
        /// Resume the scheduled sync job
        /// </summary>
        [HttpPost("resume")]
        public async Task<ActionResult> ResumeJob()
        {
            try
            {
                var result = await _schedulerService.ResumeJobAsync();
                if (result)
                {
                    return Ok(new { success = true, message = "Job resumed successfully" });
                }
                else
                {
                    return BadRequest(new { success = false, message = "Failed to resume job" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resume job");
                return StatusCode(500, new { error = "Failed to resume job", message = ex.Message });
            }
        }

        /// <summary>
        /// Trigger the sync job immediately (outside of schedule)
        /// </summary>
        [HttpPost("trigger")]
        public async Task<ActionResult> TriggerJobNow()
        {
            try
            {
                var result = await _schedulerService.TriggerJobNowAsync();
                if (result)
                {
                    return Ok(new { success = true, message = "Job triggered successfully" });
                }
                else
                {
                    return BadRequest(new { success = false, message = "Failed to trigger job" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to trigger job");
                return StatusCode(500, new { error = "Failed to trigger job", message = ex.Message });
            }
        }

        /// <summary>
        /// Update the cron schedule
        /// </summary>
        /// <param name="request">Schedule update request</param>
        [HttpPut("schedule")]
        public async Task<ActionResult> UpdateSchedule([FromBody] UpdateScheduleRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.CronExpression))
                {
                    return BadRequest(new { success = false, message = "Cron expression is required" });
                }

                var result = await _schedulerService.UpdateScheduleAsync(request.CronExpression, request.SearchString);
                if (result)
                {
                    return Ok(new
                    {
                        success = true,
                        message = $"Schedule updated to: {request.CronExpression}",
                        cronExpression = request.CronExpression,
                        searchString = request.SearchString ?? "*"
                    });
                }
                else
                {
                    return BadRequest(new { success = false, message = "Failed to update schedule. Check if cron expression is valid." });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update schedule");
                return StatusCode(500, new { error = "Failed to update schedule", message = ex.Message });
            }
        }

        /// <summary>
        /// Enable or disable the sync
        /// </summary>
        /// <param name="request">Enable/disable request</param>
        [HttpPut("enable")]
        public async Task<ActionResult> SetSyncEnabled([FromBody] SetSyncEnabledRequest request)
        {
            try
            {
                var result = await _schedulerService.SetSyncEnabledAsync(request.Enabled);
                if (result)
                {
                    return Ok(new
                    {
                        success = true,
                        message = request.Enabled ? "Sync enabled" : "Sync disabled",
                        enabled = request.Enabled
                    });
                }
                else
                {
                    return BadRequest(new { success = false, message = "Failed to update sync enabled state" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set sync enabled");
                return StatusCode(500, new { error = "Failed to set sync enabled", message = ex.Message });
            }
        }

        /// <summary>
        /// Get information about common cron expressions
        /// </summary>
        [HttpGet("cron-examples")]
        public ActionResult<CronExamplesResponse> GetCronExamples()
        {
            return Ok(new CronExamplesResponse
            {
                Examples = new List<CronExample>
                {
                    new CronExample { Expression = "0 0 * * * ?", Description = "Every hour at minute 0" },
                    new CronExample { Expression = "0 0/30 * * * ?", Description = "Every 30 minutes" },
                    new CronExample { Expression = "0 0 0/2 * * ?", Description = "Every 2 hours" },
                    new CronExample { Expression = "0 0 9-17 * * ?", Description = "Every hour between 9 AM and 5 PM" },
                    new CronExample { Expression = "0 0 12 * * ?", Description = "Every day at noon" },
                    new CronExample { Expression = "0 0 0 * * ?", Description = "Every day at midnight" },
                    new CronExample { Expression = "0 0 0 ? * MON", Description = "Every Monday at midnight" },
                    new CronExample { Expression = "0 0 0 1 * ?", Description = "First day of every month at midnight" }
                },
                Format = "second minute hour dayOfMonth month dayOfWeek",
                Note = "Use ? for dayOfMonth or dayOfWeek when the other is specified"
            });
        }
    }

    /// <summary>
    /// Request to update schedule
    /// </summary>
    public class UpdateScheduleRequest
    {
        public string CronExpression { get; set; } = string.Empty;
        public string? SearchString { get; set; }
    }

    /// <summary>
    /// Request to enable/disable sync
    /// </summary>
    public class SetSyncEnabledRequest
    {
        public bool Enabled { get; set; }
    }

    /// <summary>
    /// Response with cron expression examples
    /// </summary>
    public class CronExamplesResponse
    {
        public List<CronExample> Examples { get; set; } = new();
        public string Format { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
    }

    /// <summary>
    /// Cron expression example
    /// </summary>
    public class CronExample
    {
        public string Expression { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
