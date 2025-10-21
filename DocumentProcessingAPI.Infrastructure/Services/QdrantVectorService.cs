using Qdrant.Client;
using Qdrant.Client.Grpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DocumentProcessingAPI.Infrastructure.Services;

/// <summary>
/// Qdrant vector database service for storing and searching embeddings
/// Official Qdrant.Client SDK implementation
/// </summary>
public class QdrantVectorService : IDisposable
{
    private readonly QdrantClient _client;
    private readonly ILogger<QdrantVectorService> _logger;
    private readonly string _collectionName;
    private readonly ulong _vectorSize;
    private bool _disposed = false;

    public QdrantVectorService(IConfiguration configuration, ILogger<QdrantVectorService> logger)
    {
        _logger = logger;
        _collectionName = configuration["Qdrant:CollectionName"] ?? "document_chunks";
        _vectorSize = ulong.Parse(configuration["Qdrant:VectorDimension"] ?? "3072");

        var host = configuration["Qdrant:Host"] ?? "localhost";
        var https = bool.Parse(configuration["Qdrant:Https"] ?? "false");
        var apiKey = configuration["Qdrant:ApiKey"];

        // Create Qdrant client with API key for cloud instances
        _client = new QdrantClient(host: host, https: https, apiKey: apiKey);
        _logger.LogInformation("✅ QdrantVectorService initialized - {Host} (HTTPS: {Https}), Collection: {Collection}, Dimensions: {Dimensions}",
            host, https, _collectionName, _vectorSize);
    }

    /// <summary>
    /// Initialize Qdrant collection with proper configuration
    /// </summary>
    public async Task InitializeCollectionAsync()
    {
        try
        {
            // Check if collection exists
            var collections = await _client.ListCollectionsAsync();
            var collectionExists = collections.Any(c => c == _collectionName);

            if (!collectionExists)
            {
                _logger.LogInformation("🔨 Creating Qdrant collection: {CollectionName} with {Dimensions} dimensions",
                    _collectionName, _vectorSize);

                // Create collection with optimized settings
                await _client.CreateCollectionAsync(
                    collectionName: _collectionName,
                    vectorsConfig: new VectorParams
                    {
                        Size = _vectorSize,
                        Distance = Distance.Cosine,
                        OnDisk = false  // Keep in memory for POC (faster)
                    }
                );

                // Create payload indexes for fast filtering
                await _client.CreatePayloadIndexAsync(
                    collectionName: _collectionName,
                    fieldName: "document_id",
                    schemaType: PayloadSchemaType.Keyword
                );

                await _client.CreatePayloadIndexAsync(
                    collectionName: _collectionName,
                    fieldName: "user_id",
                    schemaType: PayloadSchemaType.Keyword
                );

                await _client.CreatePayloadIndexAsync(
                    collectionName: _collectionName,
                    fieldName: "chunk_sequence",
                    schemaType: PayloadSchemaType.Integer
                );

                // Create index for record_uri (Content Manager records)
                await _client.CreatePayloadIndexAsync(
                    collectionName: _collectionName,
                    fieldName: "record_uri",
                    schemaType: PayloadSchemaType.Integer
                );

                _logger.LogInformation("✅ Qdrant collection '{CollectionName}' created successfully with payload indexes",
                    _collectionName);
            }
            else
            {
                _logger.LogInformation("✅ Qdrant collection '{CollectionName}' already exists", _collectionName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to initialize Qdrant collection: {CollectionName}", _collectionName);
            throw;
        }
    }

    /// <summary>
    /// Save a single embedding to Qdrant
    /// </summary>
    public async Task SaveEmbeddingAsync(string embeddingId, float[] embedding, Dictionary<string, object> metadata)
    {
        try
        {
            // Generate deterministic UUID from string ID
            var uuid = GenerateUuidFromString(embeddingId);

            var point = new PointStruct
            {
                Id = new PointId { Uuid = uuid },
                Vectors = embedding,
                Payload = { }
            };

            // Add the original string ID to metadata for retrieval
            point.Payload["string_id"] = ConvertToQdrantValue(embeddingId);

            // Add metadata to payload
            foreach (var kvp in metadata)
            {
                point.Payload[kvp.Key] = ConvertToQdrantValue(kvp.Value);
            }

            await _client.UpsertAsync(
                collectionName: _collectionName,
                points: new[] { point },
                wait: true
            );

            _logger.LogDebug("💾 Saved embedding {EmbeddingId} to Qdrant", embeddingId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to save embedding {EmbeddingId} to Qdrant", embeddingId);
            throw;
        }
    }

    /// <summary>
    /// Save batch of embeddings to Qdrant (optimized for performance)
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
            // Ensure collection exists before saving
            await InitializeCollectionAsync();

            _logger.LogInformation("💾 Saving batch of {Count} embeddings to Qdrant", vectorData.Count);

            // Convert to Qdrant points
            var points = new List<PointStruct>();

            foreach (var v in vectorData)
            {
                // Generate deterministic UUID from string ID using MD5 hash
                var uuid = GenerateUuidFromString(v.Id);

                var point = new PointStruct
                {
                    Id = new PointId { Uuid = uuid },
                    Vectors = v.Vector,
                    Payload = { }
                };

                // Add the original string ID to metadata for retrieval
                point.Payload["string_id"] = ConvertToQdrantValue(v.Id);

                // Add all metadata fields
                foreach (var kvp in v.Metadata)
                {
                    point.Payload[kvp.Key] = ConvertToQdrantValue(kvp.Value);
                }

                points.Add(point);
            }

            // Batch upsert (Qdrant handles large batches efficiently)
            await _client.UpsertAsync(
                collectionName: _collectionName,
                points: points,
                wait: true  // Wait for indexing to complete
            );

            _logger.LogInformation("✅ Successfully saved {Count} embeddings to Qdrant", vectorData.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to save batch embeddings to Qdrant");
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
            // Generate UUID from string ID
            var uuid = GenerateUuidFromString(embeddingId);

            var points = await _client.RetrieveAsync(
                collectionName: _collectionName,
                ids: new[] { new PointId { Uuid = uuid } },
                withVectors: true,
                withPayload: true
            );

            if (points.Count == 0)
            {
                _logger.LogWarning("⚠️ Embedding not found in Qdrant: {EmbeddingId}", embeddingId);
                return null;
            }

            var point = points[0];
            var embedding = point.Vectors.Vector.Data.ToArray();
            var metadata = point.Payload.ToDictionary(
                kvp => kvp.Key,
                kvp => (object)kvp.Value.ToString()
            );

            return (embedding, metadata);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get embedding {EmbeddingId} from Qdrant", embeddingId);
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
            // Generate UUID from string ID
            var uuid = GenerateUuidFromString(embeddingId);

            await _client.DeleteAsync(
                collectionName: _collectionName,
                ids: new[] { new PointId { Uuid = uuid } },
                wait: true
            );

            _logger.LogInformation("🗑️ Deleted embedding {EmbeddingId} from Qdrant", embeddingId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to delete embedding {EmbeddingId} from Qdrant", embeddingId);
            return false;
        }
    }

    /// <summary>
    /// Delete all embeddings for a specific document
    /// </summary>
    public async Task<bool> DeleteEmbeddingsByDocumentAsync(Guid documentId)
    {
        try
        {
            _logger.LogInformation("🗑️ Deleting all embeddings for document {DocumentId}", documentId);

            await _client.DeleteAsync(
                collectionName: _collectionName,
                filter: new Filter
                {
                    Must =
                    {
                        new Condition
                        {
                            Field = new FieldCondition
                            {
                                Key = "document_id",
                                Match = new Match { Keyword = documentId.ToString() }
                            }
                        }
                    }
                },
                wait: true
            );

            _logger.LogInformation("✅ Deleted all embeddings for document {DocumentId} from Qdrant", documentId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to delete embeddings for document {DocumentId} from Qdrant", documentId);
            return false;
        }
    }

    /// <summary>
    /// Ensure record_uri index exists (for existing collections)
    /// Call this once to add the index if collection was created before this update
    /// </summary>
    public async Task<bool> EnsureRecordUriIndexAsync()
    {
        try
        {
            _logger.LogInformation("🔧 Ensuring record_uri index exists in collection {CollectionName}", _collectionName);

            await _client.CreatePayloadIndexAsync(
                collectionName: _collectionName,
                fieldName: "record_uri",
                schemaType: PayloadSchemaType.Integer
            );

            _logger.LogInformation("✅ Created record_uri index successfully");
            return true;
        }
        catch (Exception ex)
        {
            // Index might already exist, which is fine
            if (ex.Message.Contains("already exists") || ex.Message.Contains("conflict"))
            {
                _logger.LogInformation("✅ record_uri index already exists");
                return true;
            }

            _logger.LogError(ex, "❌ Failed to create record_uri index");
            return false;
        }
    }

    /// <summary>
    /// Get count of embeddings for a specific record URI
    /// Uses scroll API to efficiently find all chunks for a record
    /// </summary>
    public async Task<List<PointId>> GetPointIdsByRecordUriAsync(long recordUri)
    {
        try
        {
            _logger.LogDebug("🔍 Finding all point IDs for record URI {RecordUri}", recordUri);

            var pointIds = new List<PointId>();

            // Use scroll API to find all points matching the record_uri filter
            var scrollResult = await _client.ScrollAsync(
                collectionName: _collectionName,
                filter: new Filter
                {
                    Must =
                    {
                        new Condition
                        {
                            Field = new FieldCondition
                            {
                                Key = "record_uri",
                                Match = new Match { Integer = recordUri }
                            }
                        }
                    }
                },
                limit: 1000 // Get up to 1000 chunks per scroll (should be enough for any record)
            );

            // Extract point IDs from scroll response
            if (scrollResult.Result != null)
            {
                pointIds.AddRange(scrollResult.Result.Select(p => p.Id));
            }

            _logger.LogDebug("✅ Found {Count} point IDs for record URI {RecordUri}", pointIds.Count, recordUri);
            return pointIds;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get point IDs for record URI {RecordUri}", recordUri);
            return new List<PointId>();
        }
    }

    /// <summary>
    /// Delete all embeddings for a specific Content Manager record URI using metadata filter
    /// More efficient than individual deletions when dealing with many chunks
    /// Now with automatic index creation fallback
    /// </summary>
    public async Task<bool> DeleteEmbeddingsByRecordUriAsync(long recordUri)
    {
        try
        {
            _logger.LogInformation("🗑️ Deleting all embeddings for record URI {RecordUri} using filter", recordUri);

            await _client.DeleteAsync(
                collectionName: _collectionName,
                filter: new Filter
                {
                    Must =
                    {
                        new Condition
                        {
                            Field = new FieldCondition
                            {
                                Key = "record_uri",
                                Match = new Match { Integer = recordUri }
                            }
                        }
                    }
                },
                wait: true
            );

            _logger.LogInformation("✅ Deleted all embeddings for record URI {RecordUri} from Qdrant", recordUri);
            return true;
        }
        catch (Exception ex)
        {
            // Check if error is due to missing index
            if (ex.Message.Contains("Index required") || ex.Message.Contains("not found for \"record_uri\""))
            {
                _logger.LogWarning("⚠️ record_uri index missing, creating it now...");

                // Try to create the index
                var indexCreated = await EnsureRecordUriIndexAsync();

                if (indexCreated)
                {
                    _logger.LogInformation("✅ Index created, retrying deletion...");

                    // Retry deletion after creating index
                    try
                    {
                        await _client.DeleteAsync(
                            collectionName: _collectionName,
                            filter: new Filter
                            {
                                Must =
                                {
                                    new Condition
                                    {
                                        Field = new FieldCondition
                                        {
                                            Key = "record_uri",
                                            Match = new Match { Integer = recordUri }
                                        }
                                    }
                                }
                            },
                            wait: true
                        );

                        _logger.LogInformation("✅ Deleted all embeddings for record URI {RecordUri} after index creation", recordUri);
                        return true;
                    }
                    catch (Exception retryEx)
                    {
                        _logger.LogError(retryEx, "❌ Failed to delete after creating index");
                        return false;
                    }
                }
            }

            _logger.LogError(ex, "❌ Failed to delete embeddings for record URI {RecordUri} from Qdrant", recordUri);
            return false;
        }
    }

    /// <summary>
    /// Search for similar embeddings using vector similarity
    /// </summary>
    public async Task<List<(string id, float similarity, Dictionary<string, object> metadata)>> SearchSimilarAsync(
        float[] queryEmbedding, int limit = 10, float threshold = 0.0f, string? documentId = null)
    {
        try
        {
            _logger.LogDebug("🔍 Searching Qdrant for top {Limit} similar vectors (threshold: {Threshold})",
                limit, threshold);

            Filter? filter = null;

            // Add document filter if specified
            if (!string.IsNullOrEmpty(documentId))
            {
                filter = new Filter
                {
                    Must =
                    {
                        new Condition
                        {
                            Field = new FieldCondition
                            {
                                Key = "document_id",
                                Match = new Match { Keyword = documentId }
                            }
                        }
                    }
                };

                _logger.LogDebug("🔍 Filtering search by document_id: {DocumentId}", documentId);
            }

            _logger.LogInformation("🔍 Executing Qdrant search - Collection: {Collection}, VectorSize: {VectorSize}, Limit: {Limit}, Threshold: {Threshold}",
                _collectionName, queryEmbedding.Length, limit, threshold);

            var results = await _client.SearchAsync(
                collectionName: _collectionName,
                vector: queryEmbedding,
                filter: filter,
                limit: (ulong)limit,
                scoreThreshold: threshold,
                payloadSelector: new WithPayloadSelector { Enable = true }
            );

            _logger.LogInformation("🔍 Qdrant SearchAsync returned {ResultCount} results", results.Count);

            var searchResults = results.Select(r =>
            {
                var metadata = new Dictionary<string, object>();
                foreach (var kvp in r.Payload)
                {
                    // Convert Qdrant Value type to appropriate C# types
                    metadata[kvp.Key] = ConvertQdrantValue(kvp.Value);
                }

                // Use the original string_id from payload instead of UUID
                var stringId = metadata.ContainsKey("string_id")
                    ? metadata["string_id"].ToString()
                    : r.Id.Uuid;

                return (
                    id: stringId ?? r.Id.Uuid,
                    similarity: r.Score,
                    metadata: metadata
                );
            }).ToList();

            _logger.LogInformation("✅ Found {Count} similar vectors in Qdrant", searchResults.Count);

            return searchResults;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to search similar embeddings in Qdrant");
            return new List<(string, float, Dictionary<string, object>)>();
        }
    }

    /// <summary>
    /// Get collection info and statistics
    /// </summary>
    public async Task<CollectionInfo?> GetCollectionInfoAsync()
    {
        try
        {
            return await _client.GetCollectionInfoAsync(_collectionName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get collection info for {CollectionName}", _collectionName);
            return null;
        }
    }

    /// <summary>
    /// Helper method to convert C# objects to Qdrant Value types
    /// </summary>
    private Value ConvertToQdrantValue(object value)
    {
        return value switch
        {
            string s => new Value { StringValue = s },
            int i => new Value { IntegerValue = i },
            long l => new Value { IntegerValue = l },
            double d => new Value { DoubleValue = d },
            float f => new Value { DoubleValue = f },
            bool b => new Value { BoolValue = b },
            _ => new Value { StringValue = value?.ToString() ?? "" }
        };
    }

    /// <summary>
    /// Helper method to convert Qdrant Value types to C# objects
    /// </summary>
    private object ConvertQdrantValue(Value value)
    {
        if (value.HasStringValue)
            return value.StringValue;
        if (value.HasIntegerValue)
            return value.IntegerValue;
        if (value.HasDoubleValue)
            return value.DoubleValue;
        if (value.HasBoolValue)
            return value.BoolValue;

        return value.ToString();
    }

    /// <summary>
    /// Generate a deterministic UUID from a string ID using MD5 hash
    /// This ensures consistent UUIDs for the same string ID
    /// </summary>
    private string GenerateUuidFromString(string stringId)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(stringId));
        var guid = new Guid(hash);
        return guid.ToString();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _client?.Dispose();
            _disposed = true;
            _logger.LogInformation("QdrantVectorService disposed");
        }
    }
}

/// <summary>
/// Vector data structure for batch operations
/// </summary>
public class VectorData
{
    public string Id { get; set; } = string.Empty;
    public float[] Vector { get; set; } = Array.Empty<float>();
    public Dictionary<string, object> Metadata { get; set; } = new();
}
