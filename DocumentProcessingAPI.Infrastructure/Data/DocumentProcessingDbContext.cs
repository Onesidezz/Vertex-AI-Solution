using DocumentProcessingAPI.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace DocumentProcessingAPI.Infrastructure.Data;

/// <summary>
/// Entity Framework Database Context for Document Processing API
/// </summary>
public class DocumentProcessingDbContext : DbContext
{
    public DocumentProcessingDbContext(DbContextOptions<DocumentProcessingDbContext> options) : base(options)
    {
    }

    public DbSet<Document> Documents { get; set; } = null!;
    public DbSet<DocumentChunk> DocumentChunks { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureDocumentEntity(modelBuilder);
        ConfigureDocumentChunkEntity(modelBuilder);
    }

    private static void ConfigureDocumentEntity(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Document>();

        entity.ToTable("Documents");

        entity.HasKey(d => d.Id);

        entity.Property(d => d.Id)
            .ValueGeneratedOnAdd();

        entity.Property(d => d.FileName)
            .IsRequired()
            .HasMaxLength(255);

        entity.Property(d => d.FilePath)
            .IsRequired()
            .HasMaxLength(500);

        entity.Property(d => d.ContentType)
            .HasMaxLength(100);

        entity.Property(d => d.OriginalFileName)
            .HasMaxLength(255);

        entity.Property(d => d.UserId)
            .HasMaxLength(36);

        entity.Property(d => d.UploadedAt)
            .HasDefaultValueSql("GETUTCDATE()");

        entity.Property(d => d.Status)
            .HasConversion<int>()
            .IsRequired();

        entity.HasIndex(d => d.UserId)
            .HasDatabaseName("IX_Documents_UserId");

        entity.HasIndex(d => d.Status)
            .HasDatabaseName("IX_Documents_Status");

        entity.HasIndex(d => d.UploadedAt)
            .HasDatabaseName("IX_Documents_UploadedAt");

        entity.HasIndex(d => new { d.UserId, d.Status })
            .HasDatabaseName("IX_Documents_UserId_Status");
    }

    private static void ConfigureDocumentChunkEntity(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DocumentChunk>();

        entity.ToTable("DocumentChunks");

        entity.HasKey(dc => dc.Id);

        entity.Property(dc => dc.Id)
            .ValueGeneratedOnAdd();

        entity.Property(dc => dc.Content)
            .IsRequired()
            .HasColumnType("nvarchar(max)");

        entity.Property(dc => dc.EmbeddingId)
            .HasMaxLength(255);

        entity.Property(dc => dc.CreatedAt)
            .HasDefaultValueSql("GETUTCDATE()");

        entity.HasOne(dc => dc.Document)
            .WithMany(d => d.Chunks)
            .HasForeignKey(dc => dc.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasIndex(dc => dc.DocumentId)
            .HasDatabaseName("IX_DocumentChunks_DocumentId");

        entity.HasIndex(dc => dc.ChunkSequence)
            .HasDatabaseName("IX_DocumentChunks_ChunkSequence");

        entity.HasIndex(dc => dc.EmbeddingId)
            .HasDatabaseName("IX_DocumentChunks_EmbeddingId");

        entity.HasIndex(dc => new { dc.DocumentId, dc.ChunkSequence })
            .HasDatabaseName("IX_DocumentChunks_DocumentId_ChunkSequence")
            .IsUnique();

        entity.HasIndex(dc => dc.PageNumber)
            .HasDatabaseName("IX_DocumentChunks_PageNumber");
    }
}