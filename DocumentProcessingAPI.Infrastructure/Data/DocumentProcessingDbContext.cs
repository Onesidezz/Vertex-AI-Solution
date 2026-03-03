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
    public DbSet<SyncCheckpoint> SyncCheckpoints { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Enable pgvector extension
        modelBuilder.HasPostgresExtension("vector");

        ConfigureEmbeddingEntity(modelBuilder);
        ConfigureSyncCheckpointEntity(modelBuilder);
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

        // Configure vector column (1024 dimensions for ONNX embeddings)
        entity.Property(e => e.Vector)
            .IsRequired()
            .HasColumnType("vector(1024)");

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

        // NOTE: SearchVector property is marked with [NotMapped] attribute
        // The search_vector column is managed by PostgreSQL triggers, not EF Core

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

        // NOTE: Vector index can be created for 1024-dimensional embeddings
        // pgvector supports HNSW and IVFFlat indexes up to 2000 dimensions
        // ONNX embeddings are 1024 dimensions - well within the limit
        // Index can be created after initial data load for better performance
    }

    private static void ConfigureSyncCheckpointEntity(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SyncCheckpoint>();

        entity.ToTable("SyncCheckpoints");

        entity.HasKey(e => e.Id);

        entity.Property(e => e.Id)
            .ValueGeneratedOnAdd();

        entity.Property(e => e.JobName)
            .IsRequired()
            .HasMaxLength(100);

        entity.Property(e => e.Status)
            .IsRequired()
            .HasMaxLength(50)
            .HasDefaultValue("Completed");

        entity.Property(e => e.ErrorMessage)
            .HasColumnType("text");

        entity.Property(e => e.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        entity.Property(e => e.UpdatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        // Create indexes
        entity.HasIndex(e => e.JobName)
            .IsUnique()
            .HasDatabaseName("IX_SyncCheckpoints_JobName");

        entity.HasIndex(e => e.Status)
            .HasDatabaseName("IX_SyncCheckpoints_Status");

        entity.HasIndex(e => e.LastSyncDate)
            .HasDatabaseName("IX_SyncCheckpoints_LastSyncDate");
    }
}