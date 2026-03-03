using DocumentProcessingAPI.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace DocumentProcessingAPI.Infrastructure.Services;

/// <summary>
/// Ollama-based embedding service using local models
/// Supports models like bge-m3 and nomic-embed-text via Ollama's API
/// </summary>
public class OllamaEmbeddingService : IEmbeddingService
{
    private readonly ILogger<OllamaEmbeddingService> _logger;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly string _ollamaBaseUrl;
    private readonly string _embeddingModel;
    private readonly int _embeddingDimension;

    public OllamaEmbeddingService(
        ILogger<OllamaEmbeddingService> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;

        // Read Ollama configuration
        _ollamaBaseUrl = _configuration["Ollama:BaseUrl"] ?? "http://localhost:11434";
        _embeddingModel = _configuration["Ollama:EmbeddingModel"] ?? "bge-m3";

        if (!int.TryParse(_configuration["Ollama:EmbeddingDimension"], out _embeddingDimension))
        {
            _embeddingDimension = 1024; // Default for bge-m3
        }

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_ollamaBaseUrl),
            Timeout = TimeSpan.FromMinutes(2)
        };

        _logger.LogInformation("Ollama Embedding Service initialized");
        _logger.LogInformation("   • Base URL: {BaseUrl}", _ollamaBaseUrl);
        _logger.LogInformation("   • Model: {Model}", _embeddingModel);
        _logger.LogInformation("   • Embedding Dimension: {Dimension}", _embeddingDimension);
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
            _logger.LogDebug("Generating Ollama embedding for text (length: {Length})", text.Length);

            var requestBody = new
            {
                model = _embeddingModel,
                prompt = text
            };

            var jsonContent = JsonSerializer.Serialize(requestBody);
            var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/api/embeddings", httpContent);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Ollama API error: {StatusCode} - {Error}", response.StatusCode, errorContent);
                return new float[_embeddingDimension];
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var jsonResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);

            if (jsonResponse.TryGetProperty("embedding", out var embeddingArray))
            {
                var embedding = new List<float>();
                foreach (var element in embeddingArray.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.Number)
                    {
                        embedding.Add((float)element.GetDouble());
                    }
                }

                if (embedding.Count > 0)
                {
                    _logger.LogDebug("Successfully generated Ollama embedding with {Dimensions} dimensions", embedding.Count);
                    return embedding.ToArray();
                }
                else
                {
                    _logger.LogWarning("Ollama returned empty embedding array");
                    return new float[_embeddingDimension];
                }
            }
            else
            {
                _logger.LogWarning("Ollama response missing 'embedding' property");
                return new float[_embeddingDimension];
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate Ollama embedding for text");
            _logger.LogWarning("Returning zero vector as fallback");
            return new float[_embeddingDimension];
        }
    }
}
