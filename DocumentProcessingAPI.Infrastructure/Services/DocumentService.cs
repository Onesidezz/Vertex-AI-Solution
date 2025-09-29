using DocumentProcessingAPI.Core.DTOs;
using DocumentProcessingAPI.Core.Entities;
using DocumentProcessingAPI.Core.Interfaces;
using DocumentProcessingAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DocumentProcessingAPI.Infrastructure.Services;

/// <summary>
/// Main document service orchestrating document processing pipeline
/// </summary>
public class DocumentService : IDocumentService
{
    private readonly DocumentProcessingDbContext _context;
    private readonly IFileStorageService _fileStorageService;
    private readonly IDocumentProcessor _documentProcessor;
    private readonly ITextChunkingService _textChunkingService;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILocalEmbeddingStorageService _embeddingStorageService;
    private readonly ILogger<DocumentService> _logger;

    public DocumentService(
        DocumentProcessingDbContext context,
        IFileStorageService fileStorageService,
        IDocumentProcessor documentProcessor,
        ITextChunkingService textChunkingService,
        IEmbeddingService embeddingService,
        ILocalEmbeddingStorageService embeddingStorageService,
        ILogger<DocumentService> logger)
    {
        _context = context;
        _fileStorageService = fileStorageService;
        _documentProcessor = documentProcessor;
        _textChunkingService = textChunkingService;
        _embeddingService = embeddingService;
        _embeddingStorageService = embeddingStorageService;
        _logger = logger;
    }

    public async Task<DocumentResponseDto> UploadDocumentAsync(DocumentUploadRequestDto request)
    {
        using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            _logger.LogInformation("Starting document upload for file: {FileName}", request.File.FileName);

            // Get content type
            var contentType = request.File.ContentType;

            // Store file
            string filePath;
            using (var stream = request.File.OpenReadStream())
            {
                filePath = await _fileStorageService.StoreFileAsync(stream, request.File.FileName, contentType);
            }

            // Validate file - convert relative path to full path
            var fullFilePath = await GetFullFilePathAsync(filePath);
            var (isValid, errorMessage) = await _documentProcessor.ValidateFileAsync(
                fullFilePath, contentType, request.File.Length);

            if (!isValid)
            {
                throw new InvalidOperationException($"File validation failed: {errorMessage}");
            }

            // Create document entity
            var document = new Document
            {
                FileName = request.File.FileName,
                OriginalFileName = request.File.FileName,
                FilePath = filePath,
                FileSize = request.File.Length,
                ContentType = contentType,
                UserId = request.UserId,
                Status = ProcessingStatus.Pending,
                UploadedAt = DateTime.UtcNow
            };

            _context.Documents.Add(document);
            await _context.SaveChangesAsync();

            if (request.ProcessImmediately)
            {
                await ProcessDocumentAsync(document, 1000, 200);
            }

            await transaction.CommitAsync();

            _logger.LogInformation("Successfully uploaded document: {DocumentId}", document.Id);

            return MapToDocumentResponseDto(document);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Failed to upload document: {FileName}", request.File.FileName);
            throw;
        }
    }

    public async Task<(List<DocumentResponseDto> Documents, int TotalCount)> GetDocumentsAsync(
        string? userId = null, ProcessingStatus? status = null, int pageNumber = 1, int pageSize = 20)
    {
        var query = _context.Documents.AsQueryable();

        if (!string.IsNullOrEmpty(userId))
        {
            query = query.Where(d => d.UserId == userId);
        }

        if (status.HasValue)
        {
            query = query.Where(d => d.Status == status.Value);
        }

        var totalCount = await query.CountAsync();

        var documents = await query
            .OrderByDescending(d => d.UploadedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var documentDtos = documents.Select(MapToDocumentResponseDto).ToList();

        return (documentDtos, totalCount);
    }

    public async Task<DocumentDetailResponseDto?> GetDocumentDetailsAsync(Guid documentId)
    {
        var document = await _context.Documents
            .Include(d => d.Chunks)
            .FirstOrDefaultAsync(d => d.Id == documentId);

        if (document == null)
            return null;

        var chunks = document.Chunks
            .OrderBy(c => c.ChunkSequence)
            .Select(c => new DocumentChunkDto
            {
                Id = c.Id,
                ChunkSequence = c.ChunkSequence,
                Content = c.Content,
                PageNumber = c.PageNumber,
                TokenCount = c.TokenCount,
                CreatedAt = c.CreatedAt,
                StartPosition = c.StartPosition,
                EndPosition = c.EndPosition
            })
            .ToList();

        return new DocumentDetailResponseDto
        {
            Id = document.Id,
            FileName = document.FileName,
            FileSize = document.FileSize,
            ContentType = document.ContentType,
            UploadedAt = document.UploadedAt,
            ProcessedAt = document.ProcessedAt,
            Status = document.Status,
            TotalChunks = document.TotalChunks,
            UserId = document.UserId,
            ProcessingError = document.ProcessingError,
            Chunks = chunks
        };
    }

    public async Task<bool> DeleteDocumentAsync(Guid documentId)
    {
        using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            var document = await _context.Documents.FirstOrDefaultAsync(d => d.Id == documentId);
            if (document == null)
                return false;

            // Delete embeddings from local storage
            await _embeddingStorageService.DeleteEmbeddingsByDocumentAsync(documentId);

            // Delete file from storage
            await _fileStorageService.DeleteFileAsync(document.FilePath);

            // Delete document and chunks from database (cascade delete will handle chunks)
            _context.Documents.Remove(document);
            await _context.SaveChangesAsync();

            await transaction.CommitAsync();

            _logger.LogInformation("Successfully deleted document: {DocumentId}", documentId);
            return true;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Failed to delete document: {DocumentId}", documentId);
            return false;
        }
    }

    public async Task<ProcessingStatus?> GetDocumentStatusAsync(Guid documentId)
    {
        var document = await _context.Documents.FirstOrDefaultAsync(d => d.Id == documentId);
        return document?.Status;
    }

    public async Task<DocumentResponseDto?> ReprocessDocumentAsync(Guid documentId)
    {
        var document = await _context.Documents.FirstOrDefaultAsync(d => d.Id == documentId);
        if (document == null)
            return null;

        // Reset status and clear error
        document.Status = ProcessingStatus.Pending;
        document.ProcessingError = null;
        document.ProcessedAt = null;
        document.TotalChunks = 0;

        // Clear existing chunks and embeddings
        var chunks = await _context.DocumentChunks.Where(c => c.DocumentId == documentId).ToListAsync();
        _context.DocumentChunks.RemoveRange(chunks);
        await _embeddingStorageService.DeleteEmbeddingsByDocumentAsync(documentId);

        await _context.SaveChangesAsync();

        // Reprocess document
        await ProcessDocumentAsync(document, 1000, 200);

        return MapToDocumentResponseDto(document);
    }

    private async Task ProcessDocumentAsync(Document document, int chunkSize, int chunkOverlap)
    {
        try
        {
            _logger.LogInformation("Processing document: {DocumentId}", document.Id);

            document.Status = ProcessingStatus.Processing;
            await _context.SaveChangesAsync();

            // Get full file path for processing
            var fullFilePath = await GetFullFilePathAsync(document.FilePath);

            // Extract text
            var extractedText = await _documentProcessor.ExtractTextAsync(fullFilePath, document.ContentType);

            if (string.IsNullOrWhiteSpace(extractedText))
            {
                throw new InvalidOperationException("No text could be extracted from the document");
            }

            // Chunk text
            var textChunks = await _textChunkingService.ChunkTextAsync(extractedText, chunkSize, chunkOverlap);

            if (!textChunks.Any())
            {
                throw new InvalidOperationException("No text chunks were created from the document");
            }

            // Create document chunks and generate embeddings
            var documentChunks = new List<DocumentChunk>();
            var vectorData = new List<VectorData>();
            int count = 0;

            _logger.LogInformation("Processing {ChunkCount} text chunks for document {DocumentId}",
                textChunks.Count, document.Id);

            foreach (var textChunk in textChunks)
            {
                var documentChunk = new DocumentChunk
                {
                    DocumentId = document.Id,
                    ChunkSequence = textChunk.Sequence,
                    Content = textChunk.Content,
                    PageNumber = textChunk.PageNumber,
                    TokenCount = textChunk.TokenCount,
                    StartPosition = textChunk.StartPosition,
                    EndPosition = textChunk.EndPosition,
                    EmbeddingId = $"{document.Id}_{textChunk.Sequence}",
                    CreatedAt = DateTime.UtcNow
                };

                documentChunks.Add(documentChunk);
                count++;
                _logger.LogInformation("Processing chunk {Count}/{Total} for document {DocumentId}",
                    count, textChunks.Count, document.Id);

                // Generate embedding
                var embedding = await _embeddingService.GenerateEmbeddingAsync(textChunk.Content);

                var metadata = new Dictionary<string, object>
                {
                    ["document_id"] = document.Id.ToString(),
                    ["chunk_sequence"] = textChunk.Sequence,
                    ["page_number"] = textChunk.PageNumber,
                    ["token_count"] = textChunk.TokenCount,
                    ["document_name"] = document.FileName,
                    ["content_type"] = document.ContentType,
                    ["user_id"] = document.UserId ?? string.Empty,
                    ["content_preview"] = textChunk.Content.Length > 100 ?
                        textChunk.Content.Substring(0, 100) + "..." : textChunk.Content
                };

                vectorData.Add(new VectorData
                {
                    Id = documentChunk.EmbeddingId,
                    Vector = embedding,
                    Metadata = metadata
                });
            }

            // Save chunks to database
            _context.DocumentChunks.AddRange(documentChunks);

            // Store embeddings in local storage
            _logger.LogInformation("Saving {Count} embeddings to local storage", vectorData.Count);
            await _embeddingStorageService.SaveEmbeddingsBatchAsync(vectorData);

            // Update document status
            document.Status = ProcessingStatus.Completed;
            document.ProcessedAt = DateTime.UtcNow;
            document.TotalChunks = documentChunks.Count;
            document.ProcessingError = null;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Successfully processed document: {DocumentId} with {ChunkCount} chunks",
                document.Id, documentChunks.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process document: {DocumentId}", document.Id);

            document.Status = ProcessingStatus.Failed;
            document.ProcessingError = ex.Message;
            await _context.SaveChangesAsync();

            throw;
        }
    }

    private async Task<string> GetFullFilePathAsync(string relativePath)
    {
        // Normalize path separators to fix mixed separator issues
        var normalizedRelativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);

        // Check if file exists using file storage service first
        var fileExists = await _fileStorageService.FileExistsAsync(normalizedRelativePath);
        if (!fileExists)
        {
            throw new FileNotFoundException($"File not found: {normalizedRelativePath}");
        }

        // Use the current working directory + uploads as configured in appsettings.json
        // The LocalFileStorageService should handle the base path correctly
        var basePath = Path.Combine(Directory.GetCurrentDirectory(), "uploads");

        return Path.Combine(basePath, normalizedRelativePath);
    }

    private static DocumentResponseDto MapToDocumentResponseDto(Document document)
    {
        return new DocumentResponseDto
        {
            Id = document.Id,
            FileName = document.FileName,
            FileSize = document.FileSize,
            ContentType = document.ContentType,
            UploadedAt = document.UploadedAt,
            ProcessedAt = document.ProcessedAt,
            Status = document.Status,
            TotalChunks = document.TotalChunks,
            UserId = document.UserId,
            ProcessingError = document.ProcessingError
        };
    }
}