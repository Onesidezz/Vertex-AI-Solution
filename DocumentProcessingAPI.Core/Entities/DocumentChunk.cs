using System.ComponentModel.DataAnnotations;

namespace DocumentProcessingAPI.Core.Entities;

public class DocumentChunk
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DocumentId { get; set; }

    public int ChunkSequence { get; set; }

    [Required]
    public string Content { get; set; } = string.Empty;

    public int PageNumber { get; set; } = 1;

    public int TokenCount { get; set; }

    [StringLength(255)]
    public string? EmbeddingId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int StartPosition { get; set; }

    public int EndPosition { get; set; }

    public virtual Document Document { get; set; } = null!;
}