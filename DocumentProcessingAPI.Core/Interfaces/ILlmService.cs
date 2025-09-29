using DocumentProcessingAPI.Core.DTOs;

namespace DocumentProcessingAPI.Core.Interfaces;

/// <summary>
/// Service interface for Large Language Model operations
/// </summary>
public interface ILlmService
{
    /// <summary>
    /// Generate a response based on a question and context
    /// </summary>
    /// <param name="question">The user's question</param>
    /// <param name="context">Retrieved context from documents</param>
    /// <returns>Generated response</returns>
    Task<string> GenerateResponseAsync(string question, string context);

    /// <summary>
    /// Generate a response with streaming support
    /// </summary>
    /// <param name="question">The user's question</param>
    /// <param name="context">Retrieved context from documents</param>
    /// <returns>Streaming response</returns>
    IAsyncEnumerable<string> GenerateStreamingResponseAsync(string question, string context);

    /// <summary>
    /// Check if the LLM service is healthy and available
    /// </summary>
    /// <returns>True if service is healthy</returns>
    Task<bool> IsHealthyAsync();
}