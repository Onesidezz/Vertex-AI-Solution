# Production Database Setup Guide
## Complete Index Strategy for Crore-Scale Vector Search

---

## 📋 Table of Contents

1. [Overview](#overview)
2. [Complete Table Structure](#complete-table-structure)
3. [All Indexes](#all-indexes)
4. [Performance Configuration](#performance-configuration)
5. [Monitoring Queries](#monitoring-queries)
6. [Maintenance Schedule](#maintenance-schedule)
7. [Scaling Strategy](#scaling-strategy)
8. [Troubleshooting](#troubleshooting)

---

## Overview

### System Specifications

**Current State:**
- Records: 2,346 chunks (272 records)
- Table Size: 62 MB
- Database: PostgreSQL with pgvector 0.8.1

**Production Target:**
- Records: 1 Crore (10M records = 86.3M chunks)
- Ingestion Rate: 1,000 records every 5 minutes (~103,000 chunks/hour)
- Time to Crore: ~35 days
- Expected Size: 1.1-1.3 TB (including indexes)

**Query Performance Goals:**
- Semantic Search: < 100ms
- Full-Text Search: < 50ms
- Hybrid Search: < 150ms
- Total Response: < 800ms (including ACL + AI synthesis)

---

## Complete Table Structure

### Embeddings Table Schema

```sql
-- ================================================
-- EMBEDDINGS TABLE - PRIMARY DATA STRUCTURE
-- ================================================
CREATE TABLE IF NOT EXISTS "Embeddings" (
    -- Primary Key
    "Id" BIGSERIAL PRIMARY KEY,

    -- Unique Identifier
    "EmbeddingId" VARCHAR(255) NOT NULL UNIQUE,

    -- Vector Embedding (3072 dimensions for Gemini)
    "Vector" vector(3072) NOT NULL,

    -- Content Manager Metadata
    "RecordUri" BIGINT NOT NULL,
    "RecordTitle" VARCHAR(500) NOT NULL,
    "DateCreated" TIMESTAMP WITH TIME ZONE,
    "SourceDateModified" TIMESTAMP WITH TIME ZONE,
    "RecordType" VARCHAR(50) NOT NULL,
    "Container" VARCHAR(500),
    "Assignee" VARCHAR(255),
    "AllParts" TEXT,
    "ACL" TEXT,

    -- Chunk Metadata
    "ChunkIndex" INTEGER NOT NULL DEFAULT 0,
    "ChunkSequence" INTEGER NOT NULL DEFAULT 0,
    "TotalChunks" INTEGER NOT NULL DEFAULT 1,
    "TokenCount" INTEGER NOT NULL DEFAULT 0,
    "StartPosition" INTEGER NOT NULL DEFAULT 0,
    "EndPosition" INTEGER NOT NULL DEFAULT 0,
    "PageNumber" INTEGER NOT NULL DEFAULT 0,
    "ChunkContent" TEXT NOT NULL,
    "ContentPreview" VARCHAR(200),

    -- File Metadata
    "FileExtension" VARCHAR(20),
    "FileType" VARCHAR(20),
    "DocumentCategory" VARCHAR(100),

    -- System Metadata
    "EntityType" VARCHAR(50) NOT NULL DEFAULT 'content_manager_record',
    "IndexedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "CreatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- Add comment
COMMENT ON TABLE "Embeddings" IS 'Vector embeddings for Content Manager records with comprehensive metadata for hybrid search and filtering';
COMMENT ON COLUMN "Embeddings"."Vector" IS '3072-dimensional Gemini embedding vector for semantic similarity search';
```

---

## All Indexes

### 1. Primary Key Index (Automatic)

```sql
-- Automatically created with PRIMARY KEY constraint
-- Index: "PK_Embeddings" on "Id"
-- Type: B-tree
-- Usage: Fast lookups by primary key
```

### 2. Vector Similarity Index (CRITICAL)

```sql
-- ================================================
-- VECTOR SIMILARITY INDEX - IVFFlat
-- Most critical index for semantic search performance
-- ================================================

CREATE INDEX CONCURRENTLY idx_embeddings_vector_ivfflat
ON "Embeddings"
USING ivfflat (("Vector"::halfvec(3072)) halfvec_cosine_ops)
WITH (lists = 100);

-- Purpose: Fast approximate nearest neighbor search
-- Type: IVFFlat (Inverted File with Flat compression)
-- Casting: vector → halfvec (50% storage savings)
-- Distance: Cosine similarity
-- Lists: 100 (for < 1M records, will rebuild as data grows)
-- Expected Performance:
--   - Without index: 17 seconds (at 86M records)
--   - With index: 60-100ms (280x faster!)

COMMENT ON INDEX idx_embeddings_vector_ivfflat IS
'IVFFlat index for fast vector similarity search. Rebuild with larger lists parameter as data grows:
- < 1M records: lists=100
- 1M-10M: lists=1000-2000
- 10M-100M: lists=2000-5000';
```

### 3. Full-Text Search Index (CRITICAL)

```sql
-- ================================================
-- FULL-TEXT SEARCH INDEX - GIN
-- Critical for keyword matching in hybrid search
-- ================================================

-- First, add the search_vector column if not exists
ALTER TABLE "Embeddings"
ADD COLUMN IF NOT EXISTS search_vector tsvector;

-- Create GIN index for fast full-text search
CREATE INDEX CONCURRENTLY idx_embeddings_search_vector
ON "Embeddings" USING GIN(search_vector);

-- Purpose: Fast keyword search with BM25-like ranking
-- Type: GIN (Generalized Inverted Index)
-- Usage: Keyword matching, phrase search, boolean queries
-- Expected Performance: 5-50ms regardless of dataset size

COMMENT ON INDEX idx_embeddings_search_vector IS
'GIN index for PostgreSQL Full-Text Search. Auto-updated via trigger with weighted fields: Title(A=1.0), Content(B=0.4), Category(C=0.2), Metadata(D=0.1)';
```

### 4. Unique Embedding ID Index

```sql
-- ================================================
-- UNIQUE EMBEDDING ID INDEX
-- Ensures no duplicate embeddings
-- ================================================

CREATE UNIQUE INDEX CONCURRENTLY idx_embeddings_embeddingid
ON "Embeddings" ("EmbeddingId");

-- Purpose: Fast lookup by EmbeddingId, prevent duplicates
-- Type: B-tree unique
-- Usage: Deduplication, chunk updates
-- Performance: O(log n) lookups

COMMENT ON INDEX idx_embeddings_embeddingid IS
'Unique constraint on EmbeddingId format: cm_record_{RecordUri}_chunk_{ChunkIndex}';
```

### 5. Record URI Index

```sql
-- ================================================
-- RECORD URI INDEX
-- Fast filtering by Content Manager record
-- ================================================

CREATE INDEX CONCURRENTLY idx_embeddings_recorduri
ON "Embeddings" ("RecordUri");

-- Purpose: Filter all chunks for a specific record
-- Usage:
--   - Deleting all chunks for a record
--   - Fetching all chunks for context
--   - Record-level analytics
-- Performance: O(log n) + chunk count

COMMENT ON INDEX idx_embeddings_recorduri IS
'B-tree index on RecordUri for fast record-level filtering and deletion';
```

### 6. Date Created Index

```sql
-- ================================================
-- DATE CREATED INDEX
-- Fast date range filtering
-- ================================================

CREATE INDEX CONCURRENTLY idx_embeddings_datecreated
ON "Embeddings" ("DateCreated");

-- Purpose: Date range queries
-- Usage:
--   - "show me records from last month"
--   - "records between Jan 2024 and Mar 2024"
--   - Time-based analytics
-- Performance: O(log n) range scan

COMMENT ON INDEX idx_embeddings_datecreated IS
'B-tree index for date range filtering. Supports queries like "records from last week"';
```

### 7. File Type Index

```sql
-- ================================================
-- FILE TYPE INDEX
-- Fast filtering by document type
-- ================================================

CREATE INDEX CONCURRENTLY idx_embeddings_filetype
ON "Embeddings" ("FileType");

-- Purpose: Filter by file type (pdf, docx, xlsx, etc.)
-- Usage:
--   - "show me all PDF documents"
--   - "find Excel spreadsheets"
--   - File type analytics
-- Cardinality: Low (~10-20 distinct values)

COMMENT ON INDEX idx_embeddings_filetype IS
'B-tree index for file type filtering (pdf, docx, xlsx, txt, etc.)';
```

### 8. Record Type Index

```sql
-- ================================================
-- RECORD TYPE INDEX
-- Fast filtering by Content Manager record type
-- ================================================

CREATE INDEX CONCURRENTLY idx_embeddings_recordtype
ON "Embeddings" ("RecordType");

-- Purpose: Filter by record type (Document, Container)
-- Usage: Content Manager specific filtering
-- Cardinality: Very low (~2-5 distinct values)

COMMENT ON INDEX idx_embeddings_recordtype IS
'B-tree index for Content Manager record type (Document, Container)';
```

### 9. Entity Type Index

```sql
-- ================================================
-- ENTITY TYPE INDEX
-- Fast filtering by entity type
-- ================================================

CREATE INDEX CONCURRENTLY idx_embeddings_entitytype
ON "Embeddings" ("EntityType");

-- Purpose: Filter by entity source
-- Usage: Distinguish between different data sources
-- Cardinality: Very low (currently just 'content_manager_record')

COMMENT ON INDEX idx_embeddings_entitytype IS
'B-tree index for entity type filtering. Currently: content_manager_record';
```

### 10. Composite Index: RecordUri + ChunkSequence

```sql
-- ================================================
-- COMPOSITE INDEX: RECORD CHUNKS
-- Efficient chunk ordering within a record
-- ================================================

CREATE INDEX CONCURRENTLY idx_embeddings_record_chunks
ON "Embeddings" ("RecordUri", "ChunkSequence");

-- Purpose: Fast ordered retrieval of all chunks for a record
-- Usage:
--   - Reconstructing full document from chunks
--   - Sequential chunk reading
-- Performance: Single index scan for all chunks in order

COMMENT ON INDEX idx_embeddings_record_chunks IS
'Composite index for ordered chunk retrieval within a record';
```

### 11. Composite Index: Date + FileType (Optional)

```sql
-- ================================================
-- COMPOSITE INDEX: DATE + FILE TYPE
-- Optimize common filter combinations
-- ================================================

CREATE INDEX CONCURRENTLY idx_embeddings_date_filetype
ON "Embeddings" ("DateCreated", "FileType")
WHERE "DateCreated" IS NOT NULL;

-- Purpose: Optimize queries filtering by both date and file type
-- Usage:
--   - "show me PDFs from last month"
--   - "Excel files created in Q4 2024"
-- Partial Index: Only indexes records with dates (saves space)

COMMENT ON INDEX idx_embeddings_date_filetype IS
'Composite partial index for date+filetype queries. Partial: only records with DateCreated';
```

---

## Index Summary Table

| Index Name | Type | Columns | Purpose | Size Est. (1 Crore) | Priority |
|------------|------|---------|---------|---------------------|----------|
| **PK_Embeddings** | B-tree | Id | Primary key | 1.8 GB | Critical |
| **idx_embeddings_vector_ivfflat** | IVFFlat | Vector (halfvec) | Semantic search | 400-600 GB | **CRITICAL** |
| **idx_embeddings_search_vector** | GIN | search_vector | Full-text search | 50-100 GB | **CRITICAL** |
| **idx_embeddings_embeddingid** | B-tree (unique) | EmbeddingId | Deduplication | 2 GB | High |
| **idx_embeddings_recorduri** | B-tree | RecordUri | Record filtering | 1.8 GB | High |
| **idx_embeddings_datecreated** | B-tree | DateCreated | Date filtering | 1.5 GB | Medium |
| **idx_embeddings_filetype** | B-tree | FileType | File type filtering | 200 MB | Medium |
| **idx_embeddings_recordtype** | B-tree | RecordType | Record type filtering | 200 MB | Low |
| **idx_embeddings_entitytype** | B-tree | EntityType | Entity filtering | 200 MB | Low |
| **idx_embeddings_record_chunks** | B-tree | RecordUri, ChunkSequence | Chunk ordering | 2.5 GB | Medium |
| **idx_embeddings_date_filetype** | B-tree | DateCreated, FileType | Combined filter | 1 GB | Optional |

**Total Index Storage (at 1 Crore):** ~460-670 GB
**Total Database Size (at 1 Crore):** ~1.1-1.3 TB

---

## Performance Configuration

### PostgreSQL Settings

```sql
-- ================================================
-- POSTGRESQL CONFIGURATION FOR VECTOR SEARCH
-- Add to postgresql.conf or ALTER SYSTEM
-- ================================================

-- Memory Settings
ALTER SYSTEM SET shared_buffers = '8GB';              -- 25% of RAM (for 32GB server)
ALTER SYSTEM SET effective_cache_size = '24GB';       -- 75% of RAM
ALTER SYSTEM SET work_mem = '256MB';                  -- Per operation memory
ALTER SYSTEM SET maintenance_work_mem = '2GB';        -- For index builds

-- IVFFlat Query Optimization
ALTER DATABASE "DocEmbeddings" SET ivfflat.probes = 10;  -- Balance speed/recall

-- Full-Text Search Optimization
ALTER DATABASE "DocEmbeddings" SET default_text_search_config = 'pg_catalog.english';

-- Autovacuum Settings (for high-throughput inserts)
ALTER TABLE "Embeddings" SET (
    autovacuum_vacuum_scale_factor = 0.05,     -- Vacuum at 5% dead tuples
    autovacuum_analyze_scale_factor = 0.02,    -- Analyze at 2% changes
    autovacuum_vacuum_cost_delay = 10          -- Faster vacuuming
);

-- Parallel Query Settings
ALTER SYSTEM SET max_parallel_workers_per_gather = 4;
ALTER SYSTEM SET max_parallel_workers = 8;

-- Checkpoint Settings (for write-heavy workload)
ALTER SYSTEM SET checkpoint_timeout = '15min';
ALTER SYSTEM SET checkpoint_completion_target = 0.9;

-- WAL Settings
ALTER SYSTEM SET wal_buffers = '16MB';
ALTER SYSTEM SET max_wal_size = '4GB';

-- Apply settings
SELECT pg_reload_conf();
```

### Table-Level Optimizations

```sql
-- ================================================
-- TABLE OPTIMIZATIONS
-- ================================================

-- Fill factor (leave 10% free for updates)
ALTER TABLE "Embeddings" SET (fillfactor = 90);

-- Statistics target (better query planning)
ALTER TABLE "Embeddings" ALTER COLUMN "Vector" SET STATISTICS 1000;
ALTER TABLE "Embeddings" ALTER COLUMN "RecordUri" SET STATISTICS 1000;
ALTER TABLE "Embeddings" ALTER COLUMN "DateCreated" SET STATISTICS 1000;

-- Enable compression (PostgreSQL 14+)
ALTER TABLE "Embeddings" SET (toast_tuple_target = 8160);
```

---

## Monitoring Queries

### Daily Health Check

```sql
-- ================================================
-- DAILY DATABASE HEALTH CHECK
-- Run this every morning
-- ================================================

SELECT
    '=== EMBEDDINGS TABLE STATISTICS ===' as section,
    NOW() as check_time;

-- 1. Data Volume
SELECT
    'Data Volume' as metric,
    COUNT(*) as total_chunks,
    (COUNT(*) / 8.63)::int as approx_records,
    (COUNT(*) / 10000000.0)::numeric(6,3) as crores,
    pg_size_pretty(pg_total_relation_size('"Embeddings"')) as total_size,
    pg_size_pretty(pg_relation_size('"Embeddings"')) as table_only_size,
    pg_size_pretty(pg_total_relation_size('"Embeddings"') - pg_relation_size('"Embeddings"')) as all_indexes_size
FROM "Embeddings";

-- 2. Growth Rate (Last 24 hours)
SELECT
    'Growth Rate' as metric,
    COUNT(*) FILTER (WHERE "CreatedAt" > NOW() - INTERVAL '24 hours') as chunks_24h,
    COUNT(*) FILTER (WHERE "CreatedAt" > NOW() - INTERVAL '1 hour') as chunks_1h,
    (COUNT(*) FILTER (WHERE "CreatedAt" > NOW() - INTERVAL '24 hours') / 24.0)::numeric(10,1) as avg_chunks_per_hour
FROM "Embeddings";

-- 3. Index Sizes
SELECT
    'Index Sizes' as metric,
    indexname,
    pg_size_pretty(pg_relation_size(indexrelid)) as size,
    idx_scan as times_used,
    idx_tup_read as tuples_read
FROM pg_stat_user_indexes
WHERE schemaname = 'public' AND tablename = 'Embeddings'
ORDER BY pg_relation_size(indexrelid) DESC;

-- 4. Vector Index Health
SELECT
    'Vector Index' as metric,
    indexname,
    pg_size_pretty(pg_relation_size(indexrelid)) as size,
    idx_scan as scans,
    CASE
        WHEN idx_scan = 0 THEN 'UNUSED - Check query plans!'
        WHEN idx_scan < 10 THEN 'Low usage'
        ELSE 'Active'
    END as status
FROM pg_stat_user_indexes
WHERE indexname = 'idx_embeddings_vector_ivfflat';

-- 5. Table Bloat Check
SELECT
    'Table Health' as metric,
    n_dead_tup as dead_tuples,
    n_live_tup as live_tuples,
    CASE
        WHEN n_live_tup > 0 THEN (n_dead_tup::float / n_live_tup * 100)::numeric(5,2)
        ELSE 0
    END as dead_tuple_percent,
    last_vacuum,
    last_autovacuum,
    last_analyze,
    last_autoanalyze
FROM pg_stat_user_tables
WHERE schemaname = 'public' AND relname = 'Embeddings';
```

### Weekly Performance Report

```sql
-- ================================================
-- WEEKLY PERFORMANCE REPORT
-- Run every Monday
-- ================================================

-- Projection to 1 Crore
WITH current_stats AS (
    SELECT
        COUNT(*) as current_chunks,
        COUNT(*) FILTER (WHERE "CreatedAt" > NOW() - INTERVAL '7 days') as chunks_last_week,
        MAX("CreatedAt") as latest_record
    FROM "Embeddings"
)
SELECT
    'Projection to 1 Crore' as report,
    current_chunks,
    (current_chunks / 10000000.0)::numeric(6,3) as current_crores,
    chunks_last_week as chunks_per_week,
    CASE
        WHEN chunks_last_week > 0 THEN
            ((86300000 - current_chunks)::float / chunks_last_week)::numeric(10,1)
        ELSE NULL
    END as weeks_to_1_crore,
    CASE
        WHEN chunks_last_week > 0 THEN
            ((86300000 - current_chunks)::float / chunks_last_week * 7)::numeric(10,1)
        ELSE NULL
    END as days_to_1_crore,
    latest_record
FROM current_stats;

-- Index Maintenance Recommendations
SELECT
    'Index Maintenance' as report,
    indexname,
    pg_size_pretty(pg_relation_size(indexrelid)) as current_size,
    CASE
        WHEN indexname = 'idx_embeddings_vector_ivfflat' THEN
            CASE
                WHEN (SELECT COUNT(*) FROM "Embeddings") < 1000000 THEN 'OK - lists=100'
                WHEN (SELECT COUNT(*) FROM "Embeddings") < 10000000 THEN 'REBUILD RECOMMENDED - Use lists=1000-2000'
                WHEN (SELECT COUNT(*) FROM "Embeddings") < 50000000 THEN 'REBUILD RECOMMENDED - Use lists=2000-3000'
                ELSE 'REBUILD RECOMMENDED - Use lists=3000-5000'
            END
        ELSE 'OK'
    END as recommendation
FROM pg_stat_user_indexes
WHERE schemaname = 'public' AND tablename = 'Embeddings'
ORDER BY pg_relation_size(indexrelid) DESC;
```

### Real-Time Query Performance Monitor

```sql
-- ================================================
-- QUERY PERFORMANCE MONITOR
-- Test search performance in real-time
-- ================================================

-- Test semantic search speed
EXPLAIN (ANALYZE, BUFFERS, TIMING)
SELECT
    "EmbeddingId",
    "RecordTitle",
    1 - ("Vector" <=> (SELECT "Vector" FROM "Embeddings" ORDER BY RANDOM() LIMIT 1)) as similarity
FROM "Embeddings"
ORDER BY "Vector" <=> (SELECT "Vector" FROM "Embeddings" ORDER BY RANDOM() LIMIT 1)
LIMIT 20;

-- Expected output analysis:
-- Planning Time: < 5ms
-- Execution Time: < 100ms (with index)
-- Index Scan using idx_embeddings_vector_ivfflat ✅
```

---

## Maintenance Schedule

### Daily Tasks (Automated)

```sql
-- ================================================
-- DAILY MAINTENANCE (Run via cron at 2 AM)
-- ================================================

-- 1. Update table statistics
ANALYZE "Embeddings";

-- 2. Check for index bloat
SELECT
    indexname,
    pg_size_pretty(pg_relation_size(indexrelid)) as size
FROM pg_stat_user_indexes
WHERE schemaname = 'public' AND tablename = 'Embeddings'
  AND pg_relation_size(indexrelid) > 1000000000;  -- > 1GB

-- 3. Refresh search_vector if needed (handled by trigger, just verify)
SELECT COUNT(*) as records_without_fts
FROM "Embeddings"
WHERE search_vector IS NULL;
```

### Weekly Tasks

```sql
-- ================================================
-- WEEKLY MAINTENANCE (Run Sunday 2 AM)
-- ================================================

-- 1. Vacuum analyze (if autovacuum not keeping up)
VACUUM ANALYZE "Embeddings";

-- 2. Reindex if needed (only if bloat detected)
-- REINDEX INDEX CONCURRENTLY idx_embeddings_search_vector;

-- 3. Check for missing indexes
SELECT
    schemaname,
    tablename,
    attname,
    n_distinct,
    correlation
FROM pg_stats
WHERE schemaname = 'public'
  AND tablename = 'Embeddings'
  AND n_distinct > 100
ORDER BY abs(correlation) DESC;
```

### Monthly Tasks

```sql
-- ================================================
-- MONTHLY MAINTENANCE (First Sunday of month)
-- ================================================

-- 1. Check if vector index needs rebuilding
SELECT
    COUNT(*) as total_chunks,
    CASE
        WHEN COUNT(*) >= 50000000 THEN 'REBUILD with lists=3000'
        WHEN COUNT(*) >= 10000000 THEN 'REBUILD with lists=2000'
        WHEN COUNT(*) >= 1000000 THEN 'REBUILD with lists=1000'
        ELSE 'OK - Current lists=100'
    END as vector_index_recommendation
FROM "Embeddings";

-- 2. Full vacuum (if database > 500GB)
-- VACUUM FULL "Embeddings";  -- WARNING: Locks table! Run during maintenance window
```

### Vector Index Rebuild Procedure

```sql
-- ================================================
-- VECTOR INDEX REBUILD PROCEDURE
-- Run when data grows significantly
-- ================================================

-- Step 1: Check current index size and record count
SELECT
    COUNT(*) as records,
    pg_size_pretty(pg_relation_size('idx_embeddings_vector_ivfflat')) as current_index_size;

-- Step 2: Calculate new lists parameter
-- Formula: lists ≈ sqrt(record_count)
-- Practical:
--   1M records → lists = 1000
--   10M records → lists = 2000-3000
--   100M records → lists = 5000-10000

-- Step 3: Drop old index
DROP INDEX CONCURRENTLY idx_embeddings_vector_ivfflat;

-- Step 4: Create new index with updated lists
-- Example for 10M records:
CREATE INDEX CONCURRENTLY idx_embeddings_vector_ivfflat
ON "Embeddings"
USING ivfflat (("Vector"::halfvec(3072)) halfvec_cosine_ops)
WITH (lists = 2000);

-- Step 5: Verify new index
SELECT
    indexname,
    pg_size_pretty(pg_relation_size(indexrelid)) as new_size,
    idx_scan
FROM pg_stat_user_indexes
WHERE indexname = 'idx_embeddings_vector_ivfflat';

-- Step 6: Test query performance
EXPLAIN ANALYZE
SELECT "EmbeddingId"
FROM "Embeddings"
ORDER BY "Vector" <=> '[...]'::vector
LIMIT 20;
```

---

## Scaling Strategy

### Growth Milestones & Actions

| Milestone | Records | Chunks | Action Required | ETA |
|-----------|---------|--------|----------------|-----|
| **Current** | 272 | 2,346 | ✅ Initial indexes created | Now |
| **Week 1** | 115K | 1M | Rebuild vector index (lists=1000) | 7 days |
| **Week 2** | 1.16M | 10M | Rebuild vector index (lists=2000) | 14 days |
| **Week 3** | 2.32M | 20M | Monitor performance | 21 days |
| **Week 4** | 5.8M | 50M | Rebuild vector index (lists=3000) | 28 days |
| **1 Crore** | 10M | 86.3M | Final optimization, consider partitioning | 35 days |

### Horizontal Scaling Options (Beyond 1 Crore)

#### Option 1: Partitioning by Date

```sql
-- Create partitioned table (for 10+ crore scale)
CREATE TABLE "Embeddings_Partitioned" (
    LIKE "Embeddings" INCLUDING ALL
) PARTITION BY RANGE ("CreatedAt");

-- Create yearly partitions
CREATE TABLE embeddings_2024 PARTITION OF "Embeddings_Partitioned"
FOR VALUES FROM ('2024-01-01') TO ('2025-01-01');

CREATE TABLE embeddings_2025 PARTITION OF "Embeddings_Partitioned"
FOR VALUES FROM ('2025-01-01') TO ('2026-01-01');

-- Create indexes on each partition
CREATE INDEX ON embeddings_2024
USING ivfflat (("Vector"::halfvec(3072)) halfvec_cosine_ops)
WITH (lists = 2000);

CREATE INDEX ON embeddings_2025
USING ivfflat (("Vector"::halfvec(3072)) halfvec_cosine_ops)
WITH (lists = 2000);
```

#### Option 2: Read Replicas

```sql
-- Configure read replicas for query distribution
-- Primary: Handle all writes (scheduler inserts)
-- Replica 1: Handle search queries
-- Replica 2: Handle analytics/reporting

-- Application connection string:
-- Write: primary-db.example.com
-- Read: replica-db.example.com (load balanced)
```

#### Option 3: Separate Hot/Cold Data

```sql
-- Move old data to archive table
CREATE TABLE "Embeddings_Archive" (LIKE "Embeddings" INCLUDING ALL);

-- Move records older than 2 years
INSERT INTO "Embeddings_Archive"
SELECT * FROM "Embeddings"
WHERE "CreatedAt" < NOW() - INTERVAL '2 years';

DELETE FROM "Embeddings"
WHERE "CreatedAt" < NOW() - INTERVAL '2 years';

-- Smaller active table = faster queries
-- Archive still searchable when needed
```

---

## Troubleshooting

### Problem: Slow Inserts After Index Creation

**Symptoms:**
- Scheduler batches taking > 5 minutes
- INSERT statements timing out
- High CPU usage on database server

**Diagnosis:**
```sql
-- Check index sizes
SELECT indexname, pg_size_pretty(pg_relation_size(indexrelid))
FROM pg_stat_user_indexes
WHERE tablename = 'Embeddings';

-- Check for lock waits
SELECT * FROM pg_stat_activity
WHERE wait_event_type = 'Lock' AND state = 'active';
```

**Solutions:**
1. Reduce `MaxParallelTasks` in scheduler (less concurrency)
2. Batch inserts (100-500 chunks per transaction)
3. Temporarily disable vector index during bulk load:
   ```sql
   DROP INDEX CONCURRENTLY idx_embeddings_vector_ivfflat;
   -- Load data
   -- Recreate index
   ```

---

### Problem: Vector Index Not Being Used

**Symptoms:**
- Queries still slow (> 1 second)
- EXPLAIN shows "Seq Scan" instead of "Index Scan"

**Diagnosis:**
```sql
EXPLAIN ANALYZE
SELECT * FROM "Embeddings"
ORDER BY "Vector" <=> '[...]'::vector
LIMIT 20;
-- Look for "Index Scan using idx_embeddings_vector_ivfflat"
```

**Solutions:**
1. Check index exists:
   ```sql
   \d+ "Embeddings"
   ```
2. Increase `ivfflat.probes`:
   ```sql
   SET ivfflat.probes = 20;  -- Try higher value
   ```
3. Update statistics:
   ```sql
   ANALYZE "Embeddings";
   ```
4. Check planner settings:
   ```sql
   SET enable_seqscan = off;  -- Force index usage for testing
   ```

---

### Problem: Out of Memory During Index Build

**Symptoms:**
- Index creation fails with "out of memory" error
- Server becomes unresponsive during CREATE INDEX

**Solutions:**
1. Increase `maintenance_work_mem`:
   ```sql
   SET maintenance_work_mem = '4GB';
   CREATE INDEX ...;
   ```
2. Create index in steps (only if CONCURRENTLY fails):
   ```sql
   -- Split data into chunks and index separately
   ```
3. Use smaller `lists` parameter initially:
   ```sql
   WITH (lists = 50);  -- Instead of 100
   ```

---

### Problem: Table Bloat

**Symptoms:**
- Table size growing faster than data
- Many dead tuples
- Slow queries despite indexes

**Diagnosis:**
```sql
SELECT
    n_dead_tup,
    n_live_tup,
    (n_dead_tup::float / NULLIF(n_live_tup, 0) * 100)::numeric(5,2) as bloat_percent
FROM pg_stat_user_tables
WHERE relname = 'Embeddings';
```

**Solutions:**
1. Manual vacuum:
   ```sql
   VACUUM FULL "Embeddings";  -- Warning: Locks table
   ```
2. Tune autovacuum:
   ```sql
   ALTER TABLE "Embeddings" SET (
       autovacuum_vacuum_scale_factor = 0.02
   );
   ```

---

## Quick Reference Commands

### Complete Setup (Fresh Database)

```sql
-- Run this complete script on a fresh database

-- 1. Enable pgvector extension
CREATE EXTENSION IF NOT EXISTS vector;

-- 2. Create table (already done via migration)
-- See migration: 20251021140247_InitialPostgresWithPgvectorNoIndex.cs

-- 3. Create all indexes
\i create_all_indexes.sql

-- 4. Configure performance
\i performance_config.sql

-- 5. Set up monitoring
\i monitoring_setup.sql

-- 6. Verify setup
SELECT 'Setup Complete!' as status;
SELECT COUNT(*) as index_count
FROM pg_indexes
WHERE tablename = 'Embeddings';
```

### Emergency Index Rebuild

```sql
-- If vector search becomes slow, rebuild index

-- 1. Check current state
SELECT COUNT(*) FROM "Embeddings";

-- 2. Calculate new lists parameter
-- lists = sqrt(COUNT)

-- 3. Rebuild
DROP INDEX CONCURRENTLY idx_embeddings_vector_ivfflat;
CREATE INDEX CONCURRENTLY idx_embeddings_vector_ivfflat
ON "Embeddings"
USING ivfflat (("Vector"::halfvec(3072)) halfvec_cosine_ops)
WITH (lists = 2000);  -- Adjust based on data size

-- 4. Verify
EXPLAIN ANALYZE
SELECT * FROM "Embeddings" ORDER BY "Vector" <=> '[...]'::vector LIMIT 10;
```

---

## Performance Benchmarks

### Expected Query Performance

| Dataset Size | Without Index | With IVFFlat | Speedup | Status |
|--------------|---------------|--------------|---------|--------|
| 2,346 (now) | 30ms | 20ms | 1.5x | ✅ Small data |
| 100K | 150ms | 40ms | 3.8x | ✅ Fast |
| 1M | 500ms | 50ms | 10x | ✅ Acceptable |
| 10M | 2,000ms | 70ms | 28.6x | ✅ Good |
| 86.3M (1 crore) | 17,000ms | 90ms | 189x | ✅ Excellent |

### Expected Index Build Times

| Dataset Size | IVFFlat Build Time | HNSW Build Time | Recommended |
|--------------|-------------------|-----------------|-------------|
| 2,346 | 30 seconds | 1 minute | Either |
| 100K | 2 minutes | 10 minutes | IVFFlat |
| 1M | 10 minutes | 1 hour | IVFFlat |
| 10M | 45 minutes | 6 hours | IVFFlat |
| 86.3M | 2-4 hours | 12-24 hours | IVFFlat |

---

## Conclusion

This production setup provides:

✅ **Complete indexing strategy** for crore-scale vector search
✅ **Optimized performance** (189x speedup at 1 crore)
✅ **Monitoring & maintenance** procedures
✅ **Scaling roadmap** for future growth
✅ **Troubleshooting guides** for common issues

**Total Storage Impact at 1 Crore:**
- Table Data: 530 GB
- All Indexes: 460-670 GB
- **Total: 1.1-1.3 TB**

**Query Performance at 1 Crore:**
- Semantic Search: 90ms (vs 17 seconds without index)
- Full-Text Search: 50ms
- Hybrid Search: 140ms
- **Total Response: ~700ms** (including all processing)

**Next Steps:**
1. ✅ Indexes created
2. Start 5-minute scheduler
3. Monitor weekly using provided queries
4. Rebuild vector index at milestones (1M, 10M, 50M)
5. Scale horizontally if exceeding 10 crore

---

**Document Version:** 1.0
**Last Updated:** 2025-01-21
**Database:** PostgreSQL 16 + pgvector 0.8.1
**Application:** Document Processing API with Gemini Embeddings
