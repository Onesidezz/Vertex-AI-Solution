namespace DocumentProcessingAPI.Core.Interfaces;

/// <summary>
/// Service for reranking search results using a cross-encoder model.
/// Cross-encoders jointly encode the query and each document, producing
/// a relevance score more accurate than bi-encoder cosine similarity.
/// </summary>
public interface IRerankerService
{
    /// <summary>
    /// Rerank a list of search candidates by relevance to the query.
    /// Uses the same tuple type as RecordSearchService to avoid any mapping layer.
    /// The similarity field in each returned tuple is replaced with the reranker score
    /// normalized to [0,1]. If reranking is skipped or fails, the original list is
    /// returned unchanged.
    /// </summary>
    /// <param name="query">The cleaned search query used as the cross-encoder input.</param>
    /// <param name="candidates">Deduplicated candidates from hybrid search, sorted by hybrid score.</param>
    /// <param name="topK">Final result count requested — used to skip reranking when candidates.Count &lt;= topK.</param>
    Task<List<(string id, float similarity, Dictionary<string, object> metadata)>> RerankAsync(
        string query,
        List<(string id, float similarity, Dictionary<string, object> metadata)> candidates,
        int topK);
}
