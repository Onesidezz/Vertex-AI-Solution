using DocumentProcessingAPI.Core.Entities;

namespace DocumentProcessingAPI.Core.Interfaces;

/// <summary>
/// Interface for document processing operations
/// </summary>
public interface IDocumentProcessor
{
    /// <summary>
    /// Extract text content from a document
    /// </summary>
    /// <param name="filePath">Path to the document file</param>
    /// <param name="contentType">MIME content type of the document</param>
    /// <returns>Extracted text content</returns>
    Task<string> ExtractTextAsync(string filePath, string contentType);

    /// <summary>
    /// Check if a file type is supported for processing
    /// </summary>
    /// <param name="contentType">MIME content type</param>
    /// <returns>True if supported</returns>
    bool IsFileTypeSupported(string contentType);

    /// <summary>
    /// Get supported file extensions
    /// </summary>
    /// <returns>List of supported extensions</returns>
    IEnumerable<string> GetSupportedExtensions();

    /// <summary>
    /// Validate file for processing
    /// </summary>
    /// <param name="filePath">Path to the file</param>
    /// <param name="contentType">MIME content type</param>
    /// <param name="fileSize">File size in bytes</param>
    /// <returns>Validation result with any error messages</returns>
    Task<(bool IsValid, string? ErrorMessage)> ValidateFileAsync(string filePath, string contentType, long fileSize);
}