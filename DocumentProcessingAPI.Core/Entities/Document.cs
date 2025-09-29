using System.ComponentModel.DataAnnotations;

namespace DocumentProcessingAPI.Core.Entities;

public class Document
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [StringLength(255)]
    public string FileName { get; set; } = string.Empty;

    [Required]
    [StringLength(500)]
    public string FilePath { get; set; } = string.Empty;

    public long FileSize { get; set; }

    [StringLength(100)]
    public string ContentType { get; set; } = string.Empty;

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ProcessedAt { get; set; }

    public ProcessingStatus Status { get; set; } = ProcessingStatus.Pending;

    public int TotalChunks { get; set; }

    [StringLength(36)]
    public string? UserId { get; set; }

    [StringLength(255)]
    public string? OriginalFileName { get; set; }

    public string? ProcessingError { get; set; }

    public virtual ICollection<DocumentChunk> Chunks { get; set; } = new List<DocumentChunk>();
}

public enum ProcessingStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3
}