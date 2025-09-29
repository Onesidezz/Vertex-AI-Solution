namespace DocumentProcessingAPI.Core.Interfaces;

/// <summary>
/// Service interface for file storage operations
/// </summary>
public interface IFileStorageService
{
    /// <summary>
    /// Store a file and return the storage path
    /// </summary>
    /// <param name="fileStream">File stream</param>
    /// <param name="fileName">Original file name</param>
    /// <param name="contentType">MIME content type</param>
    /// <returns>Storage path of the saved file</returns>
    Task<string> StoreFileAsync(Stream fileStream, string fileName, string contentType);

    /// <summary>
    /// Retrieve a file stream
    /// </summary>
    /// <param name="filePath">Storage path of the file</param>
    /// <returns>File stream</returns>
    Task<Stream> RetrieveFileAsync(string filePath);

    /// <summary>
    /// Delete a file
    /// </summary>
    /// <param name="filePath">Storage path of the file</param>
    /// <returns>True if deleted successfully</returns>
    Task<bool> DeleteFileAsync(string filePath);

    /// <summary>
    /// Check if a file exists
    /// </summary>
    /// <param name="filePath">Storage path of the file</param>
    /// <returns>True if file exists</returns>
    Task<bool> FileExistsAsync(string filePath);

    /// <summary>
    /// Get file metadata
    /// </summary>
    /// <param name="filePath">Storage path of the file</param>
    /// <returns>File metadata</returns>
    Task<FileMetadata?> GetFileMetadataAsync(string filePath);

    /// <summary>
    /// Generate a presigned URL for file access (if supported)
    /// </summary>
    /// <param name="filePath">Storage path of the file</param>
    /// <param name="expirationTime">URL expiration time</param>
    /// <returns>Presigned URL or null if not supported</returns>
    Task<string?> GeneratePresignedUrlAsync(string filePath, TimeSpan expirationTime);
}

/// <summary>
/// File metadata
/// </summary>
public class FileMetadata
{
    public string FileName { get; set; } = string.Empty;
    public long Size { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
    public string ETag { get; set; } = string.Empty;
}