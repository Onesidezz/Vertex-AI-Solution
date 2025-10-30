using DocumentProcessingAPI.Core.Entities;
using DocumentProcessingAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace DocumentProcessingAPI.Infrastructure.Services;

/// <summary>
/// PostgreSQL + pgvector service for storing and searching embeddings
/// Replaces QdrantVectorService with PostgreSQL-based vector storage
/// </summary>
public class PgVectorService
{
    private readonly DocumentProcessingDbContext _context;
    private readonly ILogger<PgVectorService> _logger;

    public PgVectorService(DocumentProcessingDbContext context, ILogger<PgVectorService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Initialize the pgvector extension (ensure it's installed)
    /// This method can be called at startup to verify the database is ready
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            _logger.LogInformation("Initializing pgvector extension...");

            // Check if pgvector extension is enabled using raw SQL
            var extensionExists = false;
            var connection = _context.Database.GetDbConnection();

            // Don't dispose connection - let EF Core manage it
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT EXISTS(SELECT 1 FROM pg_extension WHERE extname = 'vector')";
                var result = await command.ExecuteScalarAsync();
                extensionExists = result != null && (bool)result;
            }

            if (!extensionExists)
            {
                _logger.LogWarning("pgvector extension not found. Attempting to create...");
                await _context.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector");
                _logger.LogInformation("✅ pgvector extension created successfully");
            }
            else
            {
                _logger.LogInformation("✅ pgvector extension already exists");
            }

            // Get total embeddings count
            var count = await _context.Embeddings.CountAsync();
            _logger.LogInformation("📊 Current embeddings count: {Count}", count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to initialize pgvector extension");
            throw;
        }
    }

    /// <summary>
    /// Save a single embedding to PostgreSQL
    /// </summary>
    public async Task SaveEmbeddingAsync(string embeddingId, float[] embedding, Dictionary<string, object> metadata)
    {
        try
        {
            var entity = new Embedding
            {
                EmbeddingId = embeddingId,
                Vector = new Vector(embedding),
                RecordUri = GetMetadataValue<long>(metadata, "record_uri"),
                RecordTitle = GetMetadataValue<string>(metadata, "record_title", ""),
                DateCreated = ParseDateTimeUtc(GetMetadataValue<string>(metadata, "date_created", null)),
                RecordType = GetMetadataValue<string>(metadata, "record_type", ""),
                Container = GetMetadataValue<string>(metadata, "container", null),
                Assignee = GetMetadataValue<string>(metadata, "assignee", null),
                AllParts = GetMetadataValue<string>(metadata, "all_parts", null),
                ACL = GetMetadataValue<string>(metadata, "acl", null),
                ChunkIndex = GetMetadataValue<int>(metadata, "chunk_index", 0),
                ChunkSequence = GetMetadataValue<int>(metadata, "chunk_sequence", 0),
                TotalChunks = GetMetadataValue<int>(metadata, "total_chunks", 1),
                TokenCount = GetMetadataValue<int>(metadata, "token_count", 0),
                StartPosition = GetMetadataValue<int>(metadata, "start_position", 0),
                EndPosition = GetMetadataValue<int>(metadata, "end_position", 0),
                PageNumber = GetMetadataValue<int>(metadata, "page_number", 0),
                ChunkContent = GetMetadataValue<string>(metadata, "chunk_content", ""),
                ContentPreview = GetMetadataValue<string>(metadata, "content_preview", null),
                FileExtension = GetMetadataValue<string>(metadata, "file_extension", null),
                FileType = GetMetadataValue<string>(metadata, "file_type", null),
                DocumentCategory = GetMetadataValue<string>(metadata, "document_category", null),
                EntityType = GetMetadataValue<string>(metadata, "entity_type", "content_manager_record"),
                IndexedAt = DateTime.SpecifyKind(DateTime.Parse(GetMetadataValue<string>(metadata, "indexed_at", DateTime.UtcNow.ToString("o"))), DateTimeKind.Utc),
                CreatedAt = DateTime.UtcNow
            };

            // Check if embedding already exists (upsert behavior)
            var existing = await _context.Embeddings
                .FirstOrDefaultAsync(e => e.EmbeddingId == embeddingId);

            if (existing != null)
            {
                // Update existing
                existing.Vector = entity.Vector;
                existing.RecordTitle = entity.RecordTitle;
                existing.DateCreated = entity.DateCreated;
                existing.RecordType = entity.RecordType;
                existing.Container = entity.Container;
                existing.Assignee = entity.Assignee;
                existing.AllParts = entity.AllParts;
                existing.ACL = entity.ACL;
                existing.ChunkContent = entity.ChunkContent;
                existing.ContentPreview = entity.ContentPreview;
                existing.FileExtension = entity.FileExtension;
                existing.FileType = entity.FileType;
                existing.DocumentCategory = entity.DocumentCategory;
                existing.IndexedAt = entity.IndexedAt;

                _context.Embeddings.Update(existing);
            }
            else
            {
                // Insert new
                await _context.Embeddings.AddAsync(entity);
            }

            await _context.SaveChangesAsync();
            _logger.LogDebug("💾 Saved embedding {EmbeddingId} to PostgreSQL", embeddingId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to save embedding {EmbeddingId} to PostgreSQL", embeddingId);
            throw;
        }
    }

    /// <summary>
    /// Save batch of embeddings to PostgreSQL (optimized for performance)
    /// </summary>
    public async Task SaveEmbeddingsBatchAsync(List<VectorData> vectorData)
    {
        if (vectorData == null || !vectorData.Any())
        {
            _logger.LogWarning("⚠️ Empty vector data batch provided");
            return;
        }

        try
        {
            _logger.LogInformation("💾 Saving batch of {Count} embeddings to PostgreSQL", vectorData.Count);

            var entities = new List<Embedding>();

            foreach (var v in vectorData)
            {
                var entity = new Embedding
                {
                    EmbeddingId = v.Id,
                    Vector = new Vector(v.Vector),
                    RecordUri = GetMetadataValue<long>(v.Metadata, "record_uri"),
                    RecordTitle = GetMetadataValue<string>(v.Metadata, "record_title", ""),
                    DateCreated = ParseDateTimeUtc(GetMetadataValue<string>(v.Metadata, "date_created", null)),
                    RecordType = GetMetadataValue<string>(v.Metadata, "record_type", ""),
                    Container = GetMetadataValue<string>(v.Metadata, "container", null),
                    Assignee = GetMetadataValue<string>(v.Metadata, "assignee", null),
                    AllParts = GetMetadataValue<string>(v.Metadata, "all_parts", null),
                    ACL = GetMetadataValue<string>(v.Metadata, "acl", null),
                    ChunkIndex = GetMetadataValue<int>(v.Metadata, "chunk_index", 0),
                    ChunkSequence = GetMetadataValue<int>(v.Metadata, "chunk_sequence", 0),
                    TotalChunks = GetMetadataValue<int>(v.Metadata, "total_chunks", 1),
                    TokenCount = GetMetadataValue<int>(v.Metadata, "token_count", 0),
                    StartPosition = GetMetadataValue<int>(v.Metadata, "start_position", 0),
                    EndPosition = GetMetadataValue<int>(v.Metadata, "end_position", 0),
                    PageNumber = GetMetadataValue<int>(v.Metadata, "page_number", 0),
                    ChunkContent = GetMetadataValue<string>(v.Metadata, "chunk_content", ""),
                    ContentPreview = GetMetadataValue<string>(v.Metadata, "content_preview", null),
                    FileExtension = GetMetadataValue<string>(v.Metadata, "file_extension", null),
                    FileType = GetMetadataValue<string>(v.Metadata, "file_type", null),
                    DocumentCategory = GetMetadataValue<string>(v.Metadata, "document_category", null),
                    EntityType = GetMetadataValue<string>(v.Metadata, "entity_type", "content_manager_record"),
                    IndexedAt = DateTime.SpecifyKind(DateTime.Parse(GetMetadataValue<string>(v.Metadata, "indexed_at", DateTime.UtcNow.ToString("o"))), DateTimeKind.Utc),
                    CreatedAt = DateTime.UtcNow
                };

                entities.Add(entity);
            }

            // Get all existing embeddings in this batch to optimize lookups
            var embeddingIds = entities.Select(e => e.EmbeddingId).ToList();
            var existingEmbeddings = await _context.Embeddings
                .Where(e => embeddingIds.Contains(e.EmbeddingId))
                .ToDictionaryAsync(e => e.EmbeddingId);

            // Batch insert with upsert logic
            foreach (var entity in entities)
            {
                if (existingEmbeddings.TryGetValue(entity.EmbeddingId, out var existing))
                {
                    // Update existing - manually set properties to avoid modifying the Id key
                    existing.Vector = entity.Vector;
                    existing.RecordUri = entity.RecordUri;
                    existing.RecordTitle = entity.RecordTitle;
                    existing.DateCreated = entity.DateCreated;
                    existing.RecordType = entity.RecordType;
                    existing.Container = entity.Container;
                    existing.Assignee = entity.Assignee;
                    existing.AllParts = entity.AllParts;
                    existing.ACL = entity.ACL;
                    existing.ChunkIndex = entity.ChunkIndex;
                    existing.ChunkSequence = entity.ChunkSequence;
                    existing.TotalChunks = entity.TotalChunks;
                    existing.TokenCount = entity.TokenCount;
                    existing.StartPosition = entity.StartPosition;
                    existing.EndPosition = entity.EndPosition;
                    existing.PageNumber = entity.PageNumber;
                    existing.ChunkContent = entity.ChunkContent;
                    existing.ContentPreview = entity.ContentPreview;
                    existing.FileExtension = entity.FileExtension;
                    existing.FileType = entity.FileType;
                    existing.DocumentCategory = entity.DocumentCategory;
                    existing.EntityType = entity.EntityType;
                    existing.IndexedAt = entity.IndexedAt;
                    // Note: Id and CreatedAt are NOT updated (CreatedAt should remain original)
                }
                else
                {
                    // Insert new
                    await _context.Embeddings.AddAsync(entity);
                }
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation("✅ Successfully saved {Count} embeddings to PostgreSQL", vectorData.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to save batch embeddings to PostgreSQL");
            throw;
        }
    }

    /// <summary>
    /// Get a single embedding by ID
    /// </summary>
    public async Task<(float[] embedding, Dictionary<string, object> metadata)?> GetEmbeddingAsync(string embeddingId)
    {
        try
        {
            var entity = await _context.Embeddings
                .FirstOrDefaultAsync(e => e.EmbeddingId == embeddingId);

            if (entity == null)
            {
                _logger.LogWarning("⚠️ Embedding not found in PostgreSQL: {EmbeddingId}", embeddingId);
                return null;
            }

            var metadata = new Dictionary<string, object>
            {
                ["record_uri"] = entity.RecordUri,
                ["record_title"] = entity.RecordTitle,
                ["date_created"] = entity.DateCreated?.ToString("MM/dd/yyyy HH:mm:ss") ?? "",
                ["record_type"] = entity.RecordType,
                ["container"] = entity.Container ?? "",
                ["assignee"] = entity.Assignee ?? "",
                ["all_parts"] = entity.AllParts ?? "",
                ["acl"] = entity.ACL ?? "",
                ["chunk_index"] = entity.ChunkIndex,
                ["chunk_sequence"] = entity.ChunkSequence,
                ["total_chunks"] = entity.TotalChunks,
                ["token_count"] = entity.TokenCount,
                ["start_position"] = entity.StartPosition,
                ["end_position"] = entity.EndPosition,
                ["page_number"] = entity.PageNumber,
                ["chunk_content"] = entity.ChunkContent,
                ["content_preview"] = entity.ContentPreview ?? "",
                ["file_extension"] = entity.FileExtension ?? "",
                ["file_type"] = entity.FileType ?? "",
                ["document_category"] = entity.DocumentCategory ?? "",
                ["entity_type"] = entity.EntityType,
                ["indexed_at"] = entity.IndexedAt.ToString("o")
            };

            return (entity.Vector.ToArray(), metadata);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get embedding {EmbeddingId} from PostgreSQL", embeddingId);
            return null;
        }
    }

    /// <summary>
    /// Delete a single embedding by ID
    /// </summary>
    public async Task<bool> DeleteEmbeddingAsync(string embeddingId)
    {
        try
        {
            var entity = await _context.Embeddings
                .FirstOrDefaultAsync(e => e.EmbeddingId == embeddingId);

            if (entity == null)
            {
                _logger.LogWarning("⚠️ Embedding not found for deletion: {EmbeddingId}", embeddingId);
                return false;
            }

            _context.Embeddings.Remove(entity);
            await _context.SaveChangesAsync();

            _logger.LogInformation("🗑️ Deleted embedding {EmbeddingId} from PostgreSQL", embeddingId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to delete embedding {EmbeddingId} from PostgreSQL", embeddingId);
            return false;
        }
    }

    /// <summary>
    /// Delete all embeddings for a specific Content Manager record URI
    /// Uses efficient WHERE clause deletion
    /// </summary>
    public async Task<bool> DeleteEmbeddingsByRecordUriAsync(long recordUri)
    {
        try
        {
            _logger.LogInformation("🗑️ Deleting all embeddings for record URI {RecordUri}", recordUri);

            var deleted = await _context.Embeddings
                .Where(e => e.RecordUri == recordUri)
                .ExecuteDeleteAsync();

            _logger.LogInformation("✅ Deleted {Count} embeddings for record URI {RecordUri}", deleted, recordUri);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to delete embeddings for record URI {RecordUri}", recordUri);
            return false;
        }
    }

    /// <summary>
    /// Get all point IDs for a specific record URI
    /// Used for deletion tracking
    /// </summary>
    public async Task<List<string>> GetPointIdsByRecordUriAsync(long recordUri)
    {
        try
        {
            var embeddingIds = await _context.Embeddings
                .Where(e => e.RecordUri == recordUri)
                .Select(e => e.EmbeddingId)
                .ToListAsync();

            _logger.LogDebug("✅ Found {Count} embeddings for record URI {RecordUri}", embeddingIds.Count, recordUri);
            return embeddingIds;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get point IDs for record URI {RecordUri}", recordUri);
            return new List<string>();
        }
    }

    /// <summary>
    /// Search for similar embeddings using vector similarity (cosine distance)
    /// Supports metadata filtering for dates, file types, etc.
    /// </summary>
    public async Task<List<(string id, float similarity, Dictionary<string, object> metadata)>> SearchSimilarAsync(
        float[] queryEmbedding, int limit = 10, float threshold = 0.0f, HashSet<long>? recordUriFilter = null)
    {
        try
        {
            _logger.LogDebug("🔍 Searching PostgreSQL for top {Limit} similar vectors (threshold: {Threshold}, URI filter: {HasFilter})",
                limit, threshold, recordUriFilter != null);

            var queryVector = new Vector(queryEmbedding);

            // Build base query
            var baseQuery = _context.Embeddings.AsQueryable();

            // Apply RecordUri filter if provided (CRITICAL: Filter at SQL level, not after retrieval)
            if (recordUriFilter != null && recordUriFilter.Any())
            {
                baseQuery = baseQuery.Where(e => recordUriFilter.Contains(e.RecordUri));
                _logger.LogDebug("   ✅ Filtering by {Count} RecordURIs at PostgreSQL query level", recordUriFilter.Count);
            }

            // Build query with vector similarity and projection
            // Note: For hybrid search with keyword boosting, order by semantic similarity first
            // Keyword boosting happens in post-processing to avoid complex SQL
            var query = baseQuery
                .OrderBy(e => e.Vector.CosineDistance(queryVector))
                .Select(e => new
                {
                    e.EmbeddingId,
                    Distance = e.Vector.CosineDistance(queryVector),
                    e.RecordUri,
                    e.RecordTitle,
                    e.DateCreated,
                    e.RecordType,
                    e.Container,
                    e.Assignee,
                    e.AllParts,
                    e.ACL,
                    e.ChunkIndex,
                    e.ChunkSequence,
                    e.TotalChunks,
                    e.TokenCount,
                    e.StartPosition,
                    e.EndPosition,
                    e.PageNumber,
                    e.ChunkContent,
                    e.ContentPreview,
                    e.FileExtension,
                    e.FileType,
                    e.DocumentCategory,
                    e.EntityType,
                    e.IndexedAt
                });

            // Apply limit
            var results = await query.Take(limit).ToListAsync();

            // Convert cosine distance to similarity score (1 - distance)
            var searchResults = results.Select(r =>
            {
                var similarity = (float)(1.0 - r.Distance);

                var metadata = new Dictionary<string, object>
                {
                    ["record_uri"] = r.RecordUri,
                    ["record_title"] = r.RecordTitle,
                    ["date_created"] = r.DateCreated?.ToString("MM/dd/yyyy HH:mm:ss") ?? "",
                    ["record_type"] = r.RecordType,
                    ["container"] = r.Container ?? "",
                    ["assignee"] = r.Assignee ?? "",
                    ["all_parts"] = r.AllParts ?? "",
                    ["acl"] = r.ACL ?? "",
                    ["chunk_index"] = r.ChunkIndex,
                    ["chunk_sequence"] = r.ChunkSequence,
                    ["total_chunks"] = r.TotalChunks,
                    ["token_count"] = r.TokenCount,
                    ["start_position"] = r.StartPosition,
                    ["end_position"] = r.EndPosition,
                    ["page_number"] = r.PageNumber,
                    ["chunk_content"] = r.ChunkContent,
                    ["content_preview"] = r.ContentPreview ?? "",
                    ["file_extension"] = r.FileExtension ?? "",
                    ["file_type"] = r.FileType ?? "",
                    ["document_category"] = r.DocumentCategory ?? "",
                    ["entity_type"] = r.EntityType,
                    ["indexed_at"] = r.IndexedAt.ToString("o"),
                    ["string_id"] = r.EmbeddingId
                };

                return (id: r.EmbeddingId, similarity, metadata);
            })
            .Where(r => r.similarity >= threshold)
            .ToList();

            _logger.LogInformation("✅ Found {Count} similar vectors in PostgreSQL", searchResults.Count);
            return searchResults;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to search similar embeddings in PostgreSQL");
            return new List<(string, float, Dictionary<string, object>)>();
        }
    }

    /// <summary>
    /// Search with hybrid scoring: combines vector similarity + keyword/exact match boosting
    /// Uses Gemini-extracted keywords for intelligent boosting
    /// </summary>
    public async Task<List<(string id, float similarity, Dictionary<string, object> metadata)>> SearchSimilarWithKeywordBoostAsync(
        float[] queryEmbedding,
        List<string> keywords,
        int limit = 10,
        float threshold = 0.0f,
        HashSet<long>? recordUriFilter = null,
        float keywordBoostWeight = 0.3f)
    {
        try
        {
            _logger.LogDebug("🔍 Hybrid search with keyword boosting using {Count} Gemini keywords", keywords?.Count ?? 0);

            // Get more results than needed for keyword boosting reranking
            var expandedLimit = Math.Min(limit * 5, recordUriFilter?.Count ?? 1000);

            // Get initial semantic search results
            var semanticResults = await SearchSimilarAsync(queryEmbedding, expandedLimit, 0, recordUriFilter);

            if (!semanticResults.Any())
            {
                return new List<(string, float, Dictionary<string, object>)>();
            }

            // Use provided Gemini keywords (already extracted)
            if (keywords == null || !keywords.Any())
            {
                _logger.LogWarning("   ⚠️ No keywords provided for boosting, using semantic-only results");
                return semanticResults.Take(limit).ToList();
            }

            _logger.LogDebug("   📋 Boosting with keywords: {Keywords}", string.Join(", ", keywords));

            // Apply hybrid scoring: semantic similarity + keyword match boost
            var hybridResults = semanticResults.Select(r =>
            {
                var semanticScore = r.similarity;
                var keywordScore = CalculateKeywordMatchScore(
                    r.metadata["chunk_content"].ToString(),
                    keywords);

                // Hybrid score: weighted combination
                var hybridScore = (semanticScore * (1 - keywordBoostWeight)) + (keywordScore * keywordBoostWeight);

                return (r.id, score: hybridScore, r.metadata, originalSemanticScore: semanticScore, keywordScore);
            })
            .OrderByDescending(r => r.score)
            .Take(limit)
            .Select(r =>
            {
                // Log significant keyword boosts
                if (r.keywordScore > 0.5f)
                {
                    var uri = r.metadata.ContainsKey("record_uri") ? r.metadata["record_uri"] : "?";
                    _logger.LogDebug("   ⬆️ Keyword boost for URI {Uri}: Semantic={Semantic:F3} → Hybrid={Hybrid:F3}",
                        uri, r.originalSemanticScore, r.score);
                }
                return (r.id, r.score, r.metadata);
            })
            .Where(r => r.score >= threshold)
            .ToList();

            _logger.LogInformation("✅ Hybrid search returned {Count} results (from {Total} semantic results)",
                hybridResults.Count, semanticResults.Count);

            return hybridResults;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed hybrid search with keyword boosting");
            // Fallback to regular semantic search
            return await SearchSimilarAsync(queryEmbedding, limit, threshold, recordUriFilter);
        }
    }

    /// <summary>
    /// Calculate keyword match score for a chunk based on exact/fuzzy keyword matching
    /// </summary>
    private float CalculateKeywordMatchScore(string chunkContent, List<string> keywords)
    {
        if (!keywords.Any() || string.IsNullOrWhiteSpace(chunkContent))
            return 0f;

        var lowerContent = chunkContent.ToLowerInvariant();
        var matchedKeywords = 0f;

        foreach (var keyword in keywords)
        {
            var lowerKeyword = keyword.ToLowerInvariant();

            // Exact match gets full score
            if (lowerContent.Contains(lowerKeyword))
            {
                matchedKeywords += 1f;
            }
            // Fuzzy match (contains most characters) gets partial score
            else if (lowerKeyword.Length >= 5)
            {
                var partialMatch = lowerKeyword.Substring(0, Math.Min(5, lowerKeyword.Length));
                if (lowerContent.Contains(partialMatch))
                {
                    matchedKeywords += 0.3f;
                }
            }
        }

        // Normalize by number of keywords
        return Math.Min(1.0f, matchedKeywords / keywords.Count);
    }

    /// <summary>
    /// Get collection statistics
    /// </summary>
    public async Task<(long count, DateTime? lastIndexed)> GetCollectionStatsAsync()
    {
        try
        {
            var count = await _context.Embeddings.CountAsync();
            var lastIndexed = await _context.Embeddings
                .OrderByDescending(e => e.IndexedAt)
                .Select(e => e.IndexedAt)
                .FirstOrDefaultAsync();

            return (count, lastIndexed == default ? null : lastIndexed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get collection stats");
            return (0, null);
        }
    }

    // Helper methods
    private T GetMetadataValue<T>(Dictionary<string, object> metadata, string key, T? defaultValue = default)
    {
        if (metadata.TryGetValue(key, out var value) && value != null)
        {
            try
            {
                if (typeof(T) == typeof(long) && value is int intValue)
                {
                    return (T)(object)(long)intValue;
                }
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return defaultValue ?? default!;
            }
        }
        return defaultValue ?? default!;
    }

    private DateTime? ParseDateTimeUtc(string? dateString)
    {
        if (string.IsNullOrEmpty(dateString))
            return null;

        if (DateTime.TryParse(dateString, out var result))
        {
            // Ensure DateTime is always UTC for PostgreSQL
            if (result.Kind == DateTimeKind.Unspecified)
                return DateTime.SpecifyKind(result, DateTimeKind.Utc);
            else if (result.Kind == DateTimeKind.Local)
                return result.ToUniversalTime();
            return result;
        }

        return null;
    }
}

/// <summary>
/// Vector data structure for batch operations
/// Matches the structure from QdrantVectorService
/// </summary>
public class VectorData
{
    public string Id { get; set; } = string.Empty;
    public float[] Vector { get; set; } = Array.Empty<float>();
    public Dictionary<string, object> Metadata { get; set; } = new();
}
