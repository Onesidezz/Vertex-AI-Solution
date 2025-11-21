-- ================================================
-- PERFORMANCE CONFIGURATION
-- PostgreSQL optimization for vector search workload
-- ================================================

-- ================================================
-- DATABASE-LEVEL SETTINGS
-- ================================================

-- IVFFlat query optimization
ALTER DATABASE "DocEmbeddings" SET ivfflat.probes = 10;

-- Full-text search configuration
ALTER DATABASE "DocEmbeddings" SET default_text_search_config = 'pg_catalog.english';

-- ================================================
-- TABLE-LEVEL OPTIMIZATIONS
-- ================================================

-- Autovacuum settings for high-throughput inserts
ALTER TABLE "Embeddings" SET (
    autovacuum_vacuum_scale_factor = 0.05,     -- Vacuum at 5% dead tuples
    autovacuum_analyze_scale_factor = 0.02,    -- Analyze at 2% changes
    autovacuum_vacuum_cost_delay = 10,         -- Faster vacuuming
    fillfactor = 90                             -- Leave 10% free for updates
);

-- Statistics target for better query planning
ALTER TABLE "Embeddings" ALTER COLUMN "Vector" SET STATISTICS 1000;
ALTER TABLE "Embeddings" ALTER COLUMN "RecordUri" SET STATISTICS 1000;
ALTER TABLE "Embeddings" ALTER COLUMN "DateCreated" SET STATISTICS 1000;
ALTER TABLE "Embeddings" ALTER COLUMN "FileType" SET STATISTICS 500;

-- Enable compression
ALTER TABLE "Embeddings" SET (toast_tuple_target = 8160);

-- ================================================
-- VERIFY CONFIGURATION
-- ================================================

-- Show table settings
SELECT
    'Table Settings' as config,
    reloptions
FROM pg_class
WHERE relname = 'Embeddings';

-- Show database settings
SELECT
    'Database Settings' as config,
    name,
    setting
FROM pg_settings
WHERE name IN ('ivfflat.probes', 'default_text_search_config')
   OR name LIKE 'autovacuum%'
ORDER BY name;

SELECT '✓ Performance configuration applied successfully!' as status;
