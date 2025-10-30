# DocumentProcessingAPI - Future Updates TODO

## High Priority: pgvector HNSW Indexing

### When pgvector 0.9.0+ is Released (Supporting >2000 Dimensions)

**Current Limitation:**
- pgvector 0.8.1 maximum HNSW/IVFFlat index dimension: **2000**
- Current embeddings: **3072 dimensions** (Gemini)
- Status: Using **sequential scan** for vector search (slow at scale)

**Action Items When pgvector 0.9.0+ Supports 3072+ Dimensions:**

#### 1. Verify pgvector Version Support
```bash
# Check if pgvector supports higher dimensions
psql -c "SELECT extversion FROM pg_extension WHERE extname = 'vector';"
# Check release notes for dimension limit increase
```

#### 2. Update Database Migration
**File:** `DocumentProcessingAPI.Infrastructure/Migrations/`

Create new migration:
```bash
dotnet ef migrations add AddHNSWIndexOnVector
```

**Migration Content:**
```sql
-- Add HNSW index on full 3072-dimension vector
CREATE INDEX IX_Embeddings_Vector_HNSW
ON "Embeddings"
USING hnsw ("Vector" vector_cosine_ops)
WITH (m = 16, ef_construction = 64);

-- Index parameters explanation:
-- m = 16              : Number of bi-directional links (default, good for most cases)
-- ef_construction = 64: Size of dynamic candidate list (higher = better accuracy, slower build)
```

#### 3. Update Code Documentation
**Files to Update:**
- `DocumentProcessingAPI.Infrastructure/Data/DocumentProcessingDbContext.cs` (lines 119-123)
- `DocumentProcessingAPI.Infrastructure/Services/PgVectorService.cs` (line 379 comment)

**Update comments from:**
```csharp
// pgvector v0.8.1 has a 2000-dimension limit for HNSW and IVFFlat indexes
```

**To:**
```csharp
// HNSW index enabled for 3072-dimension vectors with pgvector v0.9.0+
```

#### 4. Optimize Search Performance
**File:** `DocumentProcessingAPI.Infrastructure/Services/PgVectorService.cs`

**Current Method:** `SearchSimilarAsync` (lines 382-481)

**Add index tuning parameters:**
```csharp
// Before search queries, set HNSW search parameters
await _context.Database.ExecuteSqlRawAsync("SET hnsw.ef_search = 100");
```

**Parameters to tune:**
- `ef_search`: Higher = better recall, slower (default: 40, recommended: 100-200 for production)

#### 5. Performance Benchmarking
**Create test to measure improvement:**

```csharp
// Test file: DocumentProcessingAPI.Tests/Services/PgVectorServiceBenchmark.cs
// Compare:
// - Sequential scan (current): ~30-120 seconds at 10M records
// - HNSW index (future):       ~10-50ms at 10M records
// Expected speedup: 1000-3000x
```

#### 6. Monitor Index Build Time
**Index creation at scale:**
- 1M records: ~5-15 minutes
- 10M records: ~1-3 hours
- Plan downtime or use `CREATE INDEX CONCURRENTLY`

```sql
-- Non-blocking index creation (recommended for production)
CREATE INDEX CONCURRENTLY IX_Embeddings_Vector_HNSW
ON "Embeddings"
USING hnsw ("Vector" vector_cosine_ops)
WITH (m = 16, ef_construction = 64);
```

#### 7. Update Configuration
**File:** `DocumentProcessingAPI.API/appsettings.json`

Add HNSW settings:
```json
{
  "VectorSearch": {
    "UseHNSWIndex": true,
    "HNSWSearchEf": 100,
    "IndexDimensions": 3072,
    "IndexType": "hnsw"
  }
}
```

---

## Alternative: Dual-Vector Strategy (If pgvector 0.9.0+ Takes Too Long)

### Option: Add Reduced Vector Column (1536 dimensions)

**Implementation Steps:**

#### 1. Add VectorReduced Column
**File:** `DocumentProcessingAPI.Core/Entities/Embedding.cs`

```csharp
/// <summary>
/// Reduced vector embedding (1536 dimensions via PCA)
/// HNSW-indexed for fast approximate nearest neighbor search
/// </summary>
public Vector? VectorReduced { get; set; }
```

#### 2. Implement Dimension Reduction Service
**New File:** `DocumentProcessingAPI.Infrastructure/Services/DimensionReductionService.cs`

```csharp
public class DimensionReductionService
{
    // Use PCA or simple truncation to reduce 3072 -> 1536
    public float[] ReduceDimensions(float[] fullVector);
}
```

#### 3. Create Migration with HNSW on Reduced Vector
```sql
ALTER TABLE "Embeddings" ADD COLUMN "VectorReduced" vector(1536);

CREATE INDEX IX_Embeddings_VectorReduced_HNSW
ON "Embeddings"
USING hnsw ("VectorReduced" vector_cosine_ops);
```

#### 4. Implement Two-Stage Search
**Update:** `PgVectorService.SearchSimilarAsync`

```csharp
// Stage 1: Fast search on VectorReduced (HNSW) -> top 100 candidates
// Stage 2: Re-rank with full Vector -> top 10 most accurate
// Result: ~98% accuracy, 10-100ms performance
```

**Accuracy Trade-off:**
- Dimension reduction loss: ~2-5%
- Two-stage re-ranking: ~98-99% final accuracy

---

## Performance Targets (10 Million Records)

| Method | Current | With HNSW |
|--------|---------|-----------|
| Sequential Scan (3072d) | 30-120s | N/A |
| HNSW Index (3072d) | Not Possible | **10-50ms** ⭐ |
| Two-Stage (1536d→3072d) | N/A | **50-100ms** |

---

## Monitoring & Validation

### After HNSW Implementation, Monitor:

1. **Query Performance**
   - Log search times in `SearchSimilarAsync`
   - Alert if queries exceed 100ms

2. **Index Size**
   ```sql
   SELECT pg_size_pretty(pg_relation_size('IX_Embeddings_Vector_HNSW'));
   ```

3. **Recall Quality**
   - Compare HNSW results vs exact search on sample queries
   - Target: >95% recall at top-10 results

4. **Index Maintenance**
   - HNSW indexes degrade with frequent updates
   - Consider periodic `REINDEX` if accuracy drops

---

## References

- pgvector GitHub: https://github.com/pgvector/pgvector
- HNSW Index Documentation: https://github.com/pgvector/pgvector#hnsw
- Dimension Limit Issue: https://github.com/pgvector/pgvector/issues/461
- Performance Tuning: https://github.com/pgvector/pgvector#performance

---

## Timeline

- **Check pgvector releases:** Monthly
- **Estimated 0.9.0 release:** Unknown (community-driven project)
- **Implementation time:** 2-4 hours after release
- **Testing & validation:** 1-2 days

---

**Last Updated:** 2025-10-22
**Current pgvector Version:** 0.8.1
**Current Vector Dimensions:** 3072 (Gemini embeddings)
