using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocumentProcessingAPI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchVectorColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add search_vector column for Full-Text Search
            migrationBuilder.Sql(@"ALTER TABLE ""Embeddings"" ADD COLUMN IF NOT EXISTS search_vector tsvector;");

            // Create the trigger function to auto-update search_vector
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION update_search_vector()
                RETURNS TRIGGER AS $$
                BEGIN
                    NEW.search_vector :=
                        setweight(to_tsvector('english', COALESCE(NEW.""RecordTitle"", '')), 'A') ||
                        setweight(to_tsvector('english', COALESCE(NEW.""ChunkContent"", '')), 'B') ||
                        setweight(to_tsvector('english', COALESCE(NEW.""ContentPreview"", '')), 'C') ||
                        setweight(to_tsvector('english', COALESCE(NEW.""DocumentCategory"", '')), 'D');
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
            ");

            // Create the trigger
            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS tsvector_update_trigger ON ""Embeddings"";
                CREATE TRIGGER tsvector_update_trigger
                    BEFORE INSERT OR UPDATE ON ""Embeddings""
                    FOR EACH ROW
                    EXECUTE FUNCTION update_search_vector();
            ");

            // Create GIN index for fast full-text search
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_Embeddings_SearchVector"" ON ""Embeddings"" USING GIN(search_vector);");

            // Update existing records to populate search_vector
            migrationBuilder.Sql(@"
                UPDATE ""Embeddings"" SET
                    search_vector =
                        setweight(to_tsvector('english', COALESCE(""RecordTitle"", '')), 'A') ||
                        setweight(to_tsvector('english', COALESCE(""ChunkContent"", '')), 'B') ||
                        setweight(to_tsvector('english', COALESCE(""ContentPreview"", '')), 'C') ||
                        setweight(to_tsvector('english', COALESCE(""DocumentCategory"", '')), 'D')
                WHERE search_vector IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS tsvector_update_trigger ON ""Embeddings"";");
            migrationBuilder.Sql(@"DROP FUNCTION IF EXISTS update_search_vector();");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Embeddings_SearchVector"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Embeddings"" DROP COLUMN IF EXISTS search_vector;");
        }
    }
}
