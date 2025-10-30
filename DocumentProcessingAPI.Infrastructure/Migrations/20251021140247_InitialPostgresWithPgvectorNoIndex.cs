using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Pgvector;

#nullable disable

namespace DocumentProcessingAPI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialPostgresWithPgvectorNoIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    FilePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TotalChunks = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    OriginalFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ProcessingError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Embeddings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmbeddingId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Vector = table.Column<Vector>(type: "vector(3072)", nullable: false),
                    RecordUri = table.Column<long>(type: "bigint", nullable: false),
                    RecordTitle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DateCreated = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RecordType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Container = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Assignee = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    AllParts = table.Column<string>(type: "text", nullable: true),
                    ACL = table.Column<string>(type: "text", nullable: true),
                    ChunkIndex = table.Column<int>(type: "integer", nullable: false),
                    ChunkSequence = table.Column<int>(type: "integer", nullable: false),
                    TotalChunks = table.Column<int>(type: "integer", nullable: false),
                    TokenCount = table.Column<int>(type: "integer", nullable: false),
                    StartPosition = table.Column<int>(type: "integer", nullable: false),
                    EndPosition = table.Column<int>(type: "integer", nullable: false),
                    PageNumber = table.Column<int>(type: "integer", nullable: false),
                    ChunkContent = table.Column<string>(type: "text", nullable: false),
                    ContentPreview = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    FileExtension = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    FileType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    DocumentCategory = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    EntityType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "content_manager_record"),
                    IndexedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Embeddings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DocumentChunks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChunkSequence = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    PageNumber = table.Column<int>(type: "integer", nullable: false),
                    TokenCount = table.Column<int>(type: "integer", nullable: false),
                    EmbeddingId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    StartPosition = table.Column<int>(type: "integer", nullable: false),
                    EndPosition = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentChunks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentChunks_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChunks_ChunkSequence",
                table: "DocumentChunks",
                column: "ChunkSequence");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChunks_DocumentId",
                table: "DocumentChunks",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChunks_DocumentId_ChunkSequence",
                table: "DocumentChunks",
                columns: new[] { "DocumentId", "ChunkSequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChunks_EmbeddingId",
                table: "DocumentChunks",
                column: "EmbeddingId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChunks_PageNumber",
                table: "DocumentChunks",
                column: "PageNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_Status",
                table: "Documents",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_UploadedAt",
                table: "Documents",
                column: "UploadedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_UserId",
                table: "Documents",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_UserId_Status",
                table: "Documents",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Embeddings_DateCreated",
                table: "Embeddings",
                column: "DateCreated");

            migrationBuilder.CreateIndex(
                name: "IX_Embeddings_EmbeddingId",
                table: "Embeddings",
                column: "EmbeddingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Embeddings_EntityType",
                table: "Embeddings",
                column: "EntityType");

            migrationBuilder.CreateIndex(
                name: "IX_Embeddings_FileType",
                table: "Embeddings",
                column: "FileType");

            migrationBuilder.CreateIndex(
                name: "IX_Embeddings_RecordType",
                table: "Embeddings",
                column: "RecordType");

            migrationBuilder.CreateIndex(
                name: "IX_Embeddings_RecordUri",
                table: "Embeddings",
                column: "RecordUri");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentChunks");

            migrationBuilder.DropTable(
                name: "Embeddings");

            migrationBuilder.DropTable(
                name: "Documents");
        }
    }
}
