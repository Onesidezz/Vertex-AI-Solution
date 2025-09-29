namespace DocumentProcessingAPI.Core.Interfaces;

/// <summary>
/// Service interface for generating and managing embeddings
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// Generate embedding for a single text
    /// </summary>
    /// <param name="text">Text to generate embedding for</param>
    /// <returns>Embedding vector</returns>
    Task<float[]> GenerateEmbeddingAsync(string text);
}