using DocumentProcessingAPI.Core.DTOs;

namespace DocumentProcessingAPI.Core.Interfaces;

/// <summary>
/// Service interface for semantic search operations
/// </summary>
public interface ISearchService
{
    /// <summary>
    /// Perform semantic search across all documents
    /// </summary>
    /// <param name="request">Search request</param>
    /// <returns>Search results</returns>
    Task<SearchResponseDto> SearchAsync(SearchRequestDto request);

    /// <summary>
    /// Search within a specific document
    /// </summary>
    /// <param name="documentId">Document ID</param>
    /// <param name="request">Search request</param>
    /// <returns>Search results from the specific document</returns>
    Task<SearchResponseDto> SearchDocumentAsync(Guid documentId, SearchRequestDto request);

    /// <summary>
    /// Get similar chunks to a specific chunk
    /// </summary>
    /// <param name="chunkId">Reference chunk ID</param>
    /// <param name="topK">Number of similar chunks to return</param>
    /// <returns>Similar chunks</returns>
    Task<List<SearchResultDto>> GetSimilarChunksAsync(Guid chunkId, int topK = 5);

    /// <summary>
    /// Export search results to CSV
    /// </summary>
    /// <param name="searchRequest">Search request</param>
    /// <returns>CSV content as byte array</returns>
    Task<byte[]> ExportSearchResultsToCsvAsync(SearchRequestDto searchRequest);
}