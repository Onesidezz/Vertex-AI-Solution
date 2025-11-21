-- ================================================
-- CREATE ALL PRODUCTION INDEXES
-- Complete index setup for Embeddings table
-- Run Time: 1-5 minutes (on current 2,346 records)
-- ================================================

-- Prerequisites check
DO $$
BEGIN
    -- Check pgvector extension
    IF NOT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'vector') THEN
        RAISE EXCEPTION 'pgvector extension not installed. Run: CREATE EXTENSION vector;';
    END IF;

    RAISE NOTICE 'pgvector extension found ✓';
END $$;

-- Start transaction
BEGIN;

RAISE NOTICE '================================================';
RAISE NOTICE 'CREATING PRODUCTION INDEXES FOR EMBEDDINGS TABLE';
RAISE NOTICE 'This will take 1-5 minutes on current data size';
RAISE NOTICE '================================================';

-- ================================================
-- 1. VECTOR SIMILARITY INDEX (CRITICAL)
-- ================================================
RAISE NOTICE 'Creating vector similarity index (IVFFlat)...';

CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_embeddings_vector_ivfflat
ON "Embeddings"
USING ivfflat (("Vector"::halfvec(3072)) halfvec_cosine_ops)
WITH (lists = 100);

COMMENT ON INDEX idx_embeddings_vector_ivfflat IS
'IVFFlat index for fast vector similarity search. Rebuild with larger lists as data grows.';

RAISE NOTICE '✓ Vector index created';

-- ================================================
-- 2. FULL-TEXT SEARCH INDEX (CRITICAL)
-- ================================================
RAISE NOTICE 'Creating full-text search index (GIN)...';

-- Add search_vector column if not exists
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'Embeddings' AND column_name = 'search_vector'
    ) THEN
        ALTER TABLE "Embeddings" ADD COLUMN search_vector tsvector;
        RAISE NOTICE 'Added search_vector column';
    END IF;
END $$;

-- Create GIN index
CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_embeddings_search_vector
ON "Embeddings" USING GIN(search_vector);

COMMENT ON INDEX idx_embeddings_search_vector IS
'GIN index for PostgreSQL Full-Text Search with weighted fields.';

RAISE NOTICE '✓ Full-text search index created';

-- ================================================
-- 3. UNIQUE EMBEDDING ID INDEX
-- ================================================
RAISE NOTICE 'Creating unique EmbeddingId index...';

CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS idx_embeddings_embeddingid
ON "Embeddings" ("EmbeddingId");

COMMENT ON INDEX idx_embeddings_embeddingid IS
'Unique constraint on EmbeddingId (format: cm_record_{RecordUri}_chunk_{ChunkIndex})';

RAISE NOTICE '✓ EmbeddingId unique index created';

-- ================================================
-- 4. RECORD URI INDEX
-- ================================================
RAISE NOTICE 'Creating RecordUri index...';

CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_embeddings_recorduri
ON "Embeddings" ("RecordUri");

COMMENT ON INDEX idx_embeddings_recorduri IS
'B-tree index for fast record-level filtering and deletion';

RAISE NOTICE '✓ RecordUri index created';

-- ================================================
-- 5. DATE CREATED INDEX
-- ================================================
RAISE NOTICE 'Creating DateCreated index...';

CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_embeddings_datecreated
ON "Embeddings" ("DateCreated");

COMMENT ON INDEX idx_embeddings_datecreated IS
'B-tree index for date range filtering';

RAISE NOTICE '✓ DateCreated index created';

-- ================================================
-- 6. FILE TYPE INDEX
-- ================================================
RAISE NOTICE 'Creating FileType index...';

CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_embeddings_filetype
ON "Embeddings" ("FileType");

COMMENT ON INDEX idx_embeddings_filetype IS
'B-tree index for file type filtering (pdf, docx, xlsx, etc.)';

RAISE NOTICE '✓ FileType index created';

-- ================================================
-- 7. RECORD TYPE INDEX
-- ================================================
RAISE NOTICE 'Creating RecordType index...';

CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_embeddings_recordtype
ON "Embeddings" ("RecordType");

COMMENT ON INDEX idx_embeddings_recordtype IS
'B-tree index for Content Manager record type filtering';

RAISE NOTICE '✓ RecordType index created';

-- ================================================
-- 8. ENTITY TYPE INDEX
-- ================================================
RAISE NOTICE 'Creating EntityType index...';

CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_embeddings_entitytype
ON "Embeddings" ("EntityType");

COMMENT ON INDEX idx_embeddings_entitytype IS
'B-tree index for entity type filtering';

RAISE NOTICE '✓ EntityType index created';

-- ================================================
-- 9. COMPOSITE INDEX: RECORD + CHUNK SEQUENCE
-- ================================================
RAISE NOTICE 'Creating composite Record+Chunk index...';

CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_embeddings_record_chunks
ON "Embeddings" ("RecordUri", "ChunkSequence");

COMMENT ON INDEX idx_embeddings_record_chunks IS
'Composite index for ordered chunk retrieval within a record';

RAISE NOTICE '✓ Record+Chunk composite index created';

-- ================================================
-- 10. COMPOSITE INDEX: DATE + FILE TYPE (PARTIAL)
-- ================================================
RAISE NOTICE 'Creating composite Date+FileType index...';

CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_embeddings_date_filetype
ON "Embeddings" ("DateCreated", "FileType")
WHERE "DateCreated" IS NOT NULL;

COMMENT ON INDEX idx_embeddings_date_filetype IS
'Composite partial index for date+filetype queries';

RAISE NOTICE '✓ Date+FileType composite index created';

COMMIT;

-- ================================================
-- VERIFICATION
-- ================================================
RAISE NOTICE '================================================';
RAISE NOTICE 'INDEX CREATION COMPLETE!';
RAISE NOTICE '================================================';

-- Show all created indexes
SELECT
    indexname,
    indexdef,
    pg_size_pretty(pg_relation_size(indexname::regclass)) as size
FROM pg_indexes
WHERE tablename = 'Embeddings'
ORDER BY pg_relation_size(indexname::regclass) DESC;

-- Summary
SELECT
    'Index Summary' as report,
    COUNT(*) as total_indexes,
    pg_size_pretty(SUM(pg_relation_size(indexname::regclass))) as total_index_size
FROM pg_indexes
WHERE tablename = 'Embeddings';

RAISE NOTICE 'Run \d+ "Embeddings" to see all indexes';
