using DocumentProcessingAPI.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

namespace DocumentProcessingAPI.Infrastructure.Data;

/// <summary>
/// Entity Framework Database Context for Document Processing API
/// Supports both SQL Server (Documents) and PostgreSQL (Embeddings with pgvector)
/// </summary>
public class DocumentProcessingDbContext : DbContext
{
    public DocumentProcessingDbContext(DbContextOptions<DocumentProcessingDbContext> options) : base(options)
    {
    }

    public DbSet<Embedding> Embeddings { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Enable pgvector extension
        modelBuilder.HasPostgresExtension("vector");

        ConfigureEmbeddingEntity(modelBuilder);
    }

    private static void ConfigureEmbeddingEntity(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Embedding>();

        entity.ToTable("Embeddings");

        entity.HasKey(e => e.Id);

        entity.Property(e => e.Id)
            .ValueGeneratedOnAdd();

        entity.Property(e => e.EmbeddingId)
            .IsRequired()
            .HasMaxLength(255);

        // Configure vector column (3072 dimensions for Gemini embeddings)
        entity.Property(e => e.Vector)
            .IsRequired()
            .HasColumnType("vector(3072)");

        entity.Property(e => e.RecordUri)
            .IsRequired();

        entity.Property(e => e.RecordTitle)
            .HasMaxLength(500);

        entity.Property(e => e.RecordType)
            .HasMaxLength(50);

        entity.Property(e => e.Container)
            .HasMaxLength(500);

        entity.Property(e => e.Assignee)
            .HasMaxLength(255);

        entity.Property(e => e.AllParts)
            .HasColumnType("text");

        entity.Property(e => e.ACL)
            .HasColumnType("text");

        entity.Property(e => e.ChunkContent)
            .IsRequired()
            .HasColumnType("text");

        entity.Property(e => e.ContentPreview)
            .HasMaxLength(200);

        entity.Property(e => e.FileExtension)
            .HasMaxLength(200);

        entity.Property(e => e.FileType)
            .HasMaxLength(200);

        entity.Property(e => e.DocumentCategory)
            .HasMaxLength(200);

        entity.Property(e => e.EntityType)
            .IsRequired()
            .HasMaxLength(100)
            .HasDefaultValue("content_manager_record");

        entity.Property(e => e.IndexedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        entity.Property(e => e.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        // Create indexes
        entity.HasIndex(e => e.EmbeddingId)
            .IsUnique()
            .HasDatabaseName("IX_Embeddings_EmbeddingId");

        entity.HasIndex(e => e.RecordUri)
            .HasDatabaseName("IX_Embeddings_RecordUri");

        entity.HasIndex(e => e.DateCreated)
            .HasDatabaseName("IX_Embeddings_DateCreated");

        entity.HasIndex(e => e.FileType)
            .HasDatabaseName("IX_Embeddings_FileType");

        entity.HasIndex(e => e.RecordType)
            .HasDatabaseName("IX_Embeddings_RecordType");

        entity.HasIndex(e => e.EntityType)
            .HasDatabaseName("IX_Embeddings_EntityType");

        // NOTE: Vector index creation skipped in migration
        // pgvector v0.8.1 has a 2000-dimension limit for HNSW and IVFFlat indexes
        // Gemini embeddings are 3072 dimensions
        // Vector search will still work (using sequential scan initially)
        // Index can be added manually after data is loaded or when pgvector supports >2000 dims
    }
}