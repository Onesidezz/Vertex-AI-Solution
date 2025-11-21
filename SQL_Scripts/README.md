# SQL Scripts for Production Database Setup

## Quick Start Guide

This folder contains all SQL scripts needed for production-ready database setup with optimized indexing for crore-scale vector search.

---

## 📁 File Structure

| File | Purpose | Run Time | When to Run |
|------|---------|----------|-------------|
| `01_CreateAllIndexes.sql` | Creates all production indexes | 1-5 min | **Once** - Before starting scheduler |
| `02_PerformanceConfig.sql` | Configures PostgreSQL settings | <1 min | **Once** - After index creation |
| `03_MonitoringQueries.sql` | Daily/weekly monitoring queries | 1-2 min | **Daily/Weekly** - Ongoing |
| `04_RebuildVectorIndex.sql` | Rebuilds vector index as data grows | 10min-4hrs | **As needed** - At growth milestones |
| `AddFullTextSearchToEmbeddings.sql` | Sets up FTS triggers | 2-5 min | **Once** - After index creation |
| `CheckDatabaseSize.sql` | Quick database statistics | <1 min | **Anytime** - Ad-hoc checks |

---

## 🚀 Initial Setup (Run Once)

### Prerequisites

1. PostgreSQL 14+ with pgvector 0.8.1+
2. Database `DocEmbeddings` created
3. Embeddings table exists (from EF Core migrations)

### Step-by-Step Setup

```bash
# 1. Connect to database
psql -h localhost -U postgres -d DocEmbeddings

# 2. Create all indexes (1-5 minutes)
\i 01_CreateAllIndexes.sql

# 3. Apply performance configuration (<1 minute)
\i 02_PerformanceConfig.sql

# 4. Set up full-text search (2-5 minutes)
\i AddFullTextSearchToEmbeddings.sql

# 5. Verify setup
\d+ "Embeddings"
SELECT COUNT(*) FROM pg_indexes WHERE tablename = 'Embeddings';
# Should show 11+ indexes
```

**Expected Output:**
```
✓ Vector index created (idx_embeddings_vector_ivfflat)
✓ Full-text search index created (idx_embeddings_search_vector)
✓ 11 total indexes
✓ Performance configuration applied
```

---

## 📊 Daily Operations

### Daily Health Check (Run Every Morning)

```bash
psql -h localhost -U postgres -d DocEmbeddings -f 03_MonitoringQueries.sql
```

**What to check:**
- ✅ Growth rate (~103,000 chunks/hour expected)
- ✅ Index usage (all indexes should show "Active")
- ✅ Table bloat (should be < 10% dead tuples)
- ✅ Days to 1 crore (tracking progress)

### Quick Status Check

```sql
-- Quick one-liner
SELECT
    (COUNT(*) / 8.63)::int as records,
    (COUNT(*) / 10000000.0 * 100)::numeric(5,2) || '%' as progress_to_1cr,
    pg_size_pretty(pg_database_size('DocEmbeddings')) as db_size
FROM "Embeddings";
```

---

## 🔧 Maintenance Schedule

### Weekly (Every Sunday 2 AM)

```sql
-- Update statistics
ANALYZE "Embeddings";

-- Check if vector index needs rebuilding
SELECT
    COUNT(*) as chunks,
    CASE
        WHEN COUNT(*) >= 50000000 THEN '⚠️ REBUILD with lists=3000'
        WHEN COUNT(*) >= 10000000 THEN '⚠️ REBUILD with lists=2000'
        WHEN COUNT(*) >= 1000000 THEN '⚠️ REBUILD with lists=1000'
        ELSE '✅ OK'
    END as vector_index_status
FROM "Embeddings";
```

### Monthly (First Sunday)

```bash
# Full monitoring report
psql -h localhost -U postgres -d DocEmbeddings -f 03_MonitoringQueries.sql > monthly_report.txt

# Review and archive the report
```

---

## 🔄 Vector Index Rebuild (As Data Grows)

### When to Rebuild

Rebuild the vector index when you reach these milestones:

| Current Size | Lists Parameter | Rebuild Command |
|--------------|----------------|-----------------|
| 1M chunks (Week 1) | 1000 | See below |
| 10M chunks (Week 2) | 2000 | See below |
| 50M chunks (Week 4) | 3000 | See below |
| 100M+ chunks | 5000 | See below |

### How to Rebuild

```bash
# 1. Run the rebuild script
psql -h localhost -U postgres -d DocEmbeddings -f 04_RebuildVectorIndex.sql

# 2. Review the recommendations
# 3. Uncomment the REBUILD section in the script
# 4. Re-run the script

# OR use this quick method:

psql -h localhost -U postgres -d DocEmbeddings
```

```sql
-- Drop and recreate with new lists parameter
DROP INDEX CONCURRENTLY idx_embeddings_vector_ivfflat;

CREATE INDEX CONCURRENTLY idx_embeddings_vector_ivfflat
ON "Embeddings"
USING ivfflat (("Vector"::halfvec(3072)) halfvec_cosine_ops)
WITH (lists = 2000);  -- Adjust based on data size

-- Verify
SELECT indexname, pg_size_pretty(pg_relation_size(indexrelid))
FROM pg_stat_user_indexes
WHERE indexname = 'idx_embeddings_vector_ivfflat';
```

**Expected rebuild times:**
- 1M records: 10-20 minutes
- 10M records: 45-90 minutes
- 50M records: 1-2 hours
- 100M records: 2-4 hours

---

## 🎯 Performance Targets

### Query Performance Goals

| Dataset Size | Target Time | With Index | Status |
|--------------|-------------|------------|--------|
| 2,346 (current) | <50ms | 20ms | ✅ Fast |
| 1M chunks | <100ms | 50ms | ✅ Good |
| 10M chunks | <150ms | 70ms | ✅ Good |
| 86.3M chunks (1 crore) | <200ms | 90ms | ✅ Excellent |

### Test Query Performance

```sql
-- Run this to test current performance
EXPLAIN (ANALYZE, BUFFERS)
SELECT
    "EmbeddingId",
    1 - ("Vector" <=> (SELECT "Vector" FROM "Embeddings" LIMIT 1)) as similarity
FROM "Embeddings"
ORDER BY "Vector" <=> (SELECT "Vector" FROM "Embeddings" LIMIT 1)
LIMIT 20;
```

**What to look for:**
- ✅ "Index Scan using idx_embeddings_vector_ivfflat"
- ✅ Execution Time < 100ms
- ✅ Buffers: shared hit (good) vs read (bad)

---

## 🔍 Troubleshooting

### Problem: Index Not Being Used

**Symptoms:**
- EXPLAIN shows "Seq Scan" instead of "Index Scan"
- Queries taking > 1 second

**Solution:**
```sql
-- Force index usage for testing
SET enable_seqscan = off;

-- Increase probes
SET ivfflat.probes = 20;

-- Update statistics
ANALYZE "Embeddings";

-- Check index exists
\d+ "Embeddings"
```

### Problem: Slow Inserts

**Symptoms:**
- Scheduler batches taking > 5 minutes
- INSERT timeouts

**Solution:**
```sql
-- Reduce scheduler parallelism
-- In appsettings.json: "MaxParallelTasks": 4 (reduce from 6)

-- Or temporarily disable vector index during bulk load
DROP INDEX CONCURRENTLY idx_embeddings_vector_ivfflat;
-- Load data
-- Recreate index
```

### Problem: Out of Memory

**Symptoms:**
- Index creation fails with OOM
- Server becomes unresponsive

**Solution:**
```sql
-- Increase maintenance memory
SET maintenance_work_mem = '4GB';

-- Use smaller lists parameter
CREATE INDEX ... WITH (lists = 50);  -- Instead of 100
```

---

## 📈 Growth Projections

### Timeline to 1 Crore (with 5-minute scheduler)

```
Week 0 (Now):      272 records → 2,346 chunks
Week 1:          115,000 records → 1M chunks      [REBUILD: lists=1000]
Week 2:        1,160,000 records → 10M chunks     [REBUILD: lists=2000]
Week 3:        2,320,000 records → 20M chunks
Week 4:        5,800,000 records → 50M chunks     [REBUILD: lists=3000]
Week 5:       10,000,000 records → 86.3M chunks   [PRODUCTION READY!]
```

### Storage Projections

| Milestone | Table Size | Index Size | Total Size |
|-----------|------------|------------|------------|
| Current | 62 MB | 50 MB | 112 MB |
| 1M chunks | 6 GB | 3 GB | 9 GB |
| 10M chunks | 60 GB | 30 GB | 90 GB |
| 50M chunks | 300 GB | 180 GB | 480 GB |
| 86.3M (1 crore) | 530 GB | 600 GB | **1.1 TB** |

---

## 🎓 Best Practices

### Do's ✅

1. **Run monitoring queries daily** - Catch issues early
2. **Rebuild vector index at milestones** - Maintain performance
3. **Use CONCURRENTLY for all index operations** - No downtime
4. **Monitor table bloat** - Run VACUUM if needed
5. **Test query performance after changes** - Verify improvements

### Don'ts ❌

1. **Don't skip index rebuilds** - Performance will degrade
2. **Don't run VACUUM FULL without downtime window** - Locks table
3. **Don't create indexes without CONCURRENTLY** - Causes downtime
4. **Don't ignore monitoring warnings** - Small issues become big
5. **Don't modify ivfflat.probes without testing** - Can hurt performance

---

## 📞 Support

For issues or questions:

1. Check troubleshooting section above
2. Review PRODUCTION_DATABASE_SETUP.md
3. Check PostgreSQL logs: `/var/log/postgresql/`
4. Monitor system resources (CPU, RAM, disk)

---

## 📝 Changelog

### Version 1.0 (2025-01-21)
- Initial production setup
- All indexes created
- Monitoring queries established
- Documentation complete

---

**Last Updated:** 2025-01-21
**Database:** PostgreSQL 16 + pgvector 0.8.1
**Target Scale:** 1 Crore records (86.3M chunks)
