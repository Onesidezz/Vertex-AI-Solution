using DocumentProcessingAPI.Core.DTOs;

namespace DocumentProcessingAPI.Core.Interfaces
{
    /// <summary>
    /// Service for searching Content Manager records using embeddings
    /// </summary>
    public interface IRecordSearchService
    {
        /// <summary>
        /// Search records by natural language query with optional metadata filters
        /// Example: "get me records created on 22-10-2024"
        /// </summary>
        Task<RecordSearchResponseDto> SearchRecordsAsync(
            string query,
            Dictionary<string, object>? metadataFilters = null,
            int topK = 20,
            float minimumScore = 0.3f);
    }
}
