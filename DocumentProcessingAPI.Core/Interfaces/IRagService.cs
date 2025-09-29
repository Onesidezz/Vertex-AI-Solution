using DocumentProcessingAPI.Core.DTOs;

namespace DocumentProcessingAPI.Core.Interfaces;

/// <summary>
/// Service interface for Retrieval-Augmented Generation (RAG) operations
/// </summary>
public interface IRagService
{
    /// <summary>
    /// Ask a natural language question and get an AI-generated response with sources
    /// </summary>
    /// <param name="request">The RAG request containing the question and parameters</param>
    /// <returns>AI response with source citations</returns>
    Task<RagResponseDto> AskQuestionAsync(RagRequestDto request);

    /// <summary>
    /// Ask a question with streaming response
    /// </summary>
    /// <param name="request">The RAG request</param>
    /// <returns>Streaming AI response</returns>
    IAsyncEnumerable<RagStreamResponseDto> AskQuestionStreamAsync(RagRequestDto request);

    /// <summary>
    /// Ask a question about a specific document
    /// </summary>
    /// <param name="documentId">Document ID to search within</param>
    /// <param name="request">The RAG request</param>
    /// <returns>AI response with document-specific sources</returns>
    Task<RagResponseDto> AskQuestionAboutDocumentAsync(Guid documentId, RagRequestDto request);
}