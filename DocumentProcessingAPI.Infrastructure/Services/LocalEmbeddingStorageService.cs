using DocumentProcessingAPI.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DocumentProcessingAPI.Infrastructure.Services;

public interface ILocalEmbeddingStorageService
{
    Task SaveEmbeddingAsync(string embeddingId, float[] embedding, Dictionary<string, object> metadata);
    Task SaveEmbeddingsBatchAsync(List<VectorData> vectorData);
    Task<(float[] embedding, Dictionary<string, object> metadata)?> GetEmbeddingAsync(string embeddingId);
    Task<List<(string id, float[] embedding, Dictionary<string, object> metadata)>> GetAllEmbeddingsAsync();
    Task<bool> DeleteEmbeddingAsync(string embeddingId);
    Task<bool> DeleteEmbeddingsByDocumentAsync(Guid documentId);
    Task<List<(string id, float[] embedding, Dictionary<string, object> metadata, float similarity)>> SearchSimilarAsync(
        float[] queryEmbedding, int limit = 10, float threshold = 0.0f, string? documentId = null);
}

public class LocalEmbeddingStorageService : ILocalEmbeddingStorageService
{
    private readonly ILogger<LocalEmbeddingStorageService> _logger;
    private readonly string _storagePath;
    private readonly JsonSerializerOptions _jsonOptions;

    public LocalEmbeddingStorageService(IConfiguration configuration, ILogger<LocalEmbeddingStorageService> logger)
    {
        _logger = logger;
        _storagePath = configuration["Embeddings:StoragePath"] ?? "C:\\Users\\ukhan2\\source\\repos\\DocumentProcessingAPI\\Embeddings";

        // Ensure directory exists
        Directory.CreateDirectory(_storagePath);

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task SaveEmbeddingAsync(string embeddingId, float[] embedding, Dictionary<string, object> metadata)
    {
        try
        {
            var embeddingData = new EmbeddingData
            {
                Id = embeddingId,
                Vector = embedding,
                Metadata = metadata,
                CreatedAt = DateTime.UtcNow
            };

            var filePath = Path.Combine(_storagePath, $"{embeddingId}.json");
            var json = JsonSerializer.Serialize(embeddingData, _jsonOptions);

            await File.WriteAllTextAsync(filePath, json);

            _logger.LogInformation("Saved embedding {EmbeddingId} to local storage with {Dimensions} dimensions",
                embeddingId, embedding.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save embedding {EmbeddingId} to local storage", embeddingId);
            throw;
        }
    }

    public async Task SaveEmbeddingsBatchAsync(List<VectorData> vectorData)
    {
        _logger.LogInformation("Saving batch of {Count} embeddings to local storage", vectorData.Count);

        var tasks = vectorData.Select(async vd =>
        {
            try
            {
                await SaveEmbeddingAsync(vd.Id, vd.Vector, vd.Metadata);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save embedding {Id} in batch", vd.Id);
            }
        });

        await Task.WhenAll(tasks);
        _logger.LogInformation("Completed batch save of embeddings");
    }

    public async Task<(float[] embedding, Dictionary<string, object> metadata)?> GetEmbeddingAsync(string embeddingId)
    {
        try
        {
            var filePath = Path.Combine(_storagePath, $"{embeddingId}.json");

            if (!File.Exists(filePath))
            {
                _logger.LogWarning("Embedding file not found: {EmbeddingId}", embeddingId);
                return null;
            }

            var json = await File.ReadAllTextAsync(filePath);
            var embeddingData = JsonSerializer.Deserialize<EmbeddingData>(json, _jsonOptions);

            if (embeddingData == null)
            {
                _logger.LogWarning("Failed to deserialize embedding data: {EmbeddingId}", embeddingId);
                return null;
            }

            return (embeddingData.Vector, embeddingData.Metadata);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get embedding {EmbeddingId} from local storage", embeddingId);
            return null;
        }
    }

    public async Task<List<(string id, float[] embedding, Dictionary<string, object> metadata)>> GetAllEmbeddingsAsync()
    {
        var results = new List<(string id, float[] embedding, Dictionary<string, object> metadata)>();

        try
        {
            var files = Directory.GetFiles(_storagePath, "*.json");
            _logger.LogInformation("Found {Count} embedding files in local storage", files.Length);

            var tasks = files.Select(async filePath =>
            {
                try
                {
                    var json = await File.ReadAllTextAsync(filePath);
                    var embeddingData = JsonSerializer.Deserialize<EmbeddingData>(json, _jsonOptions);

                    if (embeddingData != null)
                    {
                        return (embeddingData.Id, embeddingData.Vector, embeddingData.Metadata);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load embedding from file: {FilePath}", filePath);
                }

                return ((string, float[], Dictionary<string, object>)?)null;
            });

            var loadedEmbeddings = await Task.WhenAll(tasks);
            results.AddRange(loadedEmbeddings.Where(e => e.HasValue).Select(e => e.Value));

            _logger.LogInformation("Successfully loaded {Count} embeddings from local storage", results.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all embeddings from local storage");
        }

        return results;
    }

    public async Task<bool> DeleteEmbeddingAsync(string embeddingId)
    {
        try
        {
            var filePath = Path.Combine(_storagePath, $"{embeddingId}.json");

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _logger.LogInformation("Deleted embedding {EmbeddingId} from local storage", embeddingId);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete embedding {EmbeddingId} from local storage", embeddingId);
            return false;
        }
    }

    public async Task<bool> DeleteEmbeddingsByDocumentAsync(Guid documentId)
    {
        try
        {
            var allEmbeddings = await GetAllEmbeddingsAsync();
            var documentEmbeddings = allEmbeddings
                .Where(e => e.metadata.ContainsKey("document_id") &&
                           e.metadata["document_id"].ToString() == documentId.ToString())
                .ToList();

            var deleteTasks = documentEmbeddings.Select(e => DeleteEmbeddingAsync(e.id));
            await Task.WhenAll(deleteTasks);

            _logger.LogInformation("Deleted {Count} embeddings for document {DocumentId}",
                documentEmbeddings.Count, documentId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete embeddings for document {DocumentId}", documentId);
            return false;
        }
    }

    public async Task<List<(string id, float[] embedding, Dictionary<string, object> metadata, float similarity)>> SearchSimilarAsync(
        float[] queryEmbedding, int limit = 10, float threshold = 0.0f, string? documentId = null)
    {
        try
        {
            var allEmbeddings = await GetAllEmbeddingsAsync();
            var results = new List<(string id, float[] embedding, Dictionary<string, object> metadata, float similarity)>();

            foreach (var (id, embedding, metadata) in allEmbeddings)
            {
                // Filter by document if specified
                if (!string.IsNullOrEmpty(documentId) &&
                    metadata.ContainsKey("document_id") &&
                    metadata["document_id"].ToString() != documentId)
                {
                    continue;
                }

                var similarity = CalculateCosineSimilarity(queryEmbedding, embedding);

                if (similarity >= threshold)
                {
                    results.Add((id, embedding, metadata, similarity));
                }
            }

            // Sort by similarity (descending) and take top results
            var topResults = results
                .OrderByDescending(r => r.similarity)
                .Take(limit)
                .ToList();

            _logger.LogInformation("Found {Count} similar embeddings (threshold: {Threshold}, limit: {Limit})",
                topResults.Count, threshold, limit);

            return topResults;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search similar embeddings");
            return new List<(string, float[], Dictionary<string, object>, float)>();
        }
    }

    private static float CalculateCosineSimilarity(float[] vector1, float[] vector2)
    {
        if (vector1.Length != vector2.Length)
            return 0f;

        double dotProduct = 0;
        double norm1 = 0;
        double norm2 = 0;

        for (int i = 0; i < vector1.Length; i++)
        {
            dotProduct += vector1[i] * vector2[i];
            norm1 += vector1[i] * vector1[i];
            norm2 += vector2[i] * vector2[i];
        }

        if (norm1 == 0 || norm2 == 0)
            return 0f;

        return (float)(dotProduct / (Math.Sqrt(norm1) * Math.Sqrt(norm2)));
    }
}

// Data models for JSON serialization
public class EmbeddingData
{
    public string Id { get; set; } = string.Empty;
    public float[] Vector { get; set; } = Array.Empty<float>();
    public Dictionary<string, object> Metadata { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

