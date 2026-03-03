using DocumentProcessingAPI.Core.Interfaces;
using Google.Cloud.AIPlatform.V1;
using Google.Apis.Auth.OAuth2;
using Grpc.Auth;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DocumentProcessingAPI.Infrastructure.Services;

/// <summary>
/// Google Vertex AI embedding service using gemini-embedding-001
/// Provides high-quality embeddings via Vertex AI SDK
/// Includes retry logic with exponential backoff for quota management
/// </summary>
public class GeminiEmbeddingService : IEmbeddingService
{
    private readonly ILogger<GeminiEmbeddingService> _logger;
    private readonly IConfiguration _configuration;
    private readonly int _embeddingDimension;
    private readonly string _projectId;
    private readonly string _location;
    private readonly string _embeddingModel;
    private readonly PredictionServiceClient _predictionClient;

    // Retry configuration
    private const int MaxRetries = 5;
    private const int InitialRetryDelayMs = 2000; // 2 seconds
    private const int MaxRetryDelayMs = 60000; // 60 seconds max

    public GeminiEmbeddingService(ILogger<GeminiEmbeddingService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;

        // Read Vertex AI configuration
        _projectId = _configuration["VertexAI:ProjectId"] ?? throw new InvalidOperationException("VertexAI:ProjectId not configured");
        _location = _configuration["VertexAI:Location"] ?? "us-central1";
        _embeddingModel = _configuration["VertexAI:EmbeddingModel"] ?? "gemini-embedding-001";

        // Read embedding dimension from configuration
        if (!int.TryParse(_configuration["VertexAI:EmbeddingDimension"], out _embeddingDimension))
        {
            _embeddingDimension = 3072; // Default dimension for gemini-embedding-001
        }

        // Initialize Vertex AI client with service account authentication
        var serviceAccountKeyPath = _configuration["VertexAI:ServiceAccountKeyPath"];

        if (string.IsNullOrEmpty(serviceAccountKeyPath))
        {
            _logger.LogError("VertexAI:ServiceAccountKeyPath not configured in appsettings.json");
            throw new InvalidOperationException("Service account key path is not configured. Please set VertexAI:ServiceAccountKeyPath in appsettings.json");
        }

        if (!File.Exists(serviceAccountKeyPath))
        {
            _logger.LogError("Service account key file not found at: {Path}", serviceAccountKeyPath);
            throw new FileNotFoundException($"Service account key file not found at: {serviceAccountKeyPath}");
        }

        _logger.LogInformation("🔑 Loading service account credentials from: {Path}", serviceAccountKeyPath);

        // Load service account credentials
        GoogleCredential credential;
        using (var stream = new FileStream(serviceAccountKeyPath, FileMode.Open, FileAccess.Read))
        {
            credential = GoogleCredential.FromStream(stream)
                .CreateScoped("https://www.googleapis.com/auth/cloud-platform");
        }

        // Initialize client with explicit credentials
        _predictionClient = new PredictionServiceClientBuilder
        {
            Endpoint = $"{_location}-aiplatform.googleapis.com",
            ChannelCredentials = credential.ToChannelCredentials()
        }.Build();

        _logger.LogInformation("✅ Vertex AI Embedding Service initialized - Project: {Project}, Location: {Location}, Model: {Model}, Dimensions: {Dimensions}",
            _projectId, _location, _embeddingModel, _embeddingDimension);
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogWarning("Empty text provided for embedding generation");
            return new float[_embeddingDimension];
        }

        int retryCount = 0;
        int delayMs = InitialRetryDelayMs;

        while (true)
        {
            try
            {
                _logger.LogInformation("🔄 Generating Vertex AI embedding for text (length: {Length})", text.Length);

                // Build the endpoint name
                var endpoint = EndpointName.FromProjectLocationPublisherModel(
                    _projectId,
                    _location,
                    "google",
                    _embeddingModel
                );

                // Create the prediction request
                var instance = new Google.Protobuf.WellKnownTypes.Value
                {
                    StructValue = new Google.Protobuf.WellKnownTypes.Struct
                    {
                        Fields =
                        {
                            ["content"] = Google.Protobuf.WellKnownTypes.Value.ForString(text)
                        }
                    }
                };

                var parameters = new Google.Protobuf.WellKnownTypes.Value
                {
                    StructValue = new Google.Protobuf.WellKnownTypes.Struct
                    {
                        Fields =
                        {
                            ["outputDimensionality"] = Google.Protobuf.WellKnownTypes.Value.ForNumber(_embeddingDimension)
                        }
                    }
                };

                var request = new PredictRequest
                {
                    EndpointAsEndpointName = endpoint,
                    Instances = { instance },
                    Parameters = parameters
                };

                // Call Vertex AI
                var response = await _predictionClient.PredictAsync(request);

                if (response.Predictions.Count == 0)
                {
                    _logger.LogError("No predictions returned from Vertex AI");
                    throw new InvalidOperationException("No embeddings returned from Vertex AI");
                }

                // Extract embedding values
                var prediction = response.Predictions[0];
                var embeddingsField = prediction.StructValue.Fields["embeddings"];
                var valuesField = embeddingsField.StructValue.Fields["values"];

                var embedding = valuesField.ListValue.Values
                    .Select(v => (float)v.NumberValue)
                    .ToArray();

                _logger.LogInformation("✅ Successfully generated Vertex AI embedding with {Dimensions} dimensions", embedding.Length);

                // Validate dimensions
                if (embedding.Length != _embeddingDimension)
                {
                    _logger.LogWarning("⚠️ Dimension mismatch! Expected: {Expected}, Got: {Actual}",
                        _embeddingDimension, embedding.Length);
                }

                return embedding;
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.ResourceExhausted && retryCount < MaxRetries)
            {
                retryCount++;
                _logger.LogWarning("⚠️ Quota exceeded (attempt {Attempt}/{MaxRetries}). Waiting {Delay}ms before retry...",
                    retryCount, MaxRetries, delayMs);

                await Task.Delay(delayMs);

                // Exponential backoff with jitter
                delayMs = Math.Min(delayMs * 2 + Random.Shared.Next(0, 1000), MaxRetryDelayMs);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable && retryCount < MaxRetries)
            {
                retryCount++;
                _logger.LogWarning("⚠️ Service unavailable (attempt {Attempt}/{MaxRetries}). Waiting {Delay}ms before retry...",
                    retryCount, MaxRetries, delayMs);

                await Task.Delay(delayMs);
                delayMs = Math.Min(delayMs * 2 + Random.Shared.Next(0, 1000), MaxRetryDelayMs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate Vertex AI embedding for text: {Text}",
                    text.Length > 100 ? text.Substring(0, 100) + "..." : text);
                throw;
            }
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
        // Vertex AI client handles its own disposal
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