using DocumentProcessingAPI.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace DocumentProcessingAPI.Infrastructure.Services;

/// <summary>
/// Google Gemini embedding service using the Gemini Embedding API
/// Provides high-quality embeddings via REST API calls
/// </summary>
public class GeminiEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GeminiEmbeddingService> _logger;
    private readonly IConfiguration _configuration;
    private readonly int _embeddingDimension;

    public GeminiEmbeddingService(IHttpClientFactory httpClientFactory, ILogger<GeminiEmbeddingService> logger, IConfiguration configuration)
    {
        _httpClient = httpClientFactory.CreateClient("Gemini");
        _logger = logger;
        _configuration = configuration;

        // Read embedding dimension from configuration, default to 3072 for highest quality
        if (!int.TryParse(_configuration["Gemini:EmbeddingDimension"], out _embeddingDimension))
        {
            _embeddingDimension = 3072; // Default to highest quality dimension
        }

        _logger.LogInformation("GeminiEmbeddingService initialized with {Dimensions} dimensions", _embeddingDimension);
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogWarning("Empty text provided for embedding generation");
            return new float[_embeddingDimension];
        }

        try
        {
            _logger.LogInformation("🔄 Generating embedding for text (length: {Length}) - Testing default dimensions (no outputDimensionality parameter)",
                text.Length);

            var request = new
            {
                model = "models/gemini-embedding-001",  // Correct model name
                content = new
                {
                    parts = new[]
                    {
                        new { text }
                    }
                }
                // Note: Removed outputDimensionality - let's test default behavior
            };

            var json = JsonSerializer.Serialize(request);
            _logger.LogInformation("📤 Sending Gemini API request to gemini-embedding-001:embedContent");

            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Correct endpoint
            var response = await _httpClient.PostAsync("models/gemini-embedding-001:embedContent", content);

            _logger.LogInformation("📨 Received response with status: {StatusCode}", response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Gemini API error: {StatusCode} - {Error}", response.StatusCode, errorContent);
                throw new HttpRequestException($"Gemini API failed: {response.StatusCode} - {errorContent}");
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(jsonResponse);

            // Convert to float[]
            var embedding = doc.RootElement
                              .GetProperty("embedding")
                              .GetProperty("values")
                              .EnumerateArray()
                              .Select(x => x.GetSingle())
                              .ToArray();

            _logger.LogInformation("✅ Successfully generated embedding with {Dimensions} dimensions", embedding.Length);

            // Validate dimensions
            if (embedding.Length != _embeddingDimension)
            {
                _logger.LogWarning("⚠️ Dimension mismatch! Expected: {Expected}, Got: {Actual}",
                    _embeddingDimension, embedding.Length);
            }

            return embedding;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate embedding for text: {Text}",
                text.Length > 100 ? text.Substring(0, 100) + "..." : text);
            throw;
        }
    }

    public async Task<List<float[]>> GenerateEmbeddingsBatchAsync(List<string> texts)
    {
        if (texts == null || !texts.Any())
        {
            _logger.LogWarning("Empty text list provided for batch embedding generation");
            return new List<float[]>();
        }

        _logger.LogInformation("Generating batch embeddings for {Count} texts", texts.Count);

        var results = new List<float[]>();
        var tasks = texts.Select(async text =>
        {
            try
            {
                return await GenerateEmbeddingAsync(text);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate embedding in batch");
                return new float[_embeddingDimension];
            }
        });

        var embeddings = await Task.WhenAll(tasks);
        results.AddRange(embeddings);

        _logger.LogInformation("Generated {Count} embeddings in batch", results.Count);
        return results;
    }

    public int GetEmbeddingDimension()
    {
        return _embeddingDimension;
    }

    public float CalculateCosineSimilarity(float[] embedding1, float[] embedding2)
    {
        if (embedding1 == null || embedding2 == null)
            throw new ArgumentNullException("Embeddings cannot be null");

        if (embedding1.Length != embedding2.Length)
            throw new ArgumentException("Embeddings must have the same dimensions");

        if (embedding1.Length == 0)
            return 0f;

        double dotProduct = 0;
        double norm1 = 0;
        double norm2 = 0;

        for (int i = 0; i < embedding1.Length; i++)
        {
            dotProduct += embedding1[i] * embedding2[i];
            norm1 += embedding1[i] * embedding1[i];
            norm2 += embedding2[i] * embedding2[i];
        }

        if (norm1 == 0 || norm2 == 0)
            return 0f;

        return (float)(dotProduct / (Math.Sqrt(norm1) * Math.Sqrt(norm2)));
    }

    public bool ValidateEmbedding(float[] embedding)
    {
        if (embedding == null)
            return false;

        if (embedding.Length != _embeddingDimension)
            return false;

        return embedding.All(x => !float.IsNaN(x) && !float.IsInfinity(x));
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}


// Gemini API DTOs
public class GeminiEmbeddingRequest
{
    public string Model { get; set; } = string.Empty;
    public GeminiContent[] Contents { get; set; } = Array.Empty<GeminiContent>();
    public GeminiEmbeddingConfig? EmbeddingConfig { get; set; }
}

public class GeminiContent
{
    public GeminiPart[] Parts { get; set; } = Array.Empty<GeminiPart>();
}

public class GeminiPart
{
    public string Text { get; set; } = string.Empty;
}

public class GeminiEmbeddingConfig
{
    public int OutputDimensionality { get; set; }
}

public class GeminiEmbeddingResponse
{
    public GeminiEmbedding? Embedding { get; set; }
}

public class GeminiEmbedding
{
    public float[] Values { get; set; } = Array.Empty<float>();
}