using DocumentProcessingAPI.Core.DTOs;
using DocumentProcessingAPI.Core.Entities;

namespace DocumentProcessingAPI.Core.Interfaces;

/// <summary>
/// Service interface for document operations
/// </summary>
public interface IDocumentService
{
    /// <summary>
    /// Upload and process a document
    /// </summary>
    /// <param name="request">Document upload request</param>
    /// <returns>Document response with processing status</returns>
    Task<DocumentResponseDto> UploadDocumentAsync(DocumentUploadRequestDto request);

    /// <summary>
    /// Get all documents with optional filtering
    /// </summary>
    /// <param name="userId">Optional user ID filter</param>
    /// <param name="status">Optional status filter</param>
    /// <param name="pageNumber">Page number for pagination</param>
    /// <param name="pageSize">Page size for pagination</param>
    /// <returns>List of documents</returns>
    Task<(List<DocumentResponseDto> Documents, int TotalCount)> GetDocumentsAsync(
        string? userId = null,
        ProcessingStatus? status = null,
        int pageNumber = 1,
        int pageSize = 20);

    /// <summary>
    /// Get document details with chunks
    /// </summary>
    /// <param name="documentId">Document ID</param>
    /// <returns>Document with chunks</returns>
    Task<DocumentDetailResponseDto?> GetDocumentDetailsAsync(Guid documentId);

    /// <summary>
    /// Delete a document and its chunks
    /// </summary>
    /// <param name="documentId">Document ID</param>
    /// <returns>True if deleted successfully</returns>
    Task<bool> DeleteDocumentAsync(Guid documentId);

    /// <summary>
    /// Get document processing status
    /// </summary>
    /// <param name="documentId">Document ID</param>
    /// <returns>Processing status</returns>
    Task<ProcessingStatus?> GetDocumentStatusAsync(Guid documentId);

    /// <summary>
    /// Reprocess a failed document
    /// </summary>
    /// <param name="documentId">Document ID</param>
    /// <returns>Updated document response</returns>
    Task<DocumentResponseDto?> ReprocessDocumentAsync(Guid documentId);
}