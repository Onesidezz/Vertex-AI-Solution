using DocumentProcessingAPI.Core.Entities;
using DocumentProcessingAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using System.Text.RegularExpressions;

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
                    SourceDateModified = ParseDateTimeUtc(GetMetadataValue<string>(v.Metadata, "source_date_modified", null)),
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
    /// Delete all embeddings for multiple Content Manager record URIs (batch deletion)
    /// Used for efficient cleanup when reprocessing updated records
    /// </summary>
    public async Task<int> DeleteEmbeddingsByRecordUrisAsync(List<long> recordUris)
    {
        if (recordUris == null || !recordUris.Any())
        {
            _logger.LogWarning("⚠️ Empty record URI list provided for deletion");
            return 0;
        }

        try
        {
            _logger.LogInformation("🗑️ Deleting embeddings for {Count} record URIs (batch deletion)", recordUris.Count);

            var deleted = await _context.Embeddings
                .Where(e => recordUris.Contains(e.RecordUri))
                .ExecuteDeleteAsync();

            _logger.LogInformation("✅ Successfully deleted {Count} embeddings for {RecordCount} records", deleted, recordUris.Count);
            return deleted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to delete embeddings for {Count} record URIs", recordUris.Count);
            throw;
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
    /// Get all distinct RecordUri values that exist in the database
    /// Used to filter out already-processed records during batch operations
    /// </summary>
    public async Task<HashSet<long>> GetAllExistingRecordUrisAsync()
    {
        try
        {
            var existingUris = await _context.Embeddings
                .Select(e => e.RecordUri)
                .Distinct()
                .ToListAsync();

            _logger.LogInformation("📊 Found {Count} distinct RecordUri values in PostgreSQL", existingUris.Count);
            return new HashSet<long>(existingUris);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get existing RecordUri values from PostgreSQL");
            return new HashSet<long>();
        }
    }

    /// <summary>
    /// Get modification timestamps for existing records
    /// Returns Dictionary: RecordUri -> SourceDateModified
    /// Used for smart change detection - only reprocess if source was modified after last embedding
    /// </summary>
    public async Task<Dictionary<long, DateTime?>> GetRecordModificationTimestampsAsync()
    {
        try
        {
            var timestamps = await _context.Embeddings
                .GroupBy(e => e.RecordUri)
                .Select(g => new { RecordUri = g.Key, SourceDateModified = g.Max(e => e.SourceDateModified) })
                .ToDictionaryAsync(x => x.RecordUri, x => x.SourceDateModified);

            _logger.LogInformation("📊 Retrieved modification timestamps for {Count} records", timestamps.Count);
            return timestamps;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get record modification timestamps from PostgreSQL");
            return new Dictionary<long, DateTime?>();
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
    /// Search with hybrid scoring: combines vector similarity + PostgreSQL Full-Text Search boosting
    /// Uses websearch_to_tsquery() for intelligent query parsing (stemming, stop words, Boolean operators)
    /// </summary>
    public async Task<List<(string id, float similarity, Dictionary<string, object> metadata)>> SearchSimilarWithKeywordBoostAsync(
        float[] queryEmbedding,
        string queryText,
        int limit = 10,
        float threshold = 0.0f,
        HashSet<long>? recordUriFilter = null,
        float keywordBoostWeight = 0.3f)
    {
        try
        {
            _logger.LogDebug("🔍 Hybrid search with PostgreSQL FTS using query: {Query}", queryText);

            // Get more results than needed for keyword boosting reranking
            var expandedLimit = Math.Min(limit * 5, recordUriFilter?.Count ?? 1000);

            // Get initial semantic search results
            var semanticResults = await SearchSimilarAsync(queryEmbedding, expandedLimit, 0, recordUriFilter);

            // Use raw query text directly with PostgreSQL FTS
            if (string.IsNullOrWhiteSpace(queryText))
            {
                _logger.LogWarning("   ⚠️ No query text provided for FTS boosting, using semantic-only results");
                return semanticResults.Take(limit).Where(r => r.similarity >= threshold).ToList();
            }

            _logger.LogDebug("   📋 Boosting with PostgreSQL FTS query: {Query}", queryText);

            // Escape single quotes for SQL parameter (websearch_to_tsquery will handle the rest)
            var sanitizedQuery = queryText.Replace("'", "''");

            // FIXED: Query PostgreSQL FTS scores across ALL embeddings (not just semantic results)
            // This ensures keyword matches are found even if they're not semantically similar
            // plainto_tsquery() uses OR logic (finds ANY of the words):
            // - Automatic stop word removal
            // - Stemming (workflow → work, workflows)
            // - OR logic: matches if ANY query word is present
            // - More forgiving than websearch_to_tsquery (which uses AND logic)
            var ftsScoresSql = $@"
                SELECT
                    ""EmbeddingId"",
                    ""RecordUri"",
                    ""RecordTitle"",
                    ""DateCreated"",
                    ""RecordType"",
                    ""Container"",
                    ""Assignee"",
                    ""AllParts"",
                    ""ACL"",
                    ""ChunkIndex"",
                    ""ChunkSequence"",
                    ""TotalChunks"",
                    ""TokenCount"",
                    ""StartPosition"",
                    ""EndPosition"",
                    ""PageNumber"",
                    ""ChunkContent"",
                    ""ContentPreview"",
                    ""FileExtension"",
                    ""FileType"",
                    ""DocumentCategory"",
                    ""EntityType"",
                    ""IndexedAt"",
                    ts_rank(search_vector, plainto_tsquery('english', @p0)) as fts_score
                FROM ""Embeddings""
                WHERE search_vector @@ plainto_tsquery('english', @p0)
                ORDER BY fts_score DESC
                LIMIT @p1";

            var connection = _context.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            var ftsScores = new Dictionary<string, float>();
            var ftsResults = new List<(string id, float ftsScore, Dictionary<string, object> metadata)>();

            using (var command = connection.CreateCommand())
            {
                command.CommandText = ftsScoresSql;

                var p0 = command.CreateParameter();
                p0.ParameterName = "@p0";
                p0.Value = sanitizedQuery;
                command.Parameters.Add(p0);

                var p1 = command.CreateParameter();
                p1.ParameterName = "@p1";
                p1.Value = expandedLimit;
                command.Parameters.Add(p1);

                _logger.LogDebug("   🔍 FTS SQL Query: {Query}", ftsScoresSql);
                _logger.LogDebug("   🔍 FTS Parameters: @p0='{QueryText}', @p1={Limit}", sanitizedQuery, expandedLimit);

                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var embeddingId = reader.GetString(0);
                        var ftsScore = reader.GetFloat(23);

                        ftsScores[embeddingId] = ftsScore;

                        // Build metadata for FTS results
                        var metadata = new Dictionary<string, object>
                        {
                            ["record_uri"] = reader.GetInt64(1),
                            ["record_title"] = reader.IsDBNull(2) ? "" : reader.GetString(2),
                            ["date_created"] = reader.IsDBNull(3) ? "" : reader.GetDateTime(3).ToString("MM/dd/yyyy HH:mm:ss"),
                            ["record_type"] = reader.IsDBNull(4) ? "" : reader.GetString(4),
                            ["container"] = reader.IsDBNull(5) ? "" : reader.GetString(5),
                            ["assignee"] = reader.IsDBNull(6) ? "" : reader.GetString(6),
                            ["all_parts"] = reader.IsDBNull(7) ? "" : reader.GetString(7),
                            ["acl"] = reader.IsDBNull(8) ? "" : reader.GetString(8),
                            ["chunk_index"] = reader.GetInt32(9),
                            ["chunk_sequence"] = reader.GetInt32(10),
                            ["total_chunks"] = reader.GetInt32(11),
                            ["token_count"] = reader.GetInt32(12),
                            ["start_position"] = reader.GetInt32(13),
                            ["end_position"] = reader.GetInt32(14),
                            ["page_number"] = reader.GetInt32(15),
                            ["chunk_content"] = reader.IsDBNull(16) ? "" : reader.GetString(16),
                            ["content_preview"] = reader.IsDBNull(17) ? "" : reader.GetString(17),
                            ["file_extension"] = reader.IsDBNull(18) ? "" : reader.GetString(18),
                            ["file_type"] = reader.IsDBNull(19) ? "" : reader.GetString(19),
                            ["document_category"] = reader.IsDBNull(20) ? "" : reader.GetString(20),
                            ["entity_type"] = reader.IsDBNull(21) ? "" : reader.GetString(21),
                            ["indexed_at"] = reader.GetDateTime(22).ToString("o"),
                            ["string_id"] = embeddingId
                        };

                        ftsResults.Add((embeddingId, ftsScore, metadata));
                    }
                }
            }

            await connection.CloseAsync();

            _logger.LogInformation("   📊 FTS found {FtsCount} keyword matches across all embeddings", ftsResults.Count);
            _logger.LogDebug("   📊 Semantic search found {SemanticCount} results", semanticResults.Count);

            // FALLBACK: If FTS found nothing, try LIKE search with individual words (OR logic)
            if (!ftsResults.Any() && queryText.Length > 5)
            {
                _logger.LogWarning("   ⚠️ FTS returned 0 results, attempting fallback LIKE search with OR logic");

                // Split query into words and filter out common stop words
                var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "which", "what", "where", "when", "who", "how", "is", "are", "was", "were",
                    "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for", "of",
                    "with", "by", "from", "called", "named", "titled", "book", "document", "file"
                };

                var words = queryText.Split(new[] { ' ', ',', '.', '?', '!' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(w => w.Length > 2 && !stopWords.Contains(w))
                    .Select(w => w.Replace("'", "''"))
                    .ToList();

                if (!words.Any())
                {
                    _logger.LogWarning("   ⚠️ No meaningful words found after filtering");
                }
                else
                {
                    _logger.LogInformation("   🔍 Searching for words: {Words}", string.Join(", ", words));

                    // Build WHERE clause with OR conditions for each word
                    var whereConditions = string.Join(" OR ", words.Select((_, i) => $@"""ChunkContent"" ILIKE @w{i}"));

                    var likeSearchSql = $@"
                        SELECT
                            ""EmbeddingId"",
                            ""RecordUri"",
                            ""RecordTitle"",
                            ""DateCreated"",
                            ""RecordType"",
                            ""Container"",
                            ""Assignee"",
                            ""AllParts"",
                            ""ACL"",
                            ""ChunkIndex"",
                            ""ChunkSequence"",
                            ""TotalChunks"",
                            ""TokenCount"",
                            ""StartPosition"",
                            ""EndPosition"",
                            ""PageNumber"",
                            ""ChunkContent"",
                            ""ContentPreview"",
                            ""FileExtension"",
                            ""FileType"",
                            ""DocumentCategory"",
                            ""EntityType"",
                            ""IndexedAt""
                        FROM ""Embeddings""
                        WHERE {whereConditions}
                        LIMIT @p1";

                    if (connection.State != System.Data.ConnectionState.Open)
                    {
                        await connection.OpenAsync();
                    }

                    using (var likeCommand = connection.CreateCommand())
                    {
                        likeCommand.CommandText = likeSearchSql;

                        // Add parameter for each word
                        for (int i = 0; i < words.Count; i++)
                        {
                            var param = likeCommand.CreateParameter();
                            param.ParameterName = $"@w{i}";
                            param.Value = $"%{words[i]}%";
                            likeCommand.Parameters.Add(param);
                        }

                        var lp1 = likeCommand.CreateParameter();
                        lp1.ParameterName = "@p1";
                        lp1.Value = Math.Min(expandedLimit, 100);
                        likeCommand.Parameters.Add(lp1);

                        using (var likeReader = await likeCommand.ExecuteReaderAsync())
                    {
                        while (await likeReader.ReadAsync())
                        {
                            var embeddingId = likeReader.GetString(0);
                            // Assign a fixed score for LIKE matches (0.8 to prioritize them)
                            var likeScore = 0.8f;

                            ftsScores[embeddingId] = likeScore;

                            var metadata = new Dictionary<string, object>
                            {
                                ["record_uri"] = likeReader.GetInt64(1),
                                ["record_title"] = likeReader.IsDBNull(2) ? "" : likeReader.GetString(2),
                                ["date_created"] = likeReader.IsDBNull(3) ? "" : likeReader.GetDateTime(3).ToString("MM/dd/yyyy HH:mm:ss"),
                                ["record_type"] = likeReader.IsDBNull(4) ? "" : likeReader.GetString(4),
                                ["container"] = likeReader.IsDBNull(5) ? "" : likeReader.GetString(5),
                                ["assignee"] = likeReader.IsDBNull(6) ? "" : likeReader.GetString(6),
                                ["all_parts"] = likeReader.IsDBNull(7) ? "" : likeReader.GetString(7),
                                ["acl"] = likeReader.IsDBNull(8) ? "" : likeReader.GetString(8),
                                ["chunk_index"] = likeReader.GetInt32(9),
                                ["chunk_sequence"] = likeReader.GetInt32(10),
                                ["total_chunks"] = likeReader.GetInt32(11),
                                ["token_count"] = likeReader.GetInt32(12),
                                ["start_position"] = likeReader.GetInt32(13),
                                ["end_position"] = likeReader.GetInt32(14),
                                ["page_number"] = likeReader.GetInt32(15),
                                ["chunk_content"] = likeReader.IsDBNull(16) ? "" : likeReader.GetString(16),
                                ["content_preview"] = likeReader.IsDBNull(17) ? "" : likeReader.GetString(17),
                                ["file_extension"] = likeReader.IsDBNull(18) ? "" : likeReader.GetString(18),
                                ["file_type"] = likeReader.IsDBNull(19) ? "" : likeReader.GetString(19),
                                ["document_category"] = likeReader.IsDBNull(20) ? "" : likeReader.GetString(20),
                                ["entity_type"] = likeReader.IsDBNull(21) ? "" : likeReader.GetString(21),
                                ["indexed_at"] = likeReader.GetDateTime(22).ToString("o"),
                                ["string_id"] = embeddingId
                            };

                            ftsResults.Add((embeddingId, likeScore, metadata));
                        }
                    }
                    }

                    await connection.CloseAsync();

                    if (ftsResults.Any())
                    {
                        _logger.LogInformation("   ✅ LIKE search found {Count} matches", ftsResults.Count);
                    }
                    else
                    {
                        _logger.LogWarning("   ⚠️ LIKE search also found 0 matches");
                    }
                }
            }

            // Merge semantic and FTS results (union by embedding ID)
            var allResultsById = new Dictionary<string, (float semanticScore, float ftsScore, Dictionary<string, object> metadata)>();

            // Add semantic results
            foreach (var r in semanticResults)
            {
                allResultsById[r.id] = (r.similarity, 0f, r.metadata);
            }

            // Add/merge FTS results
            foreach (var r in ftsResults)
            {
                if (allResultsById.ContainsKey(r.id))
                {
                    // Already in semantic results - add FTS score
                    var existing = allResultsById[r.id];
                    allResultsById[r.id] = (existing.semanticScore, r.ftsScore, existing.metadata);
                }
                else
                {
                    // Only in FTS results - add with 0 semantic score
                    allResultsById[r.id] = (0f, r.ftsScore, r.metadata);
                }
            }

            _logger.LogInformation("   📊 Merged results: {Total} unique embeddings ({Semantic} semantic, {Fts} FTS, {Both} both)",
                allResultsById.Count, semanticResults.Count, ftsResults.Count,
                semanticResults.Count + ftsResults.Count - allResultsById.Count);

            // Normalize FTS scores to 0-1 range
            var maxFtsScore = ftsScores.Values.Any() ? ftsScores.Values.Max() : 1.0f;

            // If LIKE fallback was used, prioritize keyword matches much more heavily
            var hasLikeFallbackResults = ftsScores.Values.Any(s => s == 0.8f); // LIKE matches have score 0.8
            var effectiveKeywordWeight = hasLikeFallbackResults ? 0.9f : keywordBoostWeight; // 90% for LIKE, 30% for FTS

            if (hasLikeFallbackResults)
            {
                _logger.LogInformation("   🎯 LIKE fallback active - using {Weight}% keyword weight to prioritize exact matches",
                    (int)(effectiveKeywordWeight * 100));
            }

            // Apply hybrid scoring to merged results
            var hybridResults = allResultsById.Select(kvp =>
            {
                var (semanticScore, ftsScore, metadata) = kvp.Value;

                // Normalize FTS score
                var normalizedFtsScore = maxFtsScore > 0 ? ftsScore / maxFtsScore : 0f;

                // Hybrid score: weighted combination
                // When LIKE fallback is active, keyword matches get 90% weight (vs 30% for normal FTS)
                var hybridScore = (semanticScore * (1 - effectiveKeywordWeight)) + (normalizedFtsScore * effectiveKeywordWeight);

                return (id: kvp.Key, score: hybridScore, metadata, semanticScore, normalizedFtsScore);
            })
            .OrderByDescending(r => r.score)
            .Take(limit)
            .Select(r =>
            {
                // Log significant FTS boosts
                if (r.normalizedFtsScore > 0.5f)
                {
                    var uri = r.metadata.ContainsKey("record_uri") ? r.metadata["record_uri"] : "?";
                    _logger.LogDebug("   ⬆️ FTS boost for URI {Uri}: Semantic={Semantic:F3}, FTS={Fts:F3} → Hybrid={Hybrid:F3}",
                        uri, r.semanticScore, r.normalizedFtsScore, r.score);
                }
                return (r.id, r.score, r.metadata);
            })
            .Where(r => r.score >= threshold)
            .ToList();

            _logger.LogInformation("✅ Hybrid search returned {Count} results (semantic: {SemanticCount}, FTS: {FtsCount})",
                hybridResults.Count, semanticResults.Count, ftsResults.Count);

            return hybridResults;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed hybrid search with PostgreSQL FTS");
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
    /// Calculate keyword match score across multiple metadata fields with weighted importance
    /// </summary>
    private float CalculateKeywordMatchScoreMultiField(Dictionary<string, object> metadata, List<string> keywords)
    {
        if (!keywords.Any() || metadata == null)
            return 0f;

        // Define field weights (total should be 1.0)
        // NOTE: date_created is included for year/date matching (e.g., "2024", "2025")
        // Primary date filtering still happens via ExtractDateRangeFromQuery + ApplyDateRangeFilter
        var fieldWeights = new Dictionary<string, float>
        {
            ["chunk_content"] = 0.35f,      // Main content - highest weight
            ["record_title"] = 0.20f,       // Record title - very important
            ["file_name"] = 0.15f,          // File name - very important
            ["document_category"] = 0.11f,  // Category - important for classification
            ["record_type"] = 0.08f,        // Record type - contextual
            ["container"] = 0.05f,          // Container name - useful context
            ["assignee"] = 0.04f,           // Assignee - useful for person searches
            ["date_created"] = 0.02f,       // Date - useful for year/date matching
            ["file_type"] = 0.01f           // File type - least important
        };

        float totalScore = 0f;
        float totalWeight = 0f;

        foreach (var fieldConfig in fieldWeights)
        {
            var fieldName = fieldConfig.Key;
            var weight = fieldConfig.Value;

            // Skip if field doesn't exist or is null
            if (!metadata.ContainsKey(fieldName) || metadata[fieldName] == null)
                continue;

            var fieldContent = metadata[fieldName].ToString();
            if (string.IsNullOrWhiteSpace(fieldContent))
                continue;

            // Calculate keyword match for this field
            var fieldScore = CalculateKeywordMatchScore(fieldContent, keywords);

            // Add weighted score
            totalScore += fieldScore * weight;
            totalWeight += weight;
        }

        // Normalize by actual weights used (in case some fields were missing)
        return totalWeight > 0 ? totalScore / totalWeight : 0f;
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

    // ============================================================
    // CHECKPOINT MANAGEMENT METHODS
    // ============================================================

    /// <summary>
    /// Get or create checkpoint for a specific job
    /// If checkpoint doesn't exist, creates a new one with default values
    /// </summary>
    public async Task<SyncCheckpoint> GetOrCreateCheckpointAsync(string jobName)
    {
        try
        {
            var checkpoint = await _context.SyncCheckpoints
                .FirstOrDefaultAsync(c => c.JobName == jobName);

            if (checkpoint == null)
            {
                _logger.LogInformation("📌 Creating new checkpoint for job: {JobName}", jobName);
                checkpoint = new SyncCheckpoint
                {
                    JobName = jobName,
                    LastSyncDate = null,
                    LastProcessedPage = 0,
                    Status = "Completed",
                    TotalRecordsProcessed = 0,
                    SuccessCount = 0,
                    FailureCount = 0,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                await _context.SyncCheckpoints.AddAsync(checkpoint);
                await _context.SaveChangesAsync();
            }

            return checkpoint;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get or create checkpoint for job: {JobName}", jobName);
            throw;
        }
    }

    /// <summary>
    /// Update checkpoint with current progress
    /// Used to persist progress during job execution
    /// </summary>
    public async Task UpdateCheckpointAsync(
        string jobName,
        int lastProcessedPage,
        string status,
        long totalRecordsProcessed = 0,
        long successCount = 0,
        long failureCount = 0,
        DateTime? lastSyncDate = null,
        string? errorMessage = null)
    {
        try
        {
            var checkpoint = await _context.SyncCheckpoints
                .FirstOrDefaultAsync(c => c.JobName == jobName);

            if (checkpoint == null)
            {
                _logger.LogWarning("⚠️ Checkpoint not found for job {JobName}, creating new one", jobName);
                checkpoint = new SyncCheckpoint
                {
                    JobName = jobName,
                    CreatedAt = DateTime.UtcNow
                };
                await _context.SyncCheckpoints.AddAsync(checkpoint);
            }

            checkpoint.LastProcessedPage = lastProcessedPage;
            checkpoint.Status = status;
            checkpoint.TotalRecordsProcessed = totalRecordsProcessed;
            checkpoint.SuccessCount = successCount;
            checkpoint.FailureCount = failureCount;
            checkpoint.ErrorMessage = errorMessage;
            checkpoint.UpdatedAt = DateTime.UtcNow;

            if (lastSyncDate.HasValue)
            {
                checkpoint.LastSyncDate = lastSyncDate.Value;
            }

            await _context.SaveChangesAsync();

            _logger.LogDebug("💾 Updated checkpoint for job {JobName}: Page {Page}, Status {Status}, Success {Success}, Failure {Failure}",
                jobName, lastProcessedPage, status, successCount, failureCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to update checkpoint for job: {JobName}", jobName);
            throw;
        }
    }

    /// <summary>
    /// Get checkpoint by job name
    /// Returns null if checkpoint doesn't exist
    /// </summary>
    public async Task<SyncCheckpoint?> GetCheckpointAsync(string jobName)
    {
        try
        {
            return await _context.SyncCheckpoints
                .FirstOrDefaultAsync(c => c.JobName == jobName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get checkpoint for job: {JobName}", jobName);
            return null;
        }
    }

    /// <summary>
    /// Find the single most relevant chunk for a query using semantic search
    /// Used for context window approach in Q&A functionality
    /// </summary>
    /// <param name="queryEmbedding">The query embedding vector</param>
    /// <param name="recordUri">Filter to specific record URI</param>
    /// <param name="threshold">Minimum similarity threshold (default 0.2)</param>
    /// <returns>The most relevant Embedding entity, or null if none found</returns>
    public async Task<Embedding?> FindMostRelevantChunkAsync(
        float[] queryEmbedding,
        long recordUri,
        float threshold = 0.2f)
    {
        try
        {
            _logger.LogDebug("🔍 Finding most relevant chunk for record URI: {RecordUri}", recordUri);

            var queryVector = new Vector(queryEmbedding);

            // Find the single most relevant chunk with distance calculation in SQL query
            var result = await _context.Embeddings
                .Where(e => e.RecordUri == recordUri)
                .OrderBy(e => e.Vector.CosineDistance(queryVector))
                .Select(e => new
                {
                    Embedding = e,
                    Distance = e.Vector.CosineDistance(queryVector) // Calculate in SQL
                })
                .FirstOrDefaultAsync();

            if (result == null)
            {
                _logger.LogWarning("⚠️ No chunks found for record URI: {RecordUri}", recordUri);
                return null;
            }

            // Calculate similarity (1 - distance) in C# after query execution
            var similarity = (float)(1.0 - result.Distance);

            if (similarity < threshold)
            {
                _logger.LogWarning("⚠️ Best chunk has similarity {Similarity:F3} which is below threshold {Threshold}",
                    similarity, threshold);
                return null;
            }

            _logger.LogInformation("✅ Found most relevant chunk: Sequence={ChunkSequence}, Page={PageNumber}, Index={ChunkIndex}, Similarity={Similarity:F3}",
                result.Embedding.ChunkSequence, result.Embedding.PageNumber, result.Embedding.ChunkIndex, similarity);

            return result.Embedding;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to find most relevant chunk for record URI: {RecordUri}", recordUri);
            return null;
        }
    }

    /// <summary>
    /// Find the top N most relevant chunks for a query using semantic search
    /// Used to cover topics spread across multiple sections of the document
    /// </summary>
    /// <param name="queryEmbedding">The query embedding vector</param>
    /// <param name="recordUri">Filter to specific record URI</param>
    /// <param name="topN">Number of top chunks to return (default 5)</param>
    /// <param name="threshold">Minimum similarity threshold (default 0.2)</param>
    /// <returns>List of top relevant Embedding entities ordered by similarity</returns>
    public async Task<List<Embedding>> FindTopRelevantChunksAsync(
        float[] queryEmbedding,
        long recordUri,
        int topN = 5,
        float threshold = 0.2f)
    {
        try
        {
            _logger.LogDebug("🔍 Finding top {TopN} relevant chunks for record URI: {RecordUri}", topN, recordUri);

            var queryVector = new Vector(queryEmbedding);

            var results = await _context.Embeddings
                .Where(e => e.RecordUri == recordUri)
                .OrderBy(e => e.Vector.CosineDistance(queryVector))
                .Select(e => new
                {
                    Embedding = e,
                    Distance = e.Vector.CosineDistance(queryVector)
                })
                .Take(topN)
                .ToListAsync();

            var filtered = results
                .Where(r => (float)(1.0 - r.Distance) >= threshold)
                .Select(r =>
                {
                    var similarity = (float)(1.0 - r.Distance);
                    _logger.LogDebug("   Chunk Seq={Seq}, Page={Page}, Similarity={Sim:F3}",
                        r.Embedding.ChunkSequence, r.Embedding.PageNumber, similarity);
                    return r.Embedding;
                })
                .ToList();

            _logger.LogInformation("✅ Found {Count}/{TopN} relevant chunks above threshold {Threshold}",
                filtered.Count, topN, threshold);

            return filtered;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to find top relevant chunks for record URI: {RecordUri}", recordUri);
            return new List<Embedding>();
        }
    }

    /// <summary>
    /// Find top N chunks by keyword/literal search — handles numbers, dollar amounts, proper nouns
    /// that semantic embeddings often miss. Used as the keyword leg of hybrid search.
    /// Strategy: (1) PostgreSQL full-text search, (2) ILIKE fallback for tokens FTS misses (e.g. "$20,000")
    /// </summary>
    public async Task<List<Embedding>> FindTopChunksByKeywordAsync(
        string searchText,
        long recordUri,
        int topN = 5)
    {
        try
        {
            _logger.LogDebug("🔑 Keyword searching top {TopN} chunks for record URI: {RecordUri}", topN, recordUri);

            var cleaned = Regex.Replace(searchText, @"[?!""']", "").Trim();
            if (string.IsNullOrWhiteSpace(cleaned))
                return new List<Embedding>();

            // --- Pass 1: PostgreSQL full-text search (good for normal words) ---
            var ftResults = await _context.Embeddings
                .Where(e => e.RecordUri == recordUri)
                .Where(e => EF.Functions.ToTsVector("english", e.ChunkContent)
                    .Matches(EF.Functions.WebSearchToTsQuery("english", cleaned)))
                .OrderByDescending(e => EF.Functions.ToTsVector("english", e.ChunkContent)
                    .Rank(EF.Functions.WebSearchToTsQuery("english", cleaned)))
                .Take(topN)
                .ToListAsync();

            if (ftResults.Any())
            {
                _logger.LogInformation("✅ Keyword (FTS) found {Count} chunks", ftResults.Count);
                return ftResults;
            }

            // --- Pass 2: ILIKE fallback — extract specific tokens FTS misses (numbers, $amounts) ---
            // e.g. "$20,000" → ["20,000", "20000"], "discussion" → ["discussion"]
            var tokens = ExtractSearchTokens(searchText);
            _logger.LogDebug("   ILIKE fallback tokens: [{Tokens}]", string.Join(", ", tokens));

            var ilikeResults = new List<Embedding>();
            foreach (var token in tokens.Take(4))
            {
                var pattern = $"%{token}%";
                var hits = await _context.Embeddings
                    .Where(e => e.RecordUri == recordUri)
                    .Where(e => EF.Functions.ILike(e.ChunkContent, pattern))
                    .Take(topN)
                    .ToListAsync();
                ilikeResults.AddRange(hits);
                if (ilikeResults.Count >= topN) break;
            }

            var distinct = ilikeResults.DistinctBy(e => e.Id).Take(topN).ToList();
            _logger.LogInformation("✅ Keyword (ILIKE) found {Count} chunks", distinct.Count);
            return distinct;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Keyword search failed for record URI: {RecordUri}", recordUri);
            return new List<Embedding>();
        }
    }

    /// <summary>
    /// Extracts meaningful tokens from a query for ILIKE search.
    /// Prioritises dollar amounts and numbers, then significant words.
    /// e.g. "what is the discussion about $20,000" → ["20,000", "20000", "discussion"]
    /// </summary>
    private static List<string> ExtractSearchTokens(string query)
    {
        var tokens = new List<string>();

        // Dollar amounts: $20,000 or $20000 → add both "20,000" and "20000"
        foreach (Match m in Regex.Matches(query, @"\$[\d,]+"))
        {
            var raw = m.Value.TrimStart('$');          // "20,000"
            tokens.Add(raw);
            tokens.Add(raw.Replace(",", ""));          // "20000"
        }

        // Bare numbers/amounts not preceded by $ (e.g. "20,000")
        foreach (Match m in Regex.Matches(query, @"\b\d[\d,]*\b"))
        {
            var raw = m.Value;
            if (!tokens.Contains(raw)) tokens.Add(raw);
        }

        // Significant words (length > 3, not common stop words)
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "what", "when", "where", "which", "that", "this", "with", "from",
              "about", "have", "does", "were", "been", "they", "their", "there" };
        foreach (var word in Regex.Split(query, @"\W+"))
        {
            if (word.Length > 3 && !stopWords.Contains(word) && !Regex.IsMatch(word, @"^\d+$"))
                tokens.Add(word);
        }

        return tokens.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Find the most relevant chunk using PostgreSQL Full-Text Search (no embeddings required)
    /// Cost-effective alternative to semantic search - uses built-in PostgreSQL text search
    /// </summary>
    /// <param name="searchText">The search query text</param>
    /// <param name="recordUri">Filter to specific record URI</param>
    /// <returns>The most relevant Embedding entity, or null if none found</returns>
    public async Task<Embedding?> FindMostRelevantChunkByTextSearchAsync(
        string searchText,
        long recordUri)
    {
        try
        {
            _logger.LogDebug("🔍 Finding most relevant chunk using full-text search for record URI: {RecordUri}", recordUri);
            _logger.LogDebug("   Search text: {SearchText}", searchText);

            // Clean search text - remove special characters and prepare for text search
            var cleanedSearch = searchText
                .Replace("'", "")
                .Replace("\"", "")
                .Replace("?", "")
                .Replace("!", "")
                .Trim();

            if (string.IsNullOrWhiteSpace(cleanedSearch))
            {
                _logger.LogWarning("⚠️ Search text is empty after cleaning");
                return null;
            }

            // Use PostgreSQL full-text search with ts_rank for relevance scoring
            // to_tsvector converts the chunk content to text search vector
            // to_tsquery converts the search text to a query
            // ts_rank scores the relevance
            var result = await _context.Embeddings
                .Where(e => e.RecordUri == recordUri)
                .Where(e => EF.Functions.ToTsVector("english", e.ChunkContent)
                    .Matches(EF.Functions.WebSearchToTsQuery("english", cleanedSearch)))
                .OrderByDescending(e => EF.Functions.ToTsVector("english", e.ChunkContent)
                    .Rank(EF.Functions.WebSearchToTsQuery("english", cleanedSearch)))
                .FirstOrDefaultAsync();

            if (result == null)
            {
                _logger.LogWarning("⚠️ No matching chunk found using full-text search for record URI: {RecordUri}", recordUri);
                return null;
            }

            _logger.LogInformation("✅ Found matching chunk using full-text search: Sequence={ChunkSequence}, Page={PageNumber}, Index={ChunkIndex}",
                result.ChunkSequence, result.PageNumber, result.ChunkIndex);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to find chunk using full-text search for record URI: {RecordUri}", recordUri);
            return null;
        }
    }

    /// <summary>
    /// Get chunks within a ChunkSequence range for a specific record
    /// Used for context window approach (e.g., get 3 chunks before and after)
    /// ChunkSequence is more accurate than PageNumber for maintaining document order
    /// </summary>
    /// <param name="recordUri">Filter to specific record URI</param>
    /// <param name="minSequence">Minimum chunk sequence number (inclusive)</param>
    /// <param name="maxSequence">Maximum chunk sequence number (inclusive)</param>
    /// <returns>List of embeddings ordered by ChunkSequence</returns>
    public async Task<List<Embedding>> GetChunksBySequenceRangeAsync(
        long recordUri,
        int minSequence,
        int maxSequence)
    {
        try
        {
            _logger.LogDebug("📄 Getting chunks for record {RecordUri}, sequences {MinSeq}-{MaxSeq}",
                recordUri, minSequence, maxSequence);

            var chunks = await _context.Embeddings
                .Where(e => e.RecordUri == recordUri &&
                           e.ChunkSequence >= minSequence &&
                           e.ChunkSequence <= maxSequence)
                .OrderBy(e => e.ChunkSequence)
                .ToListAsync();

            _logger.LogInformation("✅ Retrieved {Count} chunks from sequences {MinSeq}-{MaxSeq}",
                chunks.Count, minSequence, maxSequence);

            return chunks;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get chunks for record {RecordUri}, sequences {MinSeq}-{MaxSeq}",
                recordUri, minSequence, maxSequence);
            return new List<Embedding>();
        }
    }

    /// <summary>
    /// Get sample embeddings for testing Lucene indexing
    /// </summary>
    public async Task<List<VectorData>> GetSampleEmbeddingsAsync(int limit = 100)
    {
        try
        {
            var vectorDataList = new List<VectorData>();

            var embeddings = await _context.Embeddings
                .OrderBy(e => e.Id)
                .Take(limit)
                .ToListAsync();

            foreach (var embedding in embeddings)
            {
                // Build metadata dictionary from individual properties
                var metadata = new Dictionary<string, object>
                {
                    ["record_uri"] = embedding.RecordUri,
                    ["record_title"] = embedding.RecordTitle ?? "",
                    ["date_created"] = embedding.DateCreated?.ToString("MM/dd/yyyy HH:mm:ss") ?? "",
                    ["record_type"] = embedding.RecordType ?? "",
                    ["container"] = embedding.Container ?? "",
                    ["assignee"] = embedding.Assignee ?? "",
                    ["chunk_content"] = embedding.ChunkContent ?? "",
                    ["chunk_index"] = embedding.ChunkIndex,
                    ["file_type"] = embedding.FileType ?? "",
                    ["file_extension"] = embedding.FileExtension ?? "",
                    ["document_category"] = embedding.DocumentCategory ?? ""
                };

                vectorDataList.Add(new VectorData
                {
                    Id = embedding.EmbeddingId,
                    Vector = embedding.Vector.ToArray(),
                    Metadata = metadata
                });
            }

            _logger.LogInformation("Retrieved {Count} sample embeddings from PostgreSQL", vectorDataList.Count);
            return vectorDataList;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get sample embeddings from PostgreSQL");
            return new List<VectorData>();
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
