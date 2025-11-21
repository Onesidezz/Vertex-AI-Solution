-- ================================================
-- PRODUCTION MONITORING QUERIES
-- Run these regularly to track database health
-- ================================================

-- ================================================
-- DAILY HEALTH CHECK (Run every morning)
-- ================================================

\echo '================================================'
\echo 'DAILY DATABASE HEALTH CHECK'
\echo '================================================'

-- 1. Data Volume & Growth
\echo '\n--- DATA VOLUME ---'
SELECT
    COUNT(*) as total_chunks,
    (COUNT(*) / 8.63)::int as approx_records,
    (COUNT(*) / 10000000.0)::numeric(6,3) as crores,
    pg_size_pretty(pg_total_relation_size('"Embeddings"')) as total_size,
    pg_size_pretty(pg_relation_size('"Embeddings"')) as table_only,
    pg_size_pretty(pg_total_relation_size('"Embeddings"') - pg_relation_size('"Embeddings"')) as indexes_size
FROM "Embeddings";

-- 2. Growth Rate (Last 24 hours)
\echo '\n--- GROWTH RATE ---'
SELECT
    COUNT(*) FILTER (WHERE "CreatedAt" > NOW() - INTERVAL '24 hours') as last_24h_chunks,
    COUNT(*) FILTER (WHERE "CreatedAt" > NOW() - INTERVAL '1 hour') as last_1h_chunks,
    (COUNT(*) FILTER (WHERE "CreatedAt" > NOW() - INTERVAL '24 hours') / 24.0)::numeric(10,1) as avg_per_hour,
    CASE
        WHEN COUNT(*) FILTER (WHERE "CreatedAt" > NOW() - INTERVAL '24 hours') > 0 THEN
            ((86300000 - COUNT(*))::float /
             COUNT(*) FILTER (WHERE "CreatedAt" > NOW() - INTERVAL '24 hours'))::numeric(10,1) || ' days'
        ELSE 'N/A'
    END as days_to_1_crore
FROM "Embeddings";

-- 3. Index Health
\echo '\n--- INDEX HEALTH ---'
SELECT
    indexname,
    pg_size_pretty(pg_relation_size(indexrelid)) as size,
    idx_scan as times_used,
    idx_tup_read as tuples_read,
    CASE
        WHEN idx_scan = 0 THEN '⚠️ UNUSED'
        WHEN idx_scan < 10 THEN 'Low usage'
        ELSE '✓ Active'
    END as status
FROM pg_stat_user_indexes
WHERE schemaname = 'public' AND tablename = 'Embeddings'
ORDER BY pg_relation_size(indexrelid) DESC;

-- 4. Vector Index Specific
\echo '\n--- VECTOR INDEX STATUS ---'
SELECT
    indexname,
    pg_size_pretty(pg_relation_size(indexrelid)) as size,
    idx_scan as scans,
    idx_tup_read as vectors_read,
    CASE
        WHEN (SELECT COUNT(*) FROM "Embeddings") < 1000000 THEN '✓ OK - lists=100'
        WHEN (SELECT COUNT(*) FROM "Embeddings") < 10000000 THEN '⚠️ REBUILD with lists=1000-2000'
        WHEN (SELECT COUNT(*) FROM "Embeddings") < 50000000 THEN '⚠️ REBUILD with lists=2000-3000'
        ELSE '⚠️ REBUILD with lists=3000-5000'
    END as recommendation
FROM pg_stat_user_indexes
WHERE indexname = 'idx_embeddings_vector_ivfflat';

-- 5. Table Bloat Check
\echo '\n--- TABLE HEALTH ---'
SELECT
    n_live_tup as live_tuples,
    n_dead_tup as dead_tuples,
    CASE
        WHEN n_live_tup > 0 THEN (n_dead_tup::float / n_live_tup * 100)::numeric(5,2)
        ELSE 0
    END as dead_pct,
    CASE
        WHEN n_live_tup > 0 AND (n_dead_tup::float / n_live_tup * 100) > 10 THEN '⚠️ VACUUM NEEDED'
        ELSE '✓ Healthy'
    END as status,
    last_vacuum,
    last_autovacuum,
    last_analyze
FROM pg_stat_user_tables
WHERE relname = 'Embeddings';

-- ================================================
-- PERFORMANCE TEST (Run when needed)
-- ================================================

\echo '\n================================================'
\echo 'QUERY PERFORMANCE TEST'
\echo '================================================'

-- Test vector search speed
\echo '\n--- Testing Vector Search Speed ---'
EXPLAIN (ANALYZE, BUFFERS, TIMING OFF, SUMMARY)
SELECT
    "EmbeddingId",
    "RecordTitle",
    1 - ("Vector" <=> (SELECT "Vector" FROM "Embeddings" ORDER BY RANDOM() LIMIT 1)) as similarity
FROM "Embeddings"
ORDER BY "Vector" <=> (SELECT "Vector" FROM "Embeddings" ORDER BY RANDOM() LIMIT 1)
LIMIT 20;

\echo '\n✓ Check output above for:'
\echo '  - Should show: Index Scan using idx_embeddings_vector_ivfflat'
\echo '  - Execution Time should be < 100ms'

-- ================================================
-- WEEKLY REPORT
-- ================================================

\echo '\n================================================'
\echo 'WEEKLY PERFORMANCE REPORT'
\echo '================================================'

-- Projection to 1 Crore
\echo '\n--- PROJECTION TO 1 CRORE ---'
WITH stats AS (
    SELECT
        COUNT(*) as current,
        COUNT(*) FILTER (WHERE "CreatedAt" > NOW() - INTERVAL '7 days') as week
    FROM "Embeddings"
)
SELECT
    current as current_chunks,
    (current / 10000000.0)::numeric(6,3) as current_crores,
    week as chunks_last_week,
    (week / 7.0)::numeric(10,1) as avg_chunks_per_day,
    CASE
        WHEN week > 0 THEN ((86300000 - current)::float / (week / 7.0))::numeric(10,1)
        ELSE NULL
    END as days_to_1_crore
FROM stats;

-- Storage breakdown
\echo '\n--- STORAGE BREAKDOWN ---'
SELECT
    'Table Data' as component,
    pg_size_pretty(pg_relation_size('"Embeddings"')) as size,
    (pg_relation_size('"Embeddings"')::float / pg_total_relation_size('"Embeddings"') * 100)::numeric(5,1) as pct
UNION ALL
SELECT
    'All Indexes',
    pg_size_pretty(SUM(pg_relation_size(indexrelid))),
    (SUM(pg_relation_size(indexrelid))::float / pg_total_relation_size('"Embeddings"') * 100)::numeric(5,1)
FROM pg_stat_user_indexes
WHERE tablename = 'Embeddings'
UNION ALL
SELECT
    'TOTAL',
    pg_size_pretty(pg_total_relation_size('"Embeddings"')),
    100.0;

-- Top 5 largest indexes
\echo '\n--- LARGEST INDEXES ---'
SELECT
    indexname,
    pg_size_pretty(pg_relation_size(indexrelid)) as size,
    idx_scan as scans
FROM pg_stat_user_indexes
WHERE tablename = 'Embeddings'
ORDER BY pg_relation_size(indexrelid) DESC
LIMIT 5;

-- ================================================
-- SCHEDULER PERFORMANCE (5-minute batches)
-- ================================================

\echo '\n================================================'
\echo 'SCHEDULER PERFORMANCE (Last 24 Hours)'
\echo '================================================'

SELECT
    DATE_TRUNC('hour', "CreatedAt") as hour,
    COUNT(*) as chunks_inserted,
    (COUNT(*) / 8.63)::int as approx_records,
    COUNT(*) / 12 as avg_per_5min_batch,
    CASE
        WHEN COUNT(*) / 12 > 10000 THEN '⚠️ High load'
        WHEN COUNT(*) / 12 < 5000 THEN '⚠️ Below target'
        ELSE '✓ On track'
    END as status
FROM "Embeddings"
WHERE "CreatedAt" > NOW() - INTERVAL '24 hours'
GROUP BY DATE_TRUNC('hour', "CreatedAt")
ORDER BY hour DESC
LIMIT 24;

-- ================================================
-- QUICK STATUS SUMMARY
-- ================================================

\echo '\n================================================'
\echo 'QUICK STATUS SUMMARY'
\echo '================================================'

SELECT
    'Total Records' as metric,
    (COUNT(*) / 8.63)::int::text as value
FROM "Embeddings"
UNION ALL
SELECT
    'Total Chunks',
    COUNT(*)::text
FROM "Embeddings"
UNION ALL
SELECT
    'Progress to 1 Crore',
    (COUNT(*) / 10000000.0 * 100)::numeric(5,2)::text || '%'
FROM "Embeddings"
UNION ALL
SELECT
    'Database Size',
    pg_size_pretty(pg_database_size('DocEmbeddings'))
UNION ALL
SELECT
    'Vector Index Size',
    pg_size_pretty(pg_relation_size('idx_embeddings_vector_ivfflat'))
UNION ALL
SELECT
    'Index Count',
    COUNT(*)::text
FROM pg_indexes
WHERE tablename = 'Embeddings';

\echo '\n================================================'
\echo '✓ Monitoring Complete!'
\echo '================================================'
