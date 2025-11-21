-- ========================================
-- DATABASE SIZE AND RECORD COUNT CHECK
-- ========================================
-- Run these queries to understand your current data scale
-- before creating vector indexes

-- 1. RECORD COUNT
SELECT
    COUNT(*) as total_records,
    CASE
        WHEN COUNT(*) < 100000 THEN 'Small (<1 lakh)'
        WHEN COUNT(*) < 1000000 THEN 'Medium (<10 lakh)'
        WHEN COUNT(*) < 10000000 THEN 'Large (<1 crore)'
        WHEN COUNT(*) < 100000000 THEN 'Very Large (<10 crore)'
        ELSE 'Massive (10+ crore)'
    END as scale,
    COUNT(*)::float / 10000000 as crores
FROM "Embeddings";

-- 2. TABLE SIZE BREAKDOWN
SELECT
    pg_size_pretty(pg_total_relation_size('"Embeddings"')) as total_size,
    pg_size_pretty(pg_relation_size('"Embeddings"')) as table_size,
    pg_size_pretty(pg_total_relation_size('"Embeddings"') - pg_relation_size('"Embeddings"')) as indexes_size,
    pg_size_pretty(pg_database_size('DocEmbeddings')) as database_size;

-- 3. VECTOR COLUMN STORAGE ANALYSIS
SELECT
    pg_size_pretty(pg_column_size("Vector")) as avg_vector_size_per_row,
    pg_size_pretty(SUM(pg_column_size("Vector"))::bigint) as total_vector_storage
FROM "Embeddings"
LIMIT 1000;  -- Sample for performance

-- 4. EXISTING INDEXES
SELECT
    indexname,
    indexdef,
    pg_size_pretty(pg_relation_size(indexname::regclass)) as index_size
FROM pg_indexes
WHERE tablename = 'Embeddings'
ORDER BY pg_relation_size(indexname::regclass) DESC;

-- 5. ESTIMATE HNSW INDEX SIZE (before creating)
-- Rule of thumb: HNSW index ≈ 1.5-2x vector column size
-- For halfvec: ~0.75-1x vector column size
SELECT
    'Estimated HNSW Index Size' as metric,
    pg_size_pretty(pg_total_relation_size('"Embeddings"') * 0.8) as "halfvec estimate (conservative)",
    pg_size_pretty(pg_total_relation_size('"Embeddings"') * 1.2) as "halfvec estimate (generous)";

-- 6. SAMPLE DATA CHECK
SELECT
    COUNT(*) as total_records,
    COUNT(DISTINCT "RecordUri") as unique_records,
    AVG("TotalChunks")::numeric(10,2) as avg_chunks_per_record,
    MAX("TotalChunks") as max_chunks,
    MIN("IndexedAt") as oldest_record,
    MAX("IndexedAt") as newest_record
FROM "Embeddings";

-- 7. STORAGE SAVINGS WITH HALFVEC
-- Calculate potential savings by using halfvec
SELECT
    COUNT(*) as records,
    pg_size_pretty((COUNT(*) * 3072 * 4)::bigint) as "current_vector_storage (vector)",
    pg_size_pretty((COUNT(*) * 3072 * 2)::bigint) as "with_halfvec (saves 50%)",
    pg_size_pretty((COUNT(*) * 3072 * 2)::bigint) as "savings"
FROM "Embeddings";

-- 8. CHUNKING STATISTICS
-- Understand your chunking pattern
SELECT
    "RecordType",
    COUNT(*) as chunk_count,
    COUNT(DISTINCT "RecordUri") as record_count,
    (COUNT(*)::float / NULLIF(COUNT(DISTINCT "RecordUri"), 0))::numeric(10,2) as avg_chunks_per_record
FROM "Embeddings"
GROUP BY "RecordType"
ORDER BY chunk_count DESC;

-- ========================================
-- RECOMMENDATION QUERY
-- ========================================
-- This will give you a recommendation based on your data size
SELECT
    CASE
        WHEN COUNT(*) < 1000000 THEN
            'RECOMMENDATION: Use HNSW with default parameters (m=16, ef_construction=64). Build time: <30 min'
        WHEN COUNT(*) < 10000000 THEN
            'RECOMMENDATION: Use HNSW with optimized parameters (m=12, ef_construction=48). Build time: 2-6 hours. Consider IVFFlat for faster builds.'
        WHEN COUNT(*) < 50000000 THEN
            'RECOMMENDATION: Use IVFFlat with lists=2000-3000. Build time: 4-8 hours. HNSW will take 12-30 hours.'
        ELSE
            'RECOMMENDATION: Use IVFFlat with lists=3000-5000. Build time: 8-24 hours. HNSW not recommended at this scale.'
    END as recommendation,
    COUNT(*) as current_records,
    COUNT(*)::float / 10000000 as crores
FROM "Embeddings";
