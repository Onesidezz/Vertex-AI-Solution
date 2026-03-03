# Quick Start Guide

Get up and running with local models in minutes.

---

## Prerequisites

- .NET 8.0 SDK installed
- PostgreSQL with pgvector running
- At least 8GB free RAM

---

## Step 1: Install Ollama (2 minutes)

### Windows
1. Download from https://ollama.com/download
2. Run installer
3. Ollama auto-starts on `http://localhost:11434`

### Linux/macOS
```bash
curl -fsSL https://ollama.com/install.sh | sh
```

---

## Step 2: Pull Gemma 7B Model (5-10 minutes)

```bash
ollama pull gemma:7b
```

Wait for download to complete (~4.5 GB).

---

## Step 3: Set Up BGE-M3 ONNX (5 minutes)

### A. Clone and Copy Implementation
```bash
# Clone the repository
git clone https://github.com/yuniko-software/bge-m3-onnx.git

# Copy the C# implementation files to your Infrastructure project
# (Copy all .cs files from bge-m3-onnx/samples/dotnet/BgeM3.Onnx/ to DocumentProcessingAPI.Infrastructure/)
```

### B. Download Model Files
1. Go to: https://github.com/yuniko-software/bge-m3-onnx/releases
2. Download `onnx.zip`
3. Extract to `DocumentProcessingAPI/DocumentProcessingAPI.API/models/bge-m3/`

Your structure should be:
```
DocumentProcessingAPI.API/
└── models/
    └── bge-m3/
        ├── tokenizer.json
        ├── model.onnx
        └── model.onnx.data (if present)
```

---

## Step 4: Update Configuration

The `appsettings.json` is already configured. Verify paths are correct:

```json
"BgeM3": {
  "TokenizerPath": "./models/bge-m3/tokenizer.json",
  "ModelPath": "./models/bge-m3/model.onnx",
  "EmbeddingDimension": "1024",
  "UseCuda": "false"
},
"Ollama": {
  "BaseUrl": "http://localhost:11434",
  "ModelName": "gemma:7b"
}
```

---

## Step 5: Clear Old Embeddings

**Important**: BGE-M3 uses 1024 dimensions (Google used 3072).

Connect to PostgreSQL and run:
```sql
TRUNCATE TABLE "Embeddings";
```

Or create a new database:
```sql
CREATE DATABASE DocEmbeddings_Local;
```

Then update connection string in `appsettings.json`.

---

## Step 6: Restore Packages

```bash
cd DocumentProcessingAPI
dotnet restore
```

---

## Step 7: Build and Run

```bash
cd DocumentProcessingAPI.API
dotnet run
```

Look for these success messages:
```
✅ BGE-M3 ONNX Embedding Service initialized with CPU (Dimensions: 1024)
✅ Ollama Gemma7b Service initialized - BaseUrl: http://localhost:11434, Model: gemma:7b
```

---

## Step 8: Test

### Test 1: Health Check
```bash
curl http://localhost:5000/health
```

### Test 2: Embedding Generation
Visit Swagger UI at `http://localhost:5000/swagger` and test the embedding endpoint.

### Test 3: AI Search
Test the search endpoint with AI synthesis enabled.

---

## Troubleshooting

### "Connection refused to localhost:11434"
```bash
# Check if Ollama is running
curl http://localhost:11434/api/tags

# If not, start it:
ollama serve  # Linux/macOS
# or check Windows services for "Ollama"
```

### "Model file not found"
Verify model files exist:
```bash
ls DocumentProcessingAPI.API/models/bge-m3/
# Should show: tokenizer.json, model.onnx
```

### "Dimension mismatch"
You forgot to clear old embeddings. Run:
```sql
TRUNCATE TABLE "Embeddings";
```

---

## Performance Tips

- **First Ollama request takes 5-10 seconds** (model loading) - this is normal
- **Subsequent requests are faster** (1-5 seconds)
- For GPU acceleration, see `LOCAL_MODELS_SETUP.md`

---

## Next Steps

1. Re-run record sync to generate new embeddings
2. Test search functionality
3. See `LOCAL_MODELS_SETUP.md` for advanced configuration
4. See `MIGRATION_SUMMARY.md` for complete change details

---

## Clean Up (Optional)

Remove old Google files:
```bash
rm my-uk-project-471009-09c7eb717b39.json  # Service account key
```

---

That's it! You're now running completely locally with no external API dependencies.
