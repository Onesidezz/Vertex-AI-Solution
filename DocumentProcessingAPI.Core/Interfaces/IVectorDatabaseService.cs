using DocumentProcessingAPI.Core.DTOs;

namespace DocumentProcessingAPI.Core.Interfaces;

/// <summary>
/// Service interface for vector database operations
/// </summary>
public interface IVectorDatabaseService
{
    /// <summary>
    /// Initialize the vector database collection
    /// </summary>
    /// <returns>True if initialization successful</returns>
    Task<bool> InitializeCollectionAsync();

    /// <summary>
    /// Store a single vector with metadata
    /// </summary>
    /// <param name="id">Unique identifier for the vector</param>
    /// <param name="vector">Embedding vector</param>
    /// <param name="metadata">Associated metadata</param>
    /// <returns>True if stored successfully</returns>
    Task<bool> UpsertVectorAsync(string id, float[] vector, Dictionary<string, object> metadata);

    /// <summary>
    /// Store multiple vectors in batch
    /// </summary>
    /// <param name="vectors">List of vectors with IDs and metadata</param>
    /// <returns>Number of vectors successfully stored</returns>
    Task<int> UpsertVectorsBatchAsync(List<VectorData> vectors);

    /// <summary>
    /// Search for similar vectors
    /// </summary>
    /// <param name="queryVector">Query vector</param>
    /// <param name="topK">Number of results to return</param>
    /// <param name="filter">Optional metadata filters</param>
    /// <returns>Search results with scores</returns>
    Task<List<VectorSearchResult>> SearchVectorsAsync(float[] queryVector, int topK, Dictionary<string, object>? filter = null);

    /// <summary>
    /// Delete a vector by ID
    /// </summary>
    /// <param name="id">Vector ID to delete</param>
    /// <returns>True if deleted successfully</returns>
    Task<bool> DeleteVectorAsync(string id);

    /// <summary>
    /// Delete multiple vectors by document ID
    /// </summary>
    /// <param name="documentId">Document ID to filter vectors</param>
    /// <returns>Number of vectors deleted</returns>
    Task<int> DeleteVectorsByDocumentAsync(Guid documentId);

    /// <summary>
    /// Get vector by ID
    /// </summary>
    /// <param name="id">Vector ID</param>
    /// <returns>Vector data if found</returns>
    Task<VectorData?> GetVectorAsync(string id);

    /// <summary>
    /// Check if collection exists and is healthy
    /// </summary>
    /// <returns>True if collection is healthy</returns>
    Task<bool> IsCollectionHealthyAsync();

    /// <summary>
    /// Get collection statistics
    /// </summary>
    /// <returns>Collection statistics</returns>
    Task<CollectionStats> GetCollectionStatsAsync();
}

/// <summary>
/// Represents vector data with metadata
/// </summary>
public class VectorData
{
    public string Id { get; set; } = string.Empty;
    public float[] Vector { get; set; } = Array.Empty<float>();
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Represents a vector search result
/// </summary>
public class VectorSearchResult
{
    public string Id { get; set; } = string.Empty;
    public float Score { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Collection statistics
/// </summary>
public class CollectionStats
{
    public long VectorCount { get; set; }
    public int Dimension { get; set; }
    public DateTime LastUpdated { get; set; }
    public bool IsHealthy { get; set; }
}