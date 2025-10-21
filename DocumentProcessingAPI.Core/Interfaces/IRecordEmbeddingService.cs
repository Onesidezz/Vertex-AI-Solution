namespace DocumentProcessingAPI.Core.Interfaces
{
    /// <summary>
    /// Service for processing Content Manager records and generating embeddings
    /// </summary>
    public interface IRecordEmbeddingService
    {
        /// <summary>
        /// Process all records from Content Manager based on search criteria
        /// </summary>
        Task<int> ProcessAllRecordsAsync(string searchString = "*");

        /// <summary>
        /// Delete all embeddings (chunks) for a specific record URI from Vector DB
        /// </summary>
        /// <param name="recordUri">The Content Manager record URI to delete</param>
        /// <returns>Number of chunks deleted</returns>
        Task<int> DeleteRecordEmbeddingsAsync(long recordUri);

        /// <summary>
        /// Delete all embeddings (chunks) for multiple record URIs from Vector DB (batch deletion)
        /// </summary>
        /// <param name="recordUris">List of Content Manager record URIs to delete</param>
        /// <returns>Dictionary mapping each URI to the number of chunks deleted</returns>
        Task<Dictionary<long, int>> DeleteMultipleRecordEmbeddingsAsync(List<long> recordUris);
    }
}
