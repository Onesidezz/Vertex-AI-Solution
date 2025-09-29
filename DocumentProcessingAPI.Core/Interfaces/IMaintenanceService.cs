namespace DocumentProcessingAPI.Core.Interfaces;

/// <summary>
/// Service interface for maintenance operations
/// </summary>
public interface IMaintenanceService
{
    /// <summary>
    /// Reprocess all stored document chunks to fix text spacing issues
    /// </summary>
    /// <returns>Number of chunks reprocessed</returns>
    Task<int> ReprocessAllChunksForTextSpacingAsync();

    /// <summary>
    /// Reprocess chunks for a specific document
    /// </summary>
    /// <param name="documentId">Document ID to reprocess</param>
    /// <returns>Number of chunks reprocessed</returns>
    Task<int> ReprocessDocumentChunksAsync(Guid documentId);

    /// <summary>
    /// Get maintenance statistics
    /// </summary>
    /// <returns>Maintenance statistics</returns>
    Task<MaintenanceStatsDto> GetMaintenanceStatsAsync();
}

public class MaintenanceStatsDto
{
    public int TotalDocuments { get; set; }
    public int TotalChunks { get; set; }
    public int ChunksWithSpacingIssues { get; set; }
    public DateTime LastMaintenanceRun { get; set; }
}