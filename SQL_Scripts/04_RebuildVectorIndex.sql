-- ================================================
-- VECTOR INDEX REBUILD PROCEDURE
-- Run this when data grows significantly
-- ================================================

-- This script helps you rebuild the vector index with
-- optimized parameters as your dataset grows

-- ================================================
-- STEP 1: CHECK CURRENT STATUS
-- ================================================

\echo '================================================'
\echo 'VECTOR INDEX REBUILD PROCEDURE'
\echo '================================================'

\echo '\n--- Current Database Status ---'
SELECT
    COUNT(*) as total_chunks,
    (COUNT(*) / 8.63)::int as approx_records,
    (COUNT(*) / 10000000.0)::numeric(6,3) as crores,
    pg_size_pretty(pg_relation_size('idx_embeddings_vector_ivfflat')) as current_index_size,
    CASE
        WHEN COUNT(*) < 1000000 THEN 'lists=100 (Current OK)'
        WHEN COUNT(*) < 10000000 THEN 'lists=1000-2000 (Rebuild recommended)'
        WHEN COUNT(*) < 50000000 THEN 'lists=2000-3000 (Rebuild recommended)'
        ELSE 'lists=3000-5000 (Rebuild recommended)'
    END as recommendation
FROM "Embeddings";

-- ================================================
-- STEP 2: CALCULATE OPTIMAL LISTS PARAMETER
-- ================================================

\echo '\n--- Calculating Optimal Lists Parameter ---'

DO $$
DECLARE
    record_count BIGINT;
    optimal_lists INT;
BEGIN
    SELECT COUNT(*) INTO record_count FROM "Embeddings";

    -- Calculate optimal lists parameter
    -- Formula: lists ≈ sqrt(record_count)
    -- Practical ranges:
    optimal_lists := CASE
        WHEN record_count < 1000000 THEN 100
        WHEN record_count < 5000000 THEN 1000
        WHEN record_count < 10000000 THEN 1500
        WHEN record_count < 25000000 THEN 2000
        WHEN record_count < 50000000 THEN 2500
        WHEN record_count < 100000000 THEN 3000
        ELSE 5000
    END;

    RAISE NOTICE 'Current record count: %', record_count;
    RAISE NOTICE 'Recommended lists parameter: %', optimal_lists;
    RAISE NOTICE '';
    RAISE NOTICE 'Estimated rebuild time:';

    IF record_count < 1000000 THEN
        RAISE NOTICE '  ⏱️  5-10 minutes';
    ELSIF record_count < 10000000 THEN
        RAISE NOTICE '  ⏱️  30-60 minutes';
    ELSIF record_count < 50000000 THEN
        RAISE NOTICE '  ⏱️  1-2 hours';
    ELSE
        RAISE NOTICE '  ⏱️  2-4 hours';
    END IF;

    RAISE NOTICE '';
    RAISE NOTICE '⚠️  The old index will be dropped and rebuilt.';
    RAISE NOTICE '⚠️  Queries will use sequential scan during rebuild (slower).';
    RAISE NOTICE '⚠️  Using CONCURRENTLY - no table lock, application stays running.';
END $$;

-- ================================================
-- STEP 3: CONFIRMATION PROMPT
-- ================================================

\echo '\n================================================'
\echo 'READY TO REBUILD?'
\echo '================================================'
\echo ''
\echo 'The following steps will be executed:'
\echo '1. Drop old vector index (CONCURRENTLY - no lock)'
\echo '2. Create new index with optimal lists parameter'
\echo '3. Verify new index is working'
\echo ''
\echo '⚠️  During rebuild, vector searches will be slower!'
\echo '⚠️  Recommended: Run during low-traffic period'
\echo ''
\echo 'To proceed, uncomment and run the REBUILD section below.'
\echo 'To cancel, stop here.'
\echo ''

-- ================================================
-- STEP 4: REBUILD (UNCOMMENT TO EXECUTE)
-- ================================================

/*
-- UNCOMMENT THIS ENTIRE BLOCK TO EXECUTE REBUILD

\echo '\n--- Starting Index Rebuild ---'
\echo 'Timestamp:' \n
SELECT NOW();

-- Drop old index
\echo '\nStep 1: Dropping old index...'
DROP INDEX CONCURRENTLY IF EXISTS idx_embeddings_vector_ivfflat;
\echo '✓ Old index dropped'

-- Calculate lists parameter based on current data
\echo '\nStep 2: Creating new index...'

-- Determine lists value
DO $$
DECLARE
    record_count BIGINT;
    lists_param INT;
    create_sql TEXT;
BEGIN
    SELECT COUNT(*) INTO record_count FROM "Embeddings";

    lists_param := CASE
        WHEN record_count < 1000000 THEN 100
        WHEN record_count < 5000000 THEN 1000
        WHEN record_count < 10000000 THEN 1500
        WHEN record_count < 25000000 THEN 2000
        WHEN record_count < 50000000 THEN 2500
        WHEN record_count < 100000000 THEN 3000
        ELSE 5000
    END;

    create_sql := format('
        CREATE INDEX CONCURRENTLY idx_embeddings_vector_ivfflat
        ON "Embeddings"
        USING ivfflat (("Vector"::halfvec(3072)) halfvec_cosine_ops)
        WITH (lists = %s)', lists_param);

    RAISE NOTICE 'Creating index with lists=%', lists_param;
    EXECUTE create_sql;
END $$;

\echo '✓ New index created'

-- Verify index
\echo '\nStep 3: Verifying new index...'
SELECT
    indexname,
    pg_size_pretty(pg_relation_size(indexrelid)) as new_size,
    idx_scan as scans
FROM pg_stat_user_indexes
WHERE indexname = 'idx_embeddings_vector_ivfflat';

-- Test query performance
\echo '\nStep 4: Testing query performance...'
EXPLAIN ANALYZE
SELECT "EmbeddingId"
FROM "Embeddings"
ORDER BY "Vector" <=> (SELECT "Vector" FROM "Embeddings" LIMIT 1)
LIMIT 20;

\echo '\n================================================'
\echo '✓ INDEX REBUILD COMPLETE!'
\echo '================================================'
\echo ''
\echo 'Verify the EXPLAIN output above shows:'
\echo '  → Index Scan using idx_embeddings_vector_ivfflat'
\echo '  → Execution time < 100ms'
\echo ''

SELECT NOW() as completed_at;

-- END OF REBUILD BLOCK - UNCOMMENT ABOVE TO EXECUTE
*/

-- ================================================
-- ALTERNATIVE: MANUAL REBUILD WITH SPECIFIC LISTS
-- ================================================

-- If you want to manually specify the lists parameter,
-- uncomment and modify the value below:

/*
-- MANUAL REBUILD WITH CUSTOM LISTS VALUE

-- Step 1: Drop old index
DROP INDEX CONCURRENTLY IF EXISTS idx_embeddings_vector_ivfflat;

-- Step 2: Create new index with YOUR chosen lists value
CREATE INDEX CONCURRENTLY idx_embeddings_vector_ivfflat
ON "Embeddings"
USING ivfflat (("Vector"::halfvec(3072)) halfvec_cosine_ops)
WITH (lists = 2000);  -- ← CHANGE THIS VALUE

-- Step 3: Verify
SELECT
    indexname,
    pg_size_pretty(pg_relation_size(indexrelid)) as size
FROM pg_stat_user_indexes
WHERE indexname = 'idx_embeddings_vector_ivfflat';
*/

-- ================================================
-- REBUILD SCHEDULE REFERENCE
-- ================================================

\echo '\n================================================'
\echo 'RECOMMENDED REBUILD SCHEDULE'
\echo '================================================'
\echo ''
\echo 'Rebuild when you reach these milestones:'
\echo ''
\echo '  1M records (Week 1)   → lists = 1000'
\echo '  10M records (Week 2)  → lists = 1500-2000'
\echo '  50M records (Week 4)  → lists = 2500-3000'
\echo '  100M records          → lists = 5000'
\echo ''
\echo 'Signs you need to rebuild:'
\echo '  • Query times increasing (> 200ms)'
\echo '  • Data size doubled since last rebuild'
\echo '  • EXPLAIN shows high "buffers" usage'
\echo ''

-- ================================================
-- POST-REBUILD OPTIMIZATION
-- ================================================

\echo '================================================'
\echo 'POST-REBUILD CHECKLIST'
\echo '================================================'
\echo ''
\echo 'After rebuilding, run these commands:'
\echo ''
\echo '1. Update statistics:'
\echo '   ANALYZE "Embeddings";'
\echo ''
\echo '2. Test query performance (check for < 100ms):'
\echo '   See monitoring queries in 03_MonitoringQueries.sql'
\echo ''
\echo '3. Tune ivfflat.probes if needed:'
\echo '   SET ivfflat.probes = 10;  -- Default'
\echo '   SET ivfflat.probes = 20;  -- Better recall, slightly slower'
\echo ''
\echo '4. Monitor for 24 hours to ensure stable performance'
\echo ''
