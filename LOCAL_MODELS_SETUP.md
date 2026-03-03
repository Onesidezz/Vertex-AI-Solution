# Local Models Setup Guide

This guide explains how to set up Ollama for the Document Processing API.

## Overview

The application uses **Ollama** for all AI operations:

- **Embeddings**: Ollama with bge-m3 or nomic-embed-text (1024 dimensions)
- **AI Text Generation**: Ollama with Gemma 7B for Q&A and summarization

## Prerequisites

- .NET 8.0 SDK
- PostgreSQL with pgvector extension
- Ollama installed locally
- NVIDIA GPU (optional, for CUDA acceleration)

---

## 1. Install and Configure Ollama

### Step 1: Install Ollama

**Windows:**
1. Download Ollama from https://ollama.com/download
2. Run the installer
3. Ollama will start automatically and run on `http://localhost:11434`

**Linux/macOS:**
```bash
curl -fsSL https://ollama.com/install.sh | sh
```

### Step 2: Pull Required Models

Open a terminal and run:

```bash
# For text generation (Q&A, summarization)
ollama pull gemma:7b

# For embeddings (choose one)
ollama pull bge-m3          # Recommended: 1024 dimensions, multilingual
# OR
ollama pull nomic-embed-text  # Alternative: 768 dimensions
```

**Model Details:**
- `gemma:7b` - ~4.5 GB - Used for keyword extraction and answer synthesis
- `bge-m3` - ~2.2 GB - Used for document embeddings and search queries
- `nomic-embed-text` - ~274 MB - Smaller alternative for embeddings

### Step 3: Verify Ollama Installation

**Test text generation:**
```bash
curl http://localhost:11434/api/generate -d '{
  "model": "gemma:7b",
  "prompt": "Hello, how are you?",
  "stream": false
}'
```

**Test embeddings:**
```bash
curl http://localhost:11434/api/embeddings -d '{
  "model": "bge-m3",
  "prompt": "This is a test document"
}'
```

You should receive JSON responses with generated text and embedding vectors.

### Step 4: Configure in appsettings.json

Update your `appsettings.json`:

```json
{
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

**Configuration Options:**
- `BaseUrl`: Ollama server URL (default: http://localhost:11434)
- `ModelName`: Text generation model (default: gemma:7b)
- `EmbeddingModel`: Embedding model (default: bge-m3)
- `EmbeddingDimension`: Vector dimension (1024 for bge-m3, 768 for nomic-embed-text)
- `UseCuda`: Enable GPU acceleration (true/false)
- `CudaDeviceId`: GPU device ID (default: 0)

---

## 2. GPU Acceleration (Optional)

Ollama automatically detects and uses your GPU if available.

### Verify GPU Usage

Check if Ollama is using your GPU:

```bash
# Windows/Linux
nvidia-smi

# You should see ollama process using GPU memory
```

### GPU Requirements

- **NVIDIA GPU**: CUDA-compatible (GTX 1660+, RTX series, etc.)
- **VRAM**: Minimum 4GB for bge-m3 + gemma:7b
- **Drivers**: Latest NVIDIA drivers with CUDA support

Ollama handles CUDA automatically - no manual configuration needed!

---

## 3. Update Database Configuration

### Important: Embedding Dimension

The bge-m3 model uses **1024 dimensions**.

**If you have existing embeddings, you have two options:**

### Option A: Regenerate All Embeddings (Recommended)

1. Truncate the embeddings table:
   ```sql
   TRUNCATE TABLE "Embeddings";
   ```

2. Re-run the record sync job:
   ```bash
   POST /api/RecordEmbedding/process-all
   ```

### Option B: Create a New Database

1. Create a new PostgreSQL database:
   ```sql
   CREATE DATABASE DocEmbeddings_Ollama;
   ```

2. Update connection string in `appsettings.json`:
   ```json
   "PostgresConnection": "Host=localhost;Port=5432;Database=DocEmbeddings_Ollama;Username=postgres;Password=YourPassword"
   ```

3. Run the application - it will create the schema automatically

---

## 4. Build and Run

1. Restore NuGet packages:
   ```bash
   dotnet restore
   ```

2. Build the project:
   ```bash
   dotnet build
   ```

3. Run the application:
   ```bash
   cd DocumentProcessingAPI.API
   dotnet run
   ```

4. Check the logs for successful initialization:
   ```
   ✅ Ollama Embedding Service initialized
      • Base URL: http://localhost:11434
      • Model: bge-m3
      • Embedding Dimension: 1024
   ✅ Ollama Gemma7b Service initialized - BaseUrl: http://localhost:11434, Model: gemma:7b
   ```

---

## 5. Testing the Setup

### Test Embedding Generation

Process a single test record:

```bash
POST http://localhost:5000/api/RecordEmbedding/test-record/123456
```

### Test Search with Embeddings

```bash
POST http://localhost:5000/api/RecordEmbedding/search
Content-Type: application/json

{
  "query": "Find documents about API configuration",
  "topK": 10,
  "minimumScore": 0.3
}
```

### Test AI Answer Synthesis

The search API automatically uses Ollama Gemma 7B for answer synthesis.

---

## Performance Considerations

### Ollama Performance

**Embeddings (bge-m3):**
- **CPU Mode**: ~200-500ms per embedding
- **GPU Mode (CUDA)**: ~10-50ms per embedding
- **Batch Processing**: 10-20 documents per second (GPU)

**Text Generation (Gemma 7B):**
- **First Request**: 5-10 seconds (model loading into memory)
- **Subsequent Requests**: 1-5 seconds per response
- **Memory**: Requires ~8GB RAM minimum

### Optimization Tips

1. **GPU Acceleration**:
   - Significantly faster embeddings (10-20x speedup)
   - Ollama automatically uses GPU when available

2. **Model Preloading**:
   - Ollama keeps models in memory after first use
   - Restart Ollama to unload models: `ollama serve`

3. **Concurrent Processing**:
   - Ollama handles concurrent requests efficiently
   - Limit parallelism to avoid GPU memory issues

---

## Troubleshooting

### Ollama Issues

**Problem**: Connection refused to http://localhost:11434
- **Solution**: Ensure Ollama is running
  - Windows: Check system tray or restart Ollama Desktop
  - Linux/macOS: Run `ollama serve`

**Problem**: Model not found error
- **Solution**: Pull the model: `ollama pull bge-m3` or `ollama pull gemma:7b`

**Problem**: Slow first request
- **Solution**: Normal behavior - Ollama loads model into memory on first use

**Problem**: Out of memory errors
- **Solution**:
  - Close other GPU applications
  - Use smaller models (nomic-embed-text instead of bge-m3)
  - Reduce batch sizes in processing

### Embedding Dimension Mismatch

**Problem**: "vector has wrong dimension" error
- **Solution**:
  1. Check `EmbeddingDimension` in appsettings.json matches your model
  2. Verify database vector column: `vector(1024)` for bge-m3
  3. Regenerate all embeddings

### GPU Not Being Used

**Problem**: Ollama using CPU despite having GPU
- **Solution**:
  1. Update NVIDIA drivers: https://www.nvidia.com/download/index.aspx
  2. Restart Ollama after driver update
  3. Check `nvidia-smi` shows CUDA 11.0+ or 12.0+

---

## Configuration Reference

### Complete appsettings.json Example

```json
{
  "ConnectionStrings": {
    "PostgresConnection": "Host=localhost;Port=5432;Database=DocEmbeddings;Username=postgres;Password=Oneside@2"
  },
  "Ollama": {
    "BaseUrl": "http://localhost:11434",
    "ModelName": "gemma:7b",
    "EmbeddingModel": "bge-m3",
    "EmbeddingDimension": "1024",
    "UseCuda": "true",
    "CudaDeviceId": "0"
  },
  "FileStorage": {
    "BasePath": "./uploads"
  }
}
```

---

## Model Comparison

| Model | Dimensions | Size | Speed (CPU) | Speed (GPU) | Use Case |
|-------|-----------|------|-------------|-------------|----------|
| bge-m3 | 1024 | 2.2GB | ~300ms | ~20ms | Multilingual, best quality |
| nomic-embed-text | 768 | 274MB | ~150ms | ~10ms | Faster, English-focused |
| gemma:7b | N/A | 4.5GB | N/A | 1-5s | Text generation, Q&A |

---

## Additional Resources

- **Ollama Official Site**: https://ollama.com/
- **Ollama GitHub**: https://github.com/ollama/ollama
- **Model Library**: https://ollama.com/library
- **bge-m3 Model**: https://ollama.com/library/bge-m3
- **Gemma Models**: https://ollama.com/library/gemma

---

## Migration Checklist

- [ ] Ollama installed and running
- [ ] Models pulled (`gemma:7b` and `bge-m3`)
- [ ] GPU drivers updated (if using CUDA)
- [ ] appsettings.json updated with correct configuration
- [ ] Database vector dimension updated to 1024
- [ ] Old embeddings cleared or new database created
- [ ] Application builds successfully
- [ ] Test embedding generation works
- [ ] Test search works
- [ ] Test AI synthesis works
- [ ] GPU acceleration verified (if available)

---

## Quick Start Commands

```bash
# Install Ollama (Windows: download from ollama.com)
curl -fsSL https://ollama.com/install.sh | sh

# Pull required models
ollama pull gemma:7b
ollama pull bge-m3

# Verify Ollama is running
curl http://localhost:11434/api/tags

# Test embedding generation
curl http://localhost:11434/api/embeddings -d '{"model":"bge-m3","prompt":"test"}'

# Clear old embeddings (PostgreSQL)
psql -U postgres -d DocEmbeddings -c "TRUNCATE TABLE \"Embeddings\";"

# Run the application
cd DocumentProcessingAPI.API
dotnet run
```

---

## Support

If you encounter issues:

1. Check Ollama is running: `ollama list`
2. View Ollama logs (varies by OS)
3. Check application logs in `logs/` directory
4. Verify GPU with `nvidia-smi` (if using CUDA)
5. Test models directly with `curl` commands above
