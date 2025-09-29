using DocumentProcessingAPI.Core.Entities;

namespace DocumentProcessingAPI.Core.DTOs;

public class DocumentResponseDto
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public ProcessingStatus Status { get; set; }
    public int TotalChunks { get; set; }
    public string? UserId { get; set; }
    public string? ProcessingError { get; set; }
}

public class DocumentDetailResponseDto : DocumentResponseDto
{
    public List<DocumentChunkDto> Chunks { get; set; } = new();
}

public class DocumentChunkDto
{
    public Guid Id { get; set; }
    public int ChunkSequence { get; set; }
    public string Content { get; set; } = string.Empty;
    public int PageNumber { get; set; }
    public int TokenCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public int StartPosition { get; set; }
    public int EndPosition { get; set; }
}