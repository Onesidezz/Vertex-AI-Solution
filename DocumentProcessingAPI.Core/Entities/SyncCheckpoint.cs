namespace DocumentProcessingAPI.Core.Entities;

/// <summary>
/// Entity for tracking sync job progress and enabling resumable processing
/// Stores the last successful sync state for each job to support checkpoint/resume functionality
/// </summary>
public class SyncCheckpoint
{
    /// <summary>
    /// Primary key - Auto-increment ID
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Unique job identifier
    /// Example: "RecordSyncJob"
    /// </summary>
    public string JobName { get; set; } = string.Empty;

    /// <summary>
    /// Last successful sync completion timestamp
    /// Used to query records modified since this date (incremental sync)
    /// </summary>
    public DateTime? LastSyncDate { get; set; }

    /// <summary>
    /// Last processed page number (for pagination)
    /// Enables resuming from last checkpoint if job crashes mid-process
    /// </summary>
    public int LastProcessedPage { get; set; }

    /// <summary>
    /// Current status of the job
    /// Values: "Running", "Completed", "Failed", "Paused"
    /// </summary>
    public string Status { get; set; } = "Completed";

    /// <summary>
    /// Total records processed in current/last run
    /// </summary>
    public long TotalRecordsProcessed { get; set; }

    /// <summary>
    /// Total records successfully embedded in current/last run
    /// </summary>
    public long SuccessCount { get; set; }

    /// <summary>
    /// Total records that failed processing in current/last run
    /// </summary>
    public long FailureCount { get; set; }

    /// <summary>
    /// Error message if job failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Timestamp when this checkpoint record was created
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Timestamp when this checkpoint was last updated
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}
