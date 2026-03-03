# Complete API Flow with Ollama Embeddings

## Architecture Overview

This document explains the complete flow of embedding generation and search in the Document Processing API using **Ollama**.

---

## 🔄 Complete Processing Flow

### 1. **Record Processing API** (`POST /api/RecordEmbedding/process-all`)

**Purpose**: Process Content Manager records, extract text, generate embeddings, and store in PostgreSQL

**Flow**:

```
┌─────────────────────────────────────────────────────────────────────┐
│ 1. API Receives Request                                             │
│    POST /api/RecordEmbedding/process-all?searchString=*            │
└────────────────────────────┬────────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────────┐
│ 2. RecordEmbeddingController                                        │
│    Controllers/RecordEmbeddingController.cs:40                      │
│    ↓                                                                 │
│    await _recordEmbeddingService.ProcessAllRecordsAsync()          │
└────────────────────────────┬────────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────────┐
│ 3. RecordEmbeddingService                                           │
│    Services/RecordEmbeddingService.cs:50-346                        │
│                                                                      │
│    3a. Connect to Content Manager                                   │
│        ├─ Get records paginated (1000 per page)                    │
│        └─ Smart change detection (DateModified comparison)          │
│                                                                      │
│    3b. For each record (parallel, MAX_PARALLEL_TASKS = 10):        │
│        ├─ Download file from Content Manager                       │
│        ├─ Extract text (PDF, DOCX, XLSX, etc.)                     │
│        │  └─ DocumentProcessor.ExtractTextAsync()                  │
│        │                                                            │
│        ├─ Build metadata text (title, dates, ACL, etc.)            │
│        │  └─ BuildRecordTextComponentsAsync()                      │
│        │                                                            │
│        ├─ Chunk text (1500 tokens, 150 overlap)                    │
│        │  └─ TextChunkingService.ChunkTextAsync()                  │
│        │                                                            │
│        └─ For each chunk:                                          │
│             ├─ Add metadata header to chunk                         │
│             │                                                        │
│             ├─ ⭐ GENERATE EMBEDDING (Line 388) ⭐                  │
│             │   var embedding = await _embeddingService             │
│             │       .GenerateEmbeddingAsync(enrichedChunkContent);  │
│             │                                                        │
│             └─ Create VectorData object                            │
└────────────────────────────┬────────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────────┐
│ 4. OllamaEmbeddingService                                           │
│    Services/OllamaEmbeddingService.cs:65-117                        │
│                                                                      │
│    4a. Prepare HTTP request to Ollama                              │
│        {                                                            │
│          "model": "bge-m3",                                         │
│          "prompt": "<chunk text with metadata>"                     │
│        }                                                            │
│                                                                      │
│    4b. POST http://localhost:11434/api/embeddings                  │
│                                                                      │
│    4c. Ollama processes on GPU (CUDA automatic)                    │
│        ├─ Tokenizes text                                           │
│        ├─ Runs bge-m3 model                                        │
│        └─ Returns 1024-dimensional float vector                     │
│                                                                      │
│    4d. Parse response and return float[]                           │
└────────────────────────────┬────────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────────┐
│ 5. Save to PostgreSQL                                               │
│    PgVectorService.SaveEmbeddingsBatchAsync()                       │
│                                                                      │
│    INSERT INTO "Embeddings" (                                       │
│      embedding_id,           -- cm_record_{URI}_chunk_{index}      │
│      vector,                 -- vector(1024) - pgvector type       │
│      record_uri,             -- Content Manager record ID          │
│      record_title,           -- Document title                     │
│      chunk_content,          -- Full text chunk                    │
│      chunk_index,            -- Chunk number (0, 1, 2...)          │
│      date_created,           -- Record creation date               │
│      file_type,              -- pdf, docx, xlsx, etc.              │
│      ...                     -- + 15 more metadata fields          │
│    )                                                                │
│                                                                      │
│    Also creates PostgreSQL FTS vector (search_vector tsvector)     │
│    for hybrid search with keyword matching                          │
└─────────────────────────────────────────────────────────────────────┘
```

**Key Points**:
- Processes **1000 records per page**
- **10 concurrent tasks** for parallel processing
- **Smart change detection**: Only reprocesses if DateModified changed
- **Automatic chunking**: Large documents split into 1500-token chunks
- **Each chunk gets its own embedding**: Enables granular search
- **Metadata enrichment**: Each chunk has record metadata for filtering

---

## 🔍 Complete Search Flow

### 2. **Search API** (`POST /api/RecordEmbedding/search`)

**Purpose**: Semantic search with hybrid PostgreSQL FTS, date filtering, and AI answer synthesis

**Flow**:

```
┌─────────────────────────────────────────────────────────────────────┐
│ 1. API Receives Request                                             │
│    POST /api/RecordEmbedding/search                                │
│    {                                                                │
│      "query": "Find documents about API configuration",            │
│      "topK": 10,                                                   │
│      "minimumScore": 0.3                                           │
│    }                                                               │
└────────────────────────────┬────────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────────┐
│ 2. RecordEmbeddingController                                        │
│    Controllers/RecordEmbeddingController.cs:82                      │
│    ↓                                                                 │
│    await _recordSearchService.SearchRecordsAsync()                 │
└────────────────────────────┬────────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────────┐
│ 3. RecordSearchService - STEP 1: Query Analysis                    │
│    Services/RecordSearchService.cs:120-157                          │
│                                                                      │
│    3a. Clean and normalize query                                   │
│    3b. Extract date range (if present)                             │
│        Example: "documents from October 2024"                       │
│        → startDate: 2024-10-01, endDate: 2024-10-31               │
│    3c. Extract file type filters                                   │
│        Example: "PDF documents" → fileTypeFilters: ["pdf"]        │
│    3d. Extract sorting intent                                      │
│        Example: "latest documents" → isLatest: true                │
└────────────────────────────┬────────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────────┐
│ 4. RecordSearchService - STEP 2: Generate Query Embedding          │
│    Services/RecordSearchService.cs:162-182                          │
│                                                                      │
│    4a. ⭐ GENERATE EMBEDDING FOR QUERY (Line 177) ⭐               │
│        var queryEmbedding = await _embeddingService                │
│            .GenerateEmbeddingAsync(cleanQuery);                    │
│                                                                      │
│    → Calls OllamaEmbeddingService                                  │
│    → POST http://localhost:11434/api/embeddings                    │
│    → Returns 1024-dimensional float vector                          │
└────────────────────────────┬────────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────────┐
│ 5. RecordSearchService - STEP 3: Hybrid Search                     │
│    Services/RecordSearchService.cs:188-233                          │
│                                                                      │
│    5a. PostgreSQL Hybrid Search (Line 202-208)                     │
│        await _pgVectorService.SearchSimilarWithKeywordBoostAsync(  │
│            queryEmbedding,      // 1024-dim vector                 │
│            cleanQuery,          // raw text query                  │
│            searchLimit: 100,    // fetch more for filtering        │
│            minScore: 0.3,       // similarity threshold            │
│            keywordBoostWeight: 0.3  // 30% FTS, 70% semantic      │
│        );                                                          │
│                                                                      │
│    5b. PostgreSQL executes:                                        │
│        SELECT                                                       │
│          id,                                                        │
│          1 - (vector <=> query_vector) AS similarity,             │
│          ts_rank(search_vector, query_tsquery) AS fts_score,      │
│          -- Combine scores: 70% semantic + 30% keyword            │
│          (0.7 * (1 - (vector <=> query_vector))) +                │
│          (0.3 * ts_rank(search_vector, query_tsquery))            │
│            AS combined_score                                       │
│        FROM embeddings                                             │
│        WHERE 1 - (vector <=> query_vector) > 0.3                  │
│        ORDER BY combined_score DESC                                │
│        LIMIT 100;                                                  │
│                                                                      │
│    Returns: List of matching chunks with similarity scores         │
└────────────────────────────┬────────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────────┐
│ 6. RecordSearchService - STEP 4: Post-Filtering                    │
│    Services/RecordSearchService.cs:239-322                          │
│                                                                      │
│    6a. Apply file type filter (if specified)                       │
│        Filter to only PDF, DOCX, etc.                              │
│                                                                      │
│    6b. Apply date range filter (if extracted)                      │
│        Filter to DateCreated between startDate and endDate         │
│                                                                      │
│    6c. Deduplicate by record_uri                                   │
│        Multiple chunks from same record → keep best match          │
│                                                                      │
│    6d. Apply sorting                                               │
│        - If "earliest" → sort by DateCreated ASC                   │
│        - If "latest" → sort by DateCreated DESC                    │
│        - Else → sort by relevance score DESC                       │
│                                                                      │
│    6e. Take top K results                                          │
└────────────────────────────┬────────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────────┐
│ 7. RecordSearchService - STEP 5: ACL Filtering                     │
│    Services/RecordSearchService.cs:328-335                          │
│                                                                      │
│    7a. Check user permissions for each record                      │
│        Connect to Content Manager and verify access                │
│        Filter out records user cannot access                        │
└────────────────────────────┬────────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────────┐
│ 8. Convert to Search Result DTOs                                    │
│    Build response with:                                             │
│    - RecordUri, RecordTitle, DateCreated                           │
│    - ContentPreview (first 500 chars)                              │
│    - RelevanceScore (similarity)                                   │
│    - All metadata fields                                            │
└────────────────────────────┬────────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────────┐
│ 9. AI Answer Synthesis (Optional)                                  │
│    OllamaGemma7bService.SynthesizeRecordAnswerAsync()             │
│                                                                      │
│    9a. Build context from top 10 results                           │
│        Includes chunk content + metadata                            │
│                                                                      │
│    9b. Call Ollama Gemma 7B (Line 267)                            │
│        POST http://localhost:11434/api/generate                     │
│        {                                                            │
│          "model": "gemma:7b",                                       │
│          "prompt": "Question: {query}\n\n                          │
│                     Context: {top 10 results}\n\n                  │
│                     Answer:"                                        │
│        }                                                            │
│                                                                      │
│    9c. Ollama processes on GPU                                     │
│        - Loads Gemma 7B model                                       │
│        - Generates coherent answer                                  │
│        - Returns synthesized text                                   │
└────────────────────────────┬────────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────────┐
│ 10. Return Response                                                 │
│     {                                                               │
│       "query": "Find documents about API configuration",           │
│       "results": [                                                 │
│         {                                                           │
│           "recordUri": 123456,                                     │
│           "recordTitle": "API Configuration Guide.pdf",            │
│           "contentPreview": "This document describes...",          │
│           "relevanceScore": 0.89,                                  │
│           "dateCreated": "2024-10-15",                             │
│           ...                                                       │
│         },                                                          │
│         ...                                                         │
│       ],                                                            │
│       "totalResults": 10,                                          │
│       "queryTime": 1.23,                                           │
│       "synthesizedAnswer": "Based on the documents, the API..."    │
│     }                                                              │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 🎯 Key Components

### 1. **OllamaEmbeddingService** (NEW)

**File**: `DocumentProcessingAPI.Infrastructure/Services/OllamaEmbeddingService.cs`

**Responsibilities**:
- Generate embeddings for text chunks
- Generate embeddings for search queries
- Call Ollama `/api/embeddings` endpoint
- Return 1024-dimensional float vectors

**Used By**:
- `RecordEmbeddingService` (document processing)
- `RecordSearchService` (query embedding)
- `AIRecordService` (Q&A embedding)

**Configuration**:
```json
"Ollama": {
  "BaseUrl": "http://localhost:11434",
  "EmbeddingModel": "bge-m3",
  "EmbeddingDimension": "1024"
}
```

**Performance**:
- CPU: ~200-500ms per embedding
- GPU (CUDA): ~10-50ms per embedding
- Batch processing: 10-20 docs/sec (GPU)

---

### 2. **OllamaGemma7bService** (Existing)

**File**: `DocumentProcessingAPI.Infrastructure/Services/OllamaGemma7bService.cs`

**Responsibilities**:
- Extract keywords from queries
- Synthesize AI answers from search results
- Call Ollama `/api/generate` endpoint

**Configuration**:
```json
"Ollama": {
  "BaseUrl": "http://localhost:11434",
  "ModelName": "gemma:7b"
}
```

**Performance**:
- First request: 5-10 seconds (model loading)
- Subsequent: 1-5 seconds per response
- Memory: ~8GB RAM

---

### 3. **PostgreSQL + pgvector**

**Vector Storage**:
```sql
CREATE TABLE embeddings (
  id BIGSERIAL PRIMARY KEY,
  embedding_id VARCHAR(255) UNIQUE,
  vector vector(1024),  -- 1024 dimensions for bge-m3
  record_uri BIGINT,
  record_title VARCHAR(500),
  chunk_content TEXT,
  chunk_index INTEGER,
  date_created TIMESTAMP,
  file_type VARCHAR(200),
  search_vector tsvector,  -- For FTS
  ...
);

-- Vector similarity index (HNSW for fast search)
CREATE INDEX ON embeddings USING hnsw (vector vector_cosine_ops);

-- Full-Text Search index
CREATE INDEX ON embeddings USING gin (search_vector);
```

**Hybrid Search Query**:
- **70% Semantic**: Cosine similarity on vector embeddings
- **30% Keyword**: PostgreSQL FTS with `ts_rank`

---

## 📊 Data Flow Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                     Content Manager Records                     │
│                  (PDF, DOCX, XLSX, Images, etc.)               │
└───────────────────────────┬─────────────────────────────────────┘
                            │
                            │ Download & Extract
                            ▼
                    ┌───────────────┐
                    │ Text Chunks   │
                    │ (1500 tokens) │
                    └───────┬───────┘
                            │
                            │ For each chunk
                            ▼
                ┌───────────────────────┐
                │  Ollama bge-m3        │
                │  (Embedding Model)    │
                │  GPU Accelerated      │
                └───────────┬───────────┘
                            │
                            │ 1024-dim vector
                            ▼
                ┌───────────────────────┐
                │   PostgreSQL          │
                │   + pgvector          │
                │   + FTS (tsvector)    │
                └───────────┬───────────┘
                            │
        ┌───────────────────┴───────────────────┐
        │                                       │
        │ User Query                           │
        ▼                                       │
┌───────────────┐                              │
│ User enters   │                              │
│ search query  │                              │
└───────┬───────┘                              │
        │                                       │
        │ Generate embedding                    │
        ▼                                       │
┌───────────────────┐                          │
│  Ollama bge-m3    │                          │
│  (Query Embed)    │                          │
└───────┬───────────┘                          │
        │                                       │
        │ 1024-dim vector                       │
        ▼                                       │
┌───────────────────────────────┐              │
│ PostgreSQL Hybrid Search      │◄─────────────┘
│ • Vector Similarity (70%)     │
│ • FTS Keyword Match (30%)     │
└───────┬───────────────────────┘
        │
        │ Top K results
        ▼
┌───────────────────┐
│ Filter & Sort     │
│ • Date range      │
│ • File type       │
│ • ACL check       │
│ • Deduplicate     │
└───────┬───────────┘
        │
        │ Filtered results
        ▼
┌───────────────────────┐
│  Ollama Gemma 7B      │
│  (Answer Synthesis)   │
│  GPU Accelerated      │
└───────────┬───────────┘
            │
            │ AI-synthesized answer
            ▼
    ┌───────────────┐
    │   Response    │
    │   to User     │
    └───────────────┘
```

---

## 🔥 Performance Metrics

### Processing Performance

| Operation | CPU | GPU (CUDA) |
|-----------|-----|------------|
| Single embedding | ~300ms | ~20ms |
| 100 embeddings | ~30s | ~2s |
| 1000 records (avg 5 chunks each) | ~25 min | ~2 min |

### Search Performance

| Stage | Time |
|-------|------|
| Query embedding generation | 20-50ms (GPU) |
| PostgreSQL hybrid search | 50-200ms |
| ACL filtering | 100-500ms |
| AI answer synthesis | 1-5s |
| **Total** | **~1.5-6s** |

---

## 🛠️ Configuration Summary

### appsettings.json

```json
{
  "ConnectionStrings": {
    "PostgresConnection": "Host=localhost;Port=5432;Database=DocEmbeddings;Username=postgres;Password=***"
  },
  "Ollama": {
    "BaseUrl": "http://localhost:11434",
    "ModelName": "gemma:7b",
    "EmbeddingModel": "bge-m3",
    "EmbeddingDimension": "1024",
    "UseCuda": "true",
    "CudaDeviceId": "0"
  }
}
```

### Program.cs

```csharp
// Embedding service - Used by processing and search
builder.Services.AddScoped<IEmbeddingService, OllamaEmbeddingService>();

// AI text generation - Used by Q&A and keyword extraction
builder.Services.AddScoped<IRecordSearchGoogleServices, OllamaGemma7bService>();
```

---

## ✅ Verification Checklist

- [x] Ollama installed and running
- [x] Models pulled: `gemma:7b` and `bge-m3`
- [x] PostgreSQL with pgvector extension
- [x] Database configured with `vector(1024)`
- [x] `OllamaEmbeddingService` registered in DI
- [x] `OllamaGemma7bService` registered in DI
- [x] ONNX dependencies removed
- [x] Old service files deleted
- [x] GPU drivers updated (if using CUDA)

---

## 🚀 Testing

### 1. Test Embedding Generation
```bash
curl http://localhost:11434/api/embeddings -d '{
  "model": "bge-m3",
  "prompt": "This is a test document"
}'
```

### 2. Test Processing
```bash
POST http://localhost:5000/api/RecordEmbedding/process-all?searchString=*
```

### 3. Test Search
```bash
POST http://localhost:5000/api/RecordEmbedding/search
{
  "query": "Find documents about API configuration",
  "topK": 10,
  "minimumScore": 0.3
}
```

### 4. Monitor GPU Usage
```bash
# Watch GPU utilization while processing
nvidia-smi -l 1
```

---

## 📚 Related Documentation

- `LOCAL_MODELS_SETUP.md` - Ollama installation and setup
- `MIGRATION_SUMMARY.md` - Migration from ONNX to Ollama
- `WINDOWS_AUTHENTICATION_GUIDE.md` - Authentication setup
- `QUICK_START.md` - Quick reference guide

---

**Last Updated**: January 28, 2026
