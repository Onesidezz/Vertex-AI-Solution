-- Add PostgreSQL Full-Text Search to Embeddings Table
-- This replaces Lucene file system indexes with native PostgreSQL FTS

-- 1. Add tsvector column for full-text search
ALTER TABLE "Embeddings"
ADD COLUMN IF NOT EXISTS search_vector tsvector;

-- 2. Create GIN index for fast full-text search (similar to Lucene inverted index)
CREATE INDEX IF NOT EXISTS idx_embeddings_search_vector
ON "Embeddings" USING GIN(search_vector);

-- 3. Create function to build search_vector with field weighting
-- Weights: A (highest) for title, B for content, C for metadata, D (lowest) for misc
CREATE OR REPLACE FUNCTION build_embeddings_search_vector(
    p_record_title TEXT,
    p_chunk_content TEXT,
    p_container TEXT,
    p_assignee TEXT,
    p_record_type TEXT,
    p_file_type TEXT,
    p_document_category TEXT
) RETURNS tsvector AS $$
BEGIN
    RETURN
        -- Weight A (1.0): Record Title - Highest priority
        setweight(to_tsvector('english', COALESCE(p_record_title, '')), 'A') ||

        -- Weight B (0.4): Chunk Content - Main searchable content
        setweight(to_tsvector('english', COALESCE(p_chunk_content, '')), 'B') ||

        -- Weight C (0.2): Document Category & Record Type
        setweight(to_tsvector('english',
            COALESCE(p_document_category, '') || ' ' ||
            COALESCE(p_record_type, '')
        ), 'C') ||

        -- Weight D (0.1): Container, Assignee, File Type
        setweight(to_tsvector('english',
            COALESCE(p_container, '') || ' ' ||
            COALESCE(p_assignee, '') || ' ' ||
            COALESCE(p_file_type, '')
        ), 'D');
END;
$$ LANGUAGE plpgsql IMMUTABLE;

-- 4. Create trigger to auto-update search_vector on INSERT/UPDATE
CREATE OR REPLACE FUNCTION embeddings_search_vector_update()
RETURNS trigger AS $$
BEGIN
    NEW.search_vector := build_embeddings_search_vector(
        NEW."RecordTitle",
        NEW."ChunkContent",
        NEW."Container",
        NEW."Assignee",
        NEW."RecordType",
        NEW."FileType",
        NEW."DocumentCategory"
    );
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Drop trigger if exists (to avoid duplicate trigger error)
DROP TRIGGER IF EXISTS tsvector_update ON "Embeddings";

-- Create trigger
CREATE TRIGGER tsvector_update
BEFORE INSERT OR UPDATE ON "Embeddings"
FOR EACH ROW
EXECUTE FUNCTION embeddings_search_vector_update();

-- 5. Update existing records with search_vector
-- This will take some time for large datasets
UPDATE "Embeddings"
SET search_vector = build_embeddings_search_vector(
    "RecordTitle",
    "ChunkContent",
    "Container",
    "Assignee",
    "RecordType",
    "FileType",
    "DocumentCategory"
)
WHERE search_vector IS NULL;

-- 6. Verify the setup
SELECT
    COUNT(*) as total_records,
    COUNT(search_vector) as records_with_fts,
    pg_size_pretty(pg_total_relation_size('"Embeddings"')) as table_size,
    pg_size_pretty(pg_relation_size('idx_embeddings_search_vector')) as index_size
FROM "Embeddings";

-- 7. Test query example
-- Search for "service" with ts_rank scoring
SELECT
    "RecordUri",
    "RecordTitle",
    ts_rank(search_vector, websearch_to_tsquery('english', 'service')) as text_score,
    LEFT("ChunkContent", 100) as preview
FROM "Embeddings"
WHERE search_vector @@ websearch_to_tsquery('english', 'service')
ORDER BY text_score DESC
LIMIT 10;

-- Query explanation:
-- websearch_to_tsquery(): Parses natural language queries (handles phrases, AND/OR)
-- @@: Full-text search match operator
-- ts_rank(): Ranks results by relevance (like BM25 in Lucene)
-- GIN index: Makes search very fast (similar to Lucene inverted index)

COMMENT ON COLUMN "Embeddings".search_vector IS 'Full-text search vector with weighted fields: A=title(1.0), B=content(0.4), C=category(0.2), D=metadata(0.1)';
