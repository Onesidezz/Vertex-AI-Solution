using Pgvector;

namespace DocumentProcessingAPI.Core.Entities;

/// <summary>
/// Entity for storing Content Manager record embeddings with pgvector
/// Stores vector embeddings along with comprehensive metadata for filtering and search
/// </summary>
public class Embedding
{
    /// <summary>
    /// Primary key - Auto-increment ID
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Unique embedding identifier
    /// Format: cm_record_{URI}_chunk_{index}
    /// Example: cm_record_123456_chunk_0
    /// </summary>
    public string EmbeddingId { get; set; } = string.Empty;

    /// <summary>
    /// Vector embedding (1024 dimensions for Ollama bge-m3 model)
    /// Stored using pgvector type for efficient similarity search
    /// </summary>
    public Vector Vector { get; set; } = null!;

    // ============================================================
    // CONTENT MANAGER RECORD METADATA
    // ============================================================

    /// <summary>
    /// Content Manager record URI (unique identifier)
    /// Used for filtering and deletion
    /// </summary>
    public long RecordUri { get; set; }

    /// <summary>
    /// Record title from Content Manager
    /// </summary>
    public string RecordTitle { get; set; } = string.Empty;

    /// <summary>
    /// Date when the record was created in Content Manager
    /// Critical for date-based filtering
    /// </summary>
    public DateTime? DateCreated { get; set; }

    /// <summary>
    /// Date when the record was last modified in Content Manager
    /// Used for smart change detection - only reprocess if source modified after last embedding
    /// </summary>
    public DateTime? SourceDateModified { get; set; }

    /// <summary>
    /// Record type: "Container" or "Document"
    /// </summary>
    public string RecordType { get; set; } = string.Empty;

    /// <summary>
    /// Container name if this record belongs to a container
    /// </summary>
    public string? Container { get; set; }

    /// <summary>
    /// Record assignee
    /// </summary>
    public string? Assignee { get; set; }

    /// <summary>
    /// All parts information
    /// </summary>
    public string? AllParts { get; set; }

    /// <summary>
    /// Access Control List
    /// </summary>
    public string? ACL { get; set; }

    // ============================================================
    // CHUNK METADATA
    // ============================================================

    /// <summary>
    /// Chunk index within the record (0-based)
    /// </summary>
    public int ChunkIndex { get; set; }

    /// <summary>
    /// Chunk sequence number
    /// </summary>
    public int ChunkSequence { get; set; }

    /// <summary>
    /// Total number of chunks for this record
    /// </summary>
    public int TotalChunks { get; set; }

    /// <summary>
    /// Token count for this chunk
    /// </summary>
    public int TokenCount { get; set; }

    /// <summary>
    /// Start character position in original text
    /// </summary>
    public int StartPosition { get; set; }

    /// <summary>
    /// End character position in original text
    /// </summary>
    public int EndPosition { get; set; }

    /// <summary>
    /// Page number if applicable (for PDF documents)
    /// </summary>
    public int PageNumber { get; set; }

    /// <summary>
    /// Full chunk content text
    /// Stored for answer synthesis and context retrieval
    /// </summary>
    public string ChunkContent { get; set; } = string.Empty;

    /// <summary>
    /// Preview of chunk content (first 100 chars)
    /// For quick display without loading full content
    /// </summary>
    public string? ContentPreview { get; set; }

    // ============================================================
    // FILE METADATA
    // ============================================================

    /// <summary>
    /// File extension (e.g., ".pdf", ".docx")
    /// </summary>
    public string? FileExtension { get; set; }

    /// <summary>
    /// Normalized file type (e.g., "pdf", "docx")
    /// </summary>
    public string? FileType { get; set; }

    /// <summary>
    /// Document category for semantic grouping
    /// Examples: "PDF Document", "Word Document", "Excel Document", "Image"
    /// </summary>
    public string? DocumentCategory { get; set; }

    // ============================================================
    // SYSTEM METADATA
    // ============================================================

    /// <summary>
    /// Entity type identifier
    /// Value: "content_manager_record"
    /// </summary>
    public string EntityType { get; set; } = "content_manager_record";

    /// <summary>
    /// Timestamp when this embedding was indexed
    /// </summary>
    public DateTime IndexedAt { get; set; }

    /// <summary>
    /// Timestamp when this record was created in the database
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// PostgreSQL Full-Text Search vector (tsvector)
    /// Auto-generated from record_title, chunk_content, and metadata fields
    /// Used for fast keyword search with BM25-like ranking
    /// NOTE: This column is managed by PostgreSQL triggers, not Entity Framework
    /// </summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string? SearchVector { get; set; }
}
