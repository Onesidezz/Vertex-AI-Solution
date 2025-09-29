using DocumentProcessingAPI.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.IO.Abstractions;

namespace DocumentProcessingAPI.Infrastructure.Services;

/// <summary>
/// Local file storage service implementation
/// </summary>
public class LocalFileStorageService : IFileStorageService
{
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<LocalFileStorageService> _logger;
    private readonly string _storageBasePath;

    public LocalFileStorageService(
        IFileSystem fileSystem,
        ILogger<LocalFileStorageService> logger,
        IConfiguration configuration)
    {
        _fileSystem = fileSystem;
        _logger = logger;
        _storageBasePath = configuration["FileStorage:BasePath"] ?? Path.Combine(Path.GetTempPath(), "DocumentProcessingAPI");

        // Ensure storage directory exists
        if (!_fileSystem.Directory.Exists(_storageBasePath))
        {
            _fileSystem.Directory.CreateDirectory(_storageBasePath);
            _logger.LogInformation("Created storage directory: {StoragePath}", _storageBasePath);
        }
    }

    public async Task<string> StoreFileAsync(Stream fileStream, string fileName, string contentType)
    {
        if (fileStream == null)
            throw new ArgumentNullException(nameof(fileStream));

        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name cannot be empty", nameof(fileName));

        try
        {
            // Create a unique file path to prevent conflicts
            var fileExtension = Path.GetExtension(fileName);
            var uniqueFileName = $"{Guid.NewGuid()}{fileExtension}";
            var datePath = Path.Combine(DateTime.UtcNow.Year.ToString(), DateTime.UtcNow.Month.ToString("00"), DateTime.UtcNow.Day.ToString("00"));
            var relativePath = Path.Combine(datePath, uniqueFileName);
            var fullPath = Path.Combine(_storageBasePath, relativePath);

            // Ensure directory exists
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory) && !_fileSystem.Directory.Exists(directory))
            {
                _fileSystem.Directory.CreateDirectory(directory);
            }

            // Store the file
            using var fileStreamOut = _fileSystem.FileStream.New(fullPath, FileMode.Create, FileAccess.Write);
            await fileStream.CopyToAsync(fileStreamOut);

            _logger.LogInformation("Successfully stored file: {FileName} at {FilePath}", fileName, relativePath);
            return relativePath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store file: {FileName}", fileName);
            throw new InvalidOperationException($"Failed to store file: {ex.Message}", ex);
        }
    }

    public async Task<Stream> RetrieveFileAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be empty", nameof(filePath));

        try
        {
            var fullPath = Path.Combine(_storageBasePath, filePath);

            if (!_fileSystem.File.Exists(fullPath))
            {
                throw new FileNotFoundException($"File not found: {filePath}");
            }

            var fileStream = _fileSystem.FileStream.New(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            _logger.LogDebug("Successfully retrieved file: {FilePath}", filePath);

            return fileStream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve file: {FilePath}", filePath);
            throw;
        }
    }

    public async Task<bool> DeleteFileAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        try
        {
            var fullPath = Path.Combine(_storageBasePath, filePath);

            if (_fileSystem.File.Exists(fullPath))
            {
                _fileSystem.File.Delete(fullPath);
                _logger.LogInformation("Successfully deleted file: {FilePath}", filePath);

                // Clean up empty directories
                await CleanupEmptyDirectoriesAsync(Path.GetDirectoryName(fullPath));
                return true;
            }

            _logger.LogWarning("Attempted to delete non-existent file: {FilePath}", filePath);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete file: {FilePath}", filePath);
            return false;
        }
    }

    public async Task<bool> FileExistsAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        try
        {
            var fullPath = Path.Combine(_storageBasePath, filePath);
            return _fileSystem.File.Exists(fullPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking file existence: {FilePath}", filePath);
            return false;
        }
    }

    public async Task<FileMetadata?> GetFileMetadataAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        try
        {
            var fullPath = Path.Combine(_storageBasePath, filePath);

            if (!_fileSystem.File.Exists(fullPath))
            {
                return null;
            }

            var fileInfo = _fileSystem.FileInfo.New(fullPath);

            return new FileMetadata
            {
                FileName = Path.GetFileName(filePath),
                Size = fileInfo.Length,
                ContentType = GetContentType(Path.GetExtension(filePath)),
                LastModified = fileInfo.LastWriteTimeUtc,
                ETag = GenerateETag(fileInfo)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get file metadata: {FilePath}", filePath);
            return null;
        }
    }

    public async Task<string?> GeneratePresignedUrlAsync(string filePath, TimeSpan expirationTime)
    {
        // Local file storage doesn't support presigned URLs
        _logger.LogWarning("Presigned URLs are not supported for local file storage");
        return null;
    }

    private async Task CleanupEmptyDirectoriesAsync(string? directoryPath)
    {
        if (string.IsNullOrEmpty(directoryPath) || directoryPath == _storageBasePath)
            return;

        try
        {
            if (_fileSystem.Directory.Exists(directoryPath))
            {
                var entries = _fileSystem.Directory.GetFileSystemEntries(directoryPath);
                if (entries.Length == 0)
                {
                    _fileSystem.Directory.Delete(directoryPath);
                    _logger.LogDebug("Cleaned up empty directory: {DirectoryPath}", directoryPath);

                    // Recursively clean parent directories
                    await CleanupEmptyDirectoriesAsync(Path.GetDirectoryName(directoryPath));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup empty directory: {DirectoryPath}", directoryPath);
        }
    }

    private static string GetContentType(string fileExtension)
    {
        return fileExtension.ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".doc" => "application/msword",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".txt" => "text/plain",
            ".csv" => "text/csv",
            _ => "application/octet-stream"
        };
    }

    private static string GenerateETag(IFileInfo fileInfo)
    {
        // Simple ETag based on file size and last write time
        var etag = $"{fileInfo.Length}-{fileInfo.LastWriteTimeUtc.Ticks}";
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(etag));
    }
}