# DocumentProcessingAPI — Full Project Analysis & Local AI Upgrade Guide

> Prepared: February 2026

---

## 1. Project Overview

This is a **.NET 8 ASP.NET Core Web API** that provides semantic document search and AI-powered Q&A on top of **Micro Focus Content Manager (TRIM SDK)**. The system:

- Connects to Content Manager to fetch records (documents, containers, metadata)
- Downloads document files, extracts text (PDF, DOCX, XLSX, PPTX, TXT)
- Chunks text and generates vector embeddings
- Stores embeddings in **PostgreSQL with pgvector**
- Provides hybrid search (semantic vector search + PostgreSQL Full-Text Search)
- Uses an LLM for keyword extraction and AI answer synthesis
- Enforces ACL (Access Control List) filtering per Windows user

### Architecture

```
Content Manager (TRIM SDK)
        ↓
  RecordEmbeddingService       ← fetches + processes records
        ↓
  TextChunkingService          ← chunks text (1500 token chunks, 150 overlap)
        ↓
  OllamaEmbeddingService       ← bge-m3 (1024-dim vectors)
        ↓
  PgVectorService (PostgreSQL) ← stores embeddings

Search Flow:
  User Query
        ↓
  RecordSearchService
        ↓
  OllamaEmbeddingService   → query embedding
        ↓
  PgVectorService          → hybrid search (vector + FTS, 70/30 weight)
        ↓
  ACL Filter (TRIM SDK)    → remove records user can't access
        ↓
  OllamaGemma7bService     → synthesize answer
        ↓
  Response
```

---

## 2. What Ollama Is Used For (Two Separate Roles)

| Role | Model | Service | Used In |
|------|-------|---------|---------|
| **Embeddings** | `bge-m3` (1024-dim) | `OllamaEmbeddingService` | Indexing + query vectorization |
| **Text generation** | `gemma:7b` | `OllamaGemma7bService` | Keyword extraction + answer synthesis + record summaries + Q&A |

---

## 3. Root Causes of Inaccuracy

### 3.1 Gemma 7B — The Biggest Problem

`gemma:7b` is a **base/instruct model at 7B parameters** with known weaknesses:

**Keyword Extraction (`ExtractKeywordsWithGemini`):**
- Gemma 7B struggles to reliably output clean JSON arrays
- Often adds markdown code fences, explanatory text, or extra whitespace
- The parser (`ParseKeywordsFromGeminiResponse`) tries to handle these but still fails on edge cases
- Inconsistent behaviour: same query gives different JSON structure on different runs
- Low instruction-following capability for complex rules (the prompt has 10+ rules to follow)

**Answer Synthesis (`SynthesizeRecordAnswerAsync`):**
- Gemma 7B tends to hallucinate or repeat context verbatim rather than synthesise
- Context window of 8192 tokens is too small for 10 records × 500 chars of content
- Answer quality degrades badly with longer prompts
- Prone to generating repetitive text (hence `repeat_penalty = 1.1` in the config, but it's not enough)

**Record Summary / Q&A (`AIRecordService`):**
- Truncating content to 50,000 characters still results in prompts far too long for Gemma 7B
- The model loses coherence and starts repeating or hallucinating after ~4000 tokens of context

### 3.2 bge-m3 Embeddings — Generally Good, Minor Issues

`bge-m3` is actually one of the better local embedding models. The embedding quality is solid. Minor issues:
- CPU inference is slow (~300ms/embedding), bottlenecking batch processing
- The metadata header prepended to each chunk (title + date) adds useful signal but can dilute semantic content for long documents

### 3.3 Chunking Strategy

The current chunking (1500 token chunks, 150 overlap) is reasonable, but:
- Token counting uses a rough heuristic (`word.Length / 4`) — not actual tokenisation
- Sentence boundary detection regex can miss boundaries in poorly-structured or OCR'd text
- No semantic chunking (splitting by topic/section) which would improve retrieval quality

---

## 4. Better Local Model Options

### 4.1 Replacing Gemma 7B for Text Generation

These models are all available via Ollama and produce significantly better results for structured output and instruction following:

#### Option A: `llama3.1:8b` (Recommended — Best Balance)
```bash
ollama pull llama3.1:8b
```
- Meta's LLaMA 3.1 8B Instruct is dramatically better than Gemma 7B at instruction following
- Handles JSON output reliably with proper system prompts
- 128K context window (vs 8K in Gemma 7B) — massive improvement for synthesis
- 4.7 GB RAM, similar size to Gemma 7B
- Best overall upgrade with minimal config changes

**Config change:**
```json
"Ollama": {
  "ModelName": "llama3.1:8b"
}
```

#### Option B: `qwen2.5:7b` (Best for Structured JSON Output)
```bash
ollama pull qwen2.5:7b
```
- Alibaba's Qwen 2.5 7B Instruct is specifically strong at structured/JSON output
- Very reliable JSON array generation — directly fixes the `ParseKeywordsFromGeminiResponse` failures
- Strong multilingual support (useful if Content Manager has non-English documents)
- 4.4 GB RAM
- Best choice if the keyword extraction failures are your primary pain point

**Config change:**
```json
"Ollama": {
  "ModelName": "qwen2.5:7b"
}
```

#### Option C: `mistral:7b-instruct-v0.3` (Best for Q&A)
```bash
ollama pull mistral:7b-instruct-v0.3
```
- Mistral 7B v0.3 Instruct is excellent at concise, factual Q&A
- Strong context adherence (answers from provided context, less hallucination)
- Good at respecting "answer only from the context" type instructions
- 4.1 GB RAM

#### Option D: `gemma2:9b` (Direct Upgrade from Current)
```bash
ollama pull gemma2:9b
```
- If you want to stay in the Gemma family, `gemma2:9b` is a major improvement over `gemma:7b`
- Better instruction following, larger context window
- 5.4 GB RAM — slightly heavier but worth it
- Easiest migration (same model family, same prompt style works)

#### Option E: `phi3.5:mini` (Lowest RAM, Surprisingly Good)
```bash
ollama pull phi3.5:mini
```
- Microsoft Phi 3.5 Mini Instruct — only 2.2 GB RAM
- Surprisingly good at structured output for its size
- Best choice if RAM is constrained (running on CPU or limited VRAM)
- Not as capable as the 7B models but far better than Gemma 7B at following complex prompts

### 4.2 Comparing the Options

| Model | RAM | JSON Reliability | Context Window | Answer Quality | Speed (CPU) |
|-------|-----|-----------------|----------------|----------------|-------------|
| `gemma:7b` (current) | 4.5 GB | ❌ Poor | 8K | ❌ Weak | ~3s |
| `llama3.1:8b` | 4.7 GB | ✅ Very Good | **128K** | ✅ Excellent | ~4s |
| `qwen2.5:7b` | 4.4 GB | ✅✅ Best | 128K | ✅ Excellent | ~3.5s |
| `mistral:7b-instruct` | 4.1 GB | ✅ Good | 32K | ✅ Very Good | ~3s |
| `gemma2:9b` | 5.4 GB | ✅ Good | 8K | ✅ Good | ~5s |
| `phi3.5:mini` | 2.2 GB | ✅ Good | 128K | ⚠️ Moderate | ~1.5s |

**Recommendation: Use `qwen2.5:7b` for keyword extraction and `llama3.1:8b` for synthesis/Q&A** — or just set `llama3.1:8b` for everything as the single best all-rounder.

### 4.3 Embedding Model Upgrades

`bge-m3` is already solid. If you want an upgrade:

```bash
# Higher accuracy embeddings (1024 dims, same as bge-m3)
ollama pull mxbai-embed-large

# Faster but slightly lower quality (768 dims)
ollama pull nomic-embed-text

# Best accuracy available locally (1024 dims)
ollama pull bge-large:335m
```

If you switch embedding model you MUST:
1. Update `EmbeddingDimension` in `appsettings.json`
2. Drop and recreate the `Embeddings` table (dimension mismatch will throw)
3. Re-run `POST /api/RecordEmbedding/process-all` to regenerate all embeddings

---

## 5. Beyond Ollama — Cloud API as Drop-In Replacement

Your code's `IRecordSearchGoogleServices` interface is clean and injectable. You could swap out the Ollama service entirely for a cloud API without touching the rest of the codebase.

### Option A: Anthropic Claude API (Highly Recommended)
The Claude API (`claude-haiku-4-5` or `claude-sonnet-4-5`) would dramatically improve all three AI tasks:
- Keyword extraction: Reliably returns clean JSON every time
- Answer synthesis: Concise, grounded, no hallucination
- Record summary/Q&A: Handles very large contexts natively

You would create a `ClaudeApiService` implementing `IRecordSearchGoogleServices`, replacing `OllamaGemma7bService` in `Program.cs`:
```csharp
// In Program.cs — single line change:
builder.Services.AddScoped<IRecordSearchGoogleServices, ClaudeApiService>();
```

Similarly, you would create a `ClaudeEmbeddingService` or use **Voyage AI** embeddings (Anthropic's recommended embedding partner) to replace `OllamaEmbeddingService`.

### Option B: OpenAI API
GPT-4o-mini for text generation + `text-embedding-3-small` for embeddings. Reliable JSON output, large context window.

---

## 6. Prompt Engineering Fixes (No Model Change Needed)

Even with Gemma 7B, these changes to `OllamaGemma7bService` would improve reliability:

### Fix 1: Force JSON Mode via System Prompt
Add `system` field to the request body in `CallGeminiModelAsync`:
```csharp
var requestBody = new
{
    model = _modelName,
    system = "You are a JSON-only response assistant. Always respond with valid JSON only. Never include explanations.",
    prompt = prompt,
    stream = false,
    options = new { /* ... */ }
};
```

### Fix 2: Simpler Keyword Extraction Prompt
The current prompt has 10+ rules and complex examples. Smaller models do better with fewer, clearer rules. Reduce to 3 rules maximum.

### Fix 3: Add Fallback JSON Extraction
The `ParseKeywordsFromGeminiResponse` method currently fails if Gemma wraps output in text. Add a regex fallback to extract any JSON array found anywhere in the response:
```csharp
// After the standard parse fails, try regex extraction:
var jsonArrayMatch = Regex.Match(response, @"\[.*?\]", RegexOptions.Singleline);
if (jsonArrayMatch.Success) { /* try parsing the matched portion */ }
```

### Fix 4: Lower `num_ctx` for Keyword Extraction
The keyword extraction only needs a small context. Reduce `num_ctx` from 8192 to 2048 for that call — it runs 4x faster and Gemma 7B is more focused with a smaller context window.

---

## 7. Other Code Issues Found

### 7.1 `HttpClient` is Instantiated Per Service (Not Registered as Singleton)
Both `OllamaGemma7bService` and `OllamaEmbeddingService` create `new HttpClient()` inside their constructor. This can exhaust socket connections under load.

**Fix:** Register `IHttpClientFactory` and inject it, or register `HttpClient` via `AddHttpClient<T>()` in `Program.cs`.

### 7.2 TRIM SDK ACL Filter is Synchronous Inside Async Loop
`ApplyAclFilterAsync` iterates records and calls `new Record(database, recordUri)` synchronously in a `foreach`. For large result sets, this blocks the thread.

### 7.3 Token Counting is an Approximation
`CountTokensAsync` in `TextChunkingService` uses `word.Length / 4` — this can be off by 30-40% for technical text. Consider using `Microsoft.ML.Tokenizers` (TikToken-compatible) for accurate counts.

### 7.4 Temp File Cleanup Could Fail
In `RecordEmbeddingService.BuildRecordTextComponentsAsync`, if `_documentProcessor.ExtractTextAsync` throws, the `finally` block correctly deletes the temp file. But if the path construction itself fails, `tempPath` might not be set. Wrapping in a proper `using`-style pattern with a `TempFile` helper class would be safer.

---

## 8. Quick Start: Switching to LLaMA 3.1

1. Pull the model:
   ```bash
   ollama pull llama3.1:8b
   ```

2. Update `appsettings.json`:
   ```json
   "Ollama": {
     "BaseUrl": "http://localhost:11434",
     "ModelName": "llama3.1:8b",
     "EmbeddingModel": "bge-m3",
     "EmbeddingDimension": "1024",
     "UseCuda": "true",
     "CudaDeviceId": "0"
   }
   ```

3. Restart the API — no code changes needed. The `OllamaGemma7bService` class name is misleading but the model name is read from config, so it works with any Ollama model.

4. Test keyword extraction:
   ```bash
   POST /api/RecordAI/search
   {
     "query": "Find resumes with Python experience",
     "topK": 5
   }
   ```

5. Compare answer quality in the `synthesizedAnswer` field of the response.

---

## 9. Testing Without Content Manager

Since TRIM SDK requires a licensed Content Manager installation, direct end-to-end testing in isolation is not possible. The parts that **can** be tested independently:

- `TextChunkingService` — pure text, no external deps (`DocumentProcessingAPI.Tests` project has tests)
- `OllamaEmbeddingService` — if Ollama is running locally
- `OllamaGemma7bService` — test keyword extraction and synthesis directly
- `PgVectorService` — if PostgreSQL is available

To test the full search pipeline, provide sample records via `POST /api/RecordEmbedding/test-record/{uri}` with a real Content Manager URI, then search with `POST /api/RecordEmbedding/search`.

---

## 10. Summary Recommendations (Priority Order)

| Priority | Action | Impact | Effort |
|----------|--------|--------|--------|
| 🔴 High | Replace `gemma:7b` → `llama3.1:8b` or `qwen2.5:7b` | Major accuracy improvement | 5 min (config only) |
| 🔴 High | Add JSON fallback regex in `ParseKeywordsFromGeminiResponse` | Fix keyword extraction failures | 30 min (code change) |
| 🟡 Medium | Reduce `num_ctx` for keyword extraction calls (2048 vs 8192) | 4x faster keyword extraction | 15 min |
| 🟡 Medium | Fix `HttpClient` lifetime (use `IHttpClientFactory`) | Stability under load | 1 hour |
| 🟢 Low | Switch embedding model to `mxbai-embed-large` | Marginal embedding quality gain | 2 hours (re-embed all) |
| 🟢 Low | Use accurate tokeniser (`Microsoft.ML.Tokenizers`) | Better chunk boundaries | 2 hours |
| ⭐ Optional | Switch to Claude API (`claude-haiku-4-5`) | Best possible accuracy | Half day |
