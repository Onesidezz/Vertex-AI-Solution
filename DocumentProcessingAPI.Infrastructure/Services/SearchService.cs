using DocumentProcessingAPI.Core.DTOs;
using DocumentProcessingAPI.Core.Interfaces;
using DocumentProcessingAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DocumentProcessingAPI.Infrastructure.Services;


/// <summary>
/// Search service for semantic document search
/// </summary>
public class SearchService : ISearchService
{
    private readonly DocumentProcessingDbContext _context;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILocalEmbeddingStorageService _embeddingStorageService;
    private readonly QdrantVectorService _qdrantService;
    private readonly ILogger<SearchService> _logger;
    private readonly IConfiguration _configuration;

    public SearchService(
        DocumentProcessingDbContext context,
        IEmbeddingService embeddingService,
        ILocalEmbeddingStorageService embeddingStorageService,
        QdrantVectorService qdrantService,
        ILogger<SearchService> logger,
        IConfiguration configuration)
    {
        _context = context;
        _embeddingService = embeddingService;
        _embeddingStorageService = embeddingStorageService;
        _qdrantService = qdrantService;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<SearchResponseDto> SearchAsync(SearchRequestDto request)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation("Performing semantic search for query: {Query}", request.Query);

            // Enhance and preprocess query for better accuracy
            var enhancedQuery = await EnhanceQueryAsync(request.Query);
            _logger.LogDebug("Enhanced query: {EnhancedQuery}", enhancedQuery);

            // Generate embedding for enhanced query
            var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(enhancedQuery);

            // Search similar embeddings from Qdrant with higher TopK for re-ranking
            var expandedTopK = Math.Min(request.TopK * 3, 100); // Get more results for hybrid scoring
            _logger.LogInformation("🔍 Searching Qdrant for top {TopK} results", expandedTopK);

            var similarResults = await _qdrantService.SearchSimilarAsync(
                queryEmbedding, expandedTopK, Math.Max(request.MinimumScore - 0.1f, 0.0f));

            // Convert Qdrant results to compatible format
            var filteredResults = similarResults.Select(r => new VectorSearchResult
            {
                Id = r.id,
                Score = r.similarity,
                Metadata = r.metadata
            }).ToList();

            _logger.LogInformation("✅ Qdrant returned {Count} results", filteredResults.Count);

            // Get chunk IDs from vector results
            var chunkIds = filteredResults
                .Select(r => Guid.Parse(r.Metadata["document_id"].ToString()!))
                .ToList();

            // Get document chunks with document information
            var chunks = await _context.DocumentChunks
                .Include(c => c.Document)
                .Where(c => chunkIds.Contains(c.DocumentId))
                .ToListAsync();

            // Create search results with hybrid scoring
            var searchResults = new List<SearchResultDto>();
            var originalQuery = request.Query.ToLowerInvariant();
            var queryKeywords = ExtractKeywords(originalQuery);

            foreach (var vectorResult in filteredResults)
            {
                var chunkSequence = GetMetadataValue<int>(vectorResult.Metadata, "chunk_sequence");
                var documentId = Guid.Parse(GetMetadataValue<string>(vectorResult.Metadata, "document_id"));

                var chunk = chunks.FirstOrDefault(c =>
                    c.DocumentId == documentId && c.ChunkSequence == chunkSequence);

                if (chunk != null)
                {
                    // Calculate hybrid score: semantic + keyword + context factors
                    var hybridScore = CalculateHybridScore(
                        vectorResult.Score,
                        chunk.Content,
                        originalQuery,
                        queryKeywords,
                        chunk);

                    var searchResult = new SearchResultDto
                    {
                        ChunkId = chunk.Id,
                        DocumentId = chunk.DocumentId,
                        DocumentName = chunk.Document.FileName,
                        Content = chunk.Content,
                        RelevanceScore = hybridScore,
                        PageNumber = chunk.PageNumber,
                        ChunkSequence = chunk.ChunkSequence,
                        Metadata = new Dictionary<string, object>
                        {
                            ["token_count"] = chunk.TokenCount,
                            ["start_position"] = chunk.StartPosition,
                            ["end_position"] = chunk.EndPosition,
                            ["document_content_type"] = chunk.Document.ContentType,
                            ["document_upload_date"] = chunk.Document.UploadedAt,
                            ["chunk_created_date"] = chunk.CreatedAt,
                            ["original_semantic_score"] = vectorResult.Score,
                            ["keyword_boost"] = hybridScore - vectorResult.Score
                        }
                    };

                    // Add original vector metadata
                    foreach (var kvp in vectorResult.Metadata)
                    {
                        if (!searchResult.Metadata.ContainsKey(kvp.Key))
                        {
                            searchResult.Metadata[kvp.Key] = kvp.Value;
                        }
                    }

                    searchResults.Add(searchResult);
                }
            }

            // Sort by hybrid relevance score and apply diversity filtering
            searchResults = ApplyAdvancedRanking(searchResults, request)
                .Where(r => r.RelevanceScore >= request.MinimumScore) // Apply minimum score after hybrid scoring
                .Take(request.TopK) // Take only requested number after re-ranking
                .ToList();

            // Apply pagination
            var totalResults = searchResults.Count;
            var paginatedResults = searchResults
                .Skip((request.PageNumber - 1) * request.PageSize)
                .Take(request.PageSize)
                .ToList();

            // Generate AI answer synthesis using top-ranked results with better context
            var synthesizedAnswer = "";
            try
            {
                // Use top results and add surrounding context for better synthesis
                var contextualResults = await EnhanceResultsWithContext(searchResults.Take(3).ToList());
                synthesizedAnswer = await SynthesizeAnswerAsync(request.Query, contextualResults);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to synthesize answer for query: {Query}", request.Query);
            }

            stopwatch.Stop();

            _logger.LogInformation("Search completed. Found {ResultCount} results in {ElapsedMs}ms",
                totalResults, stopwatch.ElapsedMilliseconds);

            return new SearchResponseDto
            {
                Query = request.Query,
                Results = paginatedResults,
                TotalResults = totalResults,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize,
                QueryTime = (float)stopwatch.Elapsed.TotalSeconds,
                SynthesizedAnswer = synthesizedAnswer
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Search failed for query: {Query}", request.Query);

            return new SearchResponseDto
            {
                Query = request.Query,
                Results = new List<SearchResultDto>(),
                TotalResults = 0,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize,
                QueryTime = (float)stopwatch.Elapsed.TotalSeconds
            };
        }
    }

    public async Task<SearchResponseDto> SearchDocumentAsync(Guid documentId, SearchRequestDto request)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation("Performing document search for query: {Query} in document: {DocumentId}",
                request.Query, documentId);

            // Generate embedding for query
            var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(request.Query);

            // Search similar embeddings from Qdrant for specific document
            _logger.LogInformation("🔍 Searching Qdrant for document {DocumentId}", documentId);

            var similarResults = await _qdrantService.SearchSimilarAsync(
                queryEmbedding, request.TopK, request.MinimumScore, documentId.ToString());

            // Convert to compatible format
            var filteredResults = similarResults.Select(r => new VectorSearchResult
            {
                Id = r.id,
                Score = r.similarity,
                Metadata = r.metadata
            }).ToList();

            // Get document chunks
            var chunks = await _context.DocumentChunks
                .Include(c => c.Document)
                .Where(c => c.DocumentId == documentId)
                .ToListAsync();

            // Create search results
            var searchResults = filteredResults.Select(vectorResult =>
            {
                var chunkSequence = GetMetadataValue<int>(vectorResult.Metadata, "chunk_sequence");
                var chunk = chunks.FirstOrDefault(c => c.ChunkSequence == chunkSequence);

                if (chunk == null) return null;

                return new SearchResultDto
                {
                    ChunkId = chunk.Id,
                    DocumentId = chunk.DocumentId,
                    DocumentName = chunk.Document.FileName,
                    Content = chunk.Content,
                    RelevanceScore = vectorResult.Score,
                    PageNumber = chunk.PageNumber,
                    ChunkSequence = chunk.ChunkSequence,
                    Metadata = new Dictionary<string, object>
                    {
                        ["token_count"] = chunk.TokenCount,
                        ["start_position"] = chunk.StartPosition,
                        ["end_position"] = chunk.EndPosition,
                        ["chunk_created_date"] = chunk.CreatedAt
                    }
                };
            })
            .Where(r => r != null)
            .Cast<SearchResultDto>()
            .OrderByDescending(r => r.RelevanceScore)
            .ToList();

            // Apply pagination
            var totalResults = searchResults.Count;
            var paginatedResults = searchResults
                .Skip((request.PageNumber - 1) * request.PageSize)
                .Take(request.PageSize)
                .ToList();

            // Generate AI answer synthesis for the original results (not paginated)
            var synthesizedAnswer = "";
            try
            {
                synthesizedAnswer = await SynthesizeAnswerAsync(request.Query, searchResults.Take(5).ToList());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to synthesize answer for document search query: {Query}", request.Query);
            }

            stopwatch.Stop();

            _logger.LogInformation("Document search completed. Found {ResultCount} results in {ElapsedMs}ms",
                totalResults, stopwatch.ElapsedMilliseconds);

            return new SearchResponseDto
            {
                Query = request.Query,
                Results = paginatedResults,
                TotalResults = totalResults,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize,
                QueryTime = (float)stopwatch.Elapsed.TotalSeconds,
                SynthesizedAnswer = synthesizedAnswer
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Document search failed for query: {Query} in document: {DocumentId}",
                request.Query, documentId);

            return new SearchResponseDto
            {
                Query = request.Query,
                Results = new List<SearchResultDto>(),
                TotalResults = 0,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize,
                QueryTime = (float)stopwatch.Elapsed.TotalSeconds
            };
        }
    }

    public async Task<List<SearchResultDto>> GetSimilarChunksAsync(Guid chunkId, int topK = 5)
    {
        try
        {
            _logger.LogInformation("Finding similar chunks for chunk: {ChunkId}", chunkId);

            // Get the reference chunk
            var referenceChunk = await _context.DocumentChunks
                .Include(c => c.Document)
                .FirstOrDefaultAsync(c => c.Id == chunkId);

            if (referenceChunk == null || string.IsNullOrEmpty(referenceChunk.EmbeddingId))
            {
                _logger.LogWarning("Reference chunk not found or has no embedding: {ChunkId}", chunkId);
                return new List<SearchResultDto>();
            }

            // Get the embedding for the reference chunk from Qdrant
            var referenceEmbeddingData = await _qdrantService.GetEmbeddingAsync(referenceChunk.EmbeddingId);
            if (referenceEmbeddingData == null)
            {
                _logger.LogWarning("Reference embedding not found for chunk: {ChunkId}", chunkId);
                return new List<SearchResultDto>();
            }

            // Search for similar embeddings (excluding the reference itself)
            var similarEmbeddings = await _qdrantService.SearchSimilarAsync(
                referenceEmbeddingData.Value.embedding, topK + 1); // +1 to account for self-match

            // Remove self-match and convert to compatible format
            var similarResults = similarEmbeddings
                .Where(r => r.id != referenceChunk.EmbeddingId)
                .Take(topK)
                .Select(r => new VectorSearchResult
                {
                    Id = r.id,
                    Score = r.similarity,
                    Metadata = r.metadata
                })
                .ToList();

            _logger.LogInformation("✅ Found {Count} similar chunks in Qdrant", similarResults.Count);

            var documentIds = similarResults
                .Select(r => Guid.Parse(r.Metadata["document_id"].ToString()!))
                .Distinct()
                .ToList();

            var chunks = await _context.DocumentChunks
                .Include(c => c.Document)
                .Where(c => documentIds.Contains(c.DocumentId))
                .ToListAsync();

            var searchResults = new List<SearchResultDto>();

            foreach (var vectorResult in similarResults)
            {
                var chunkSequence = GetMetadataValue<int>(vectorResult.Metadata, "chunk_sequence");
                var documentId = Guid.Parse(GetMetadataValue<string>(vectorResult.Metadata, "document_id"));

                var chunk = chunks.FirstOrDefault(c =>
                    c.DocumentId == documentId && c.ChunkSequence == chunkSequence);

                if (chunk != null)
                {
                    searchResults.Add(new SearchResultDto
                    {
                        ChunkId = chunk.Id,
                        DocumentId = chunk.DocumentId,
                        DocumentName = chunk.Document.FileName,
                        Content = chunk.Content,
                        RelevanceScore = vectorResult.Score,
                        PageNumber = chunk.PageNumber,
                        ChunkSequence = chunk.ChunkSequence,
                        Metadata = new Dictionary<string, object>
                        {
                            ["token_count"] = chunk.TokenCount,
                            ["similarity_type"] = "semantic",
                            ["reference_chunk_id"] = chunkId
                        }
                    });
                }
            }

            _logger.LogInformation("Found {SimilarCount} similar chunks for chunk: {ChunkId}",
                searchResults.Count, chunkId);

            return searchResults;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to find similar chunks for: {ChunkId}", chunkId);
            return new List<SearchResultDto>();
        }
    }

    public async Task<byte[]> ExportSearchResultsToCsvAsync(SearchRequestDto searchRequest)
    {
        try
        {
            _logger.LogInformation("Exporting search results to CSV for query: {Query}", searchRequest.Query);

            // Perform search with high limit to get all results
            var exportRequest = new SearchRequestDto
            {
                Query = searchRequest.Query,
                TopK = 1000, // High limit for export
                MinimumScore = searchRequest.MinimumScore,
                DocumentId = searchRequest.DocumentId,
                PageNumber = 1,
                PageSize = 1000
            };

            SearchResponseDto searchResults;
            if (searchRequest.DocumentId.HasValue)
            {
                searchResults = await SearchDocumentAsync(searchRequest.DocumentId.Value, exportRequest);
            }
            else
            {
                searchResults = await SearchAsync(exportRequest);
            }

            // Create CSV content
            var csv = new StringBuilder();

            // Add header
            csv.AppendLine("Document Name,Chunk Sequence,Page Number,Relevance Score,Content,Token Count,Document ID,Chunk ID");

            // Add data rows
            foreach (var result in searchResults.Results)
            {
                var escapedContent = EscapeCsvValue(result.Content);
                var escapedDocumentName = EscapeCsvValue(result.DocumentName);

                csv.AppendLine($"{escapedDocumentName},{result.ChunkSequence},{result.PageNumber}," +
                              $"{result.RelevanceScore:F4},{escapedContent}," +
                              $"{result.Metadata.GetValueOrDefault("token_count", 0)}," +
                              $"{result.DocumentId},{result.ChunkId}");
            }

            _logger.LogInformation("Successfully exported {ResultCount} search results to CSV",
                searchResults.Results.Count);

            return Encoding.UTF8.GetBytes(csv.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export search results to CSV");
            throw new InvalidOperationException($"Failed to export search results: {ex.Message}", ex);
        }
    }

    private static string EscapeCsvValue(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        // Escape quotes and wrap in quotes if contains comma, quote, or newline
        var escaped = value.Replace("\"", "\"\"");
        if (escaped.Contains(',') || escaped.Contains('"') || escaped.Contains('\n') || escaped.Contains('\r'))
        {
            escaped = $"\"{escaped}\"";
        }

        return escaped;
    }

    private static T GetMetadataValue<T>(Dictionary<string, object> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var value))
            return default(T)!;

        // Handle JsonElement from deserialized JSON
        if (value is System.Text.Json.JsonElement jsonElement)
        {
            return System.Text.Json.JsonSerializer.Deserialize<T>(jsonElement.GetRawText())!;
        }

        // Handle direct values
        return (T)Convert.ChangeType(value, typeof(T))!;
    }

    private async Task<string> CallGemmaModel(string prompt)
    {
        try
        {
            var apiKey = _configuration["Gemini:ApiKey"];

            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.LogError("Gemini API key missing");
                return "Configuration error: Gemini API key not found.";
            }

            using var httpClient = new HttpClient()
            {
                Timeout = TimeSpan.FromMinutes(3)
            };

            // Use the exact structure from your working curl command
            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                }
            };

            var jsonContent = JsonSerializer.Serialize(requestBody);
            var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            // Add the API key as header (as in your curl command)
            httpClient.DefaultRequestHeaders.Add("x-goog-api-key", apiKey);

            _logger.LogInformation("Calling Google Gemini 2.5 Flash API with prompt length: {PromptLength} characters", prompt.Length);
            _logger.LogDebug("Gemini prompt preview: {PromptPreview}...", prompt.Substring(0, Math.Min(200, prompt.Length)));

            // Use the exact URL from your working curl command
            var url = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent";
            var response = await httpClient.PostAsync(url, httpContent);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var jsonResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);

                // Extract the response content from Gemini API response format
                if (jsonResponse.TryGetProperty("candidates", out var candidates) &&
                    candidates.GetArrayLength() > 0)
                {
                    var firstCandidate = candidates[0];
                    if (firstCandidate.TryGetProperty("content", out var content) &&
                        content.TryGetProperty("parts", out var parts) &&
                        parts.GetArrayLength() > 0)
                    {
                        var firstPart = parts[0];
                        if (firstPart.TryGetProperty("text", out var textElement))
                        {
                            var result = textElement.GetString();
                            _logger.LogInformation("✓ Successfully received response from Google Gemini 2.5 Flash API");
                            return result ?? "";
                        }
                    }
                }

                _logger.LogWarning("⚠️ Unexpected response format from Gemini API");
                _logger.LogDebug("Gemini response: {Response}", responseContent);
                return "";
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("✗ Gemini API error: {StatusCode} - {ErrorContent}", response.StatusCode, errorContent);
                return "";
            }
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogError("✗ Gemini API timeout: {Message}", ex.Message);
            return "Request timeout - the operation took too long. Please try with a shorter document or simpler query.";
        }
        catch (Exception ex)
        {
            _logger.LogError("✗ Error calling Google Gemini API: {Message}", ex.Message);
            return "";
        }
    }

    private async Task<string> SynthesizeAnswerAsync(string query, List<SearchResultDto> searchResults)
    {
        if (!searchResults.Any())
            return "";

        // Clean and combine the top relevant chunks into context
        var contextBuilder = new StringBuilder();

        // Question-first prompt structure for better year accuracy
        contextBuilder.AppendLine("You are a helpful assistant that answers questions based on provided documents.");
        contextBuilder.AppendLine();
        contextBuilder.AppendLine($"QUESTION: {query}");
        contextBuilder.AppendLine();
        contextBuilder.AppendLine("CONTEXT DOCUMENTS:");
        contextBuilder.AppendLine("==================");

        foreach (var result in searchResults.Take(3)) // Use top 3 results for better quality
        {
            // Clean the content to improve readability
            var cleanContent = CleanTextForContext(result.Content);

            contextBuilder.AppendLine($"--- Document: {result.DocumentName} (Page {result.PageNumber}) ---");
            contextBuilder.AppendLine(cleanContent);
            contextBuilder.AppendLine();
        }

        contextBuilder.AppendLine("==================");
        contextBuilder.AppendLine();
        contextBuilder.AppendLine("INSTRUCTIONS:");
        contextBuilder.AppendLine("- Answer the question using ONLY the information provided in the context documents above");
        contextBuilder.AppendLine("- Be specific and cite relevant details from the documents including exact numbers, dates, and percentages");
        contextBuilder.AppendLine("- If the question asks for contents or summary, provide a comprehensive overview of what's in the documents");
        contextBuilder.AppendLine("- If information is missing, state what information is available instead");
        contextBuilder.AppendLine("- When mentioning resolutions, include voting details (shares in favor/against, percentages) if available");
        contextBuilder.AppendLine();
        contextBuilder.AppendLine("ANSWER:");

        var prompt = contextBuilder.ToString();
        return await CallGemmaModel(prompt);
    }

    private string CleanTextForContext(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Apply the same text spacing fix used in PDF processing
        var cleanedText = FixTextSpacing(text);

        // Additional cleanup for context
        cleanedText = cleanedText.Replace("\r\n\r\n", "\n\n"); // Normalize line breaks
        cleanedText = cleanedText.Replace("\r\n", "\n");
        cleanedText = System.Text.RegularExpressions.Regex.Replace(cleanedText, @"\n{3,}", "\n\n"); // Limit excessive newlines

        return cleanedText;
    }

    private string FixTextSpacing(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var result = new StringBuilder();
        bool lastWasLetter = false;
        bool lastWasDigit = false;

        for (int i = 0; i < text.Length; i++)
        {
            char current = text[i];

            // Add space before uppercase letters that follow lowercase letters or digits
            if (char.IsUpper(current) && (lastWasLetter || lastWasDigit) &&
                result.Length > 0 && result[result.Length - 1] != ' ' && result[result.Length - 1] != '\n')
            {
                // Check if this might be an acronym (multiple consecutive uppercase letters)
                bool isAcronym = i + 1 < text.Length && char.IsUpper(text[i + 1]);
                if (!isAcronym)
                {
                    result.Append(' ');
                }
            }

            // Add space before digits that follow letters
            else if (char.IsDigit(current) && lastWasLetter &&
                     result.Length > 0 && result[result.Length - 1] != ' ' && result[result.Length - 1] != '\n')
            {
                result.Append(' ');
            }

            // Add space before letters that follow digits
            else if (char.IsLetter(current) && lastWasDigit &&
                     result.Length > 0 && result[result.Length - 1] != ' ' && result[result.Length - 1] != '\n')
            {
                result.Append(' ');
            }

            result.Append(current);

            lastWasLetter = char.IsLetter(current);
            lastWasDigit = char.IsDigit(current);
        }

        // Apply additional aggressive spacing fixes for common concatenated patterns
        var resultText = result.ToString();

        // Fix specific patterns like "rviceAPI" -> "rvice API", "workpathfolder" -> "workpath folder"
        resultText = System.Text.RegularExpressions.Regex.Replace(resultText, @"([a-z])([A-Z][a-z])", "$1 $2");

        // Fix number-letter concatenations
        resultText = System.Text.RegularExpressions.Regex.Replace(resultText, @"(\d)([A-Za-z])", "$1 $2");
        resultText = System.Text.RegularExpressions.Regex.Replace(resultText, @"([A-Za-z])(\d)", "$1 $2");

        // Fix common word boundaries that got lost
        resultText = System.Text.RegularExpressions.Regex.Replace(resultText, @"([a-z])(The|And|Or|In|On|At|To|For|By|Of|With|From)", " $2", RegexOptions.IgnoreCase);

        // Fix "bydefault" -> "by default", "inthe" -> "in the", etc.
        var commonWords = new Dictionary<string, string>
        {
            { "bydefault", "by default" },
            { "inthe", "in the" },
            { "onthe", "on the" },
            { "forthe", "for the" },
            { "tothe", "to the" },
            { "andthe", "and the" },
            { "ofthe", "of the" },
            { "withthe", "with the" },
            { "fromthe", "from the" },
            { "thatyou", "that you" },
            { "ifyou", "if you" },
            { "whenyou", "when you" },
            { "canbereplacedby", "can be replaced by" },
            { "willbeforwarded", "will be forwarded" },
            { "shouldhave", "should have" },
            { "mustbe", "must be" },
            { "canbe", "can be" }
        };

        foreach (var fix in commonWords)
        {
            resultText = System.Text.RegularExpressions.Regex.Replace(resultText, fix.Key, fix.Value, RegexOptions.IgnoreCase);
        }

        // Clean up multiple spaces
        resultText = System.Text.RegularExpressions.Regex.Replace(resultText, @"\s+", " ");

        return resultText.Trim();
    }

    /// <summary>
    /// Enhance query with context and improve semantic understanding
    /// </summary>
    private async Task<string> EnhanceQueryAsync(string originalQuery)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(originalQuery))
                return originalQuery;

            // Clean and normalize the query
            var cleanQuery = originalQuery.Trim();

            // Add context hints based on query type
            var enhancedQuery = new StringBuilder(cleanQuery);

            // Add semantic context for better embedding
            if (cleanQuery.Contains("what") || cleanQuery.Contains("how") || cleanQuery.Contains("why"))
            {
                enhancedQuery.Append(" definition explanation details");
            }

            if (cleanQuery.Contains("when") || cleanQuery.Contains("date") || cleanQuery.Contains("time"))
            {
                enhancedQuery.Append(" time period date schedule");
            }

            if (cleanQuery.Contains("where") || cleanQuery.Contains("location"))
            {
                enhancedQuery.Append(" location place address");
            }

            if (cleanQuery.Contains("number") || cleanQuery.Contains("amount") || cleanQuery.Contains("count"))
            {
                enhancedQuery.Append(" quantity statistics data numbers");
            }

            return enhancedQuery.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enhance query: {Query}", originalQuery);
            return originalQuery;
        }
    }

    /// <summary>
    /// Extract important keywords from query for hybrid scoring
    /// </summary>
    private List<string> ExtractKeywords(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<string>();

        // Simple keyword extraction - in production, consider using NLP libraries
        var words = query.ToLowerInvariant()
            .Split(new[] { ' ', ',', '.', '!', '?', ';', ':', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2) // Filter short words
            .Where(w => !IsStopWord(w)) // Filter stop words
            .Distinct()
            .ToList();

        return words;
    }

    /// <summary>
    /// Check if word is a stop word
    /// </summary>
    private bool IsStopWord(string word)
    {
        var stopWords = new HashSet<string>
        {
            "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for", "of", "with", "by",
            "from", "is", "are", "was", "were", "be", "been", "have", "has", "had", "do", "does", "did",
            "will", "would", "could", "should", "may", "might", "can", "this", "that", "these", "those"
        };

        return stopWords.Contains(word.ToLowerInvariant());
    }

    /// <summary>
    /// Calculate hybrid score combining semantic similarity with keyword matching and context factors
    /// </summary>
    private float CalculateHybridScore(float semanticScore, string content, string query, List<string> queryKeywords, dynamic chunk)
    {
        var contentLower = content.ToLowerInvariant();
        var queryLower = query.ToLowerInvariant();

        // Base semantic score (weight: 0.6)
        var baseScore = semanticScore * 0.6f;

        // Keyword matching score (weight: 0.25)
        var keywordScore = CalculateKeywordScore(contentLower, queryKeywords) * 0.25f;

        // Exact phrase matching boost (weight: 0.1)
        var phraseScore = CalculatePhraseScore(contentLower, queryLower) * 0.1f;

        // Context factors (weight: 0.05)
        var contextScore = CalculateContextScore(chunk) * 0.05f;

        var totalScore = baseScore + keywordScore + phraseScore + contextScore;

        // Ensure score doesn't exceed 1.0
        return Math.Min(totalScore, 1.0f);
    }

    /// <summary>
    /// Calculate keyword matching score
    /// </summary>
    private float CalculateKeywordScore(string content, List<string> keywords)
    {
        if (!keywords.Any())
            return 0f;

        var matchCount = 0f; // Use float instead of int
        var totalKeywords = keywords.Count;

        foreach (var keyword in keywords)
        {
            if (content.Contains(keyword))
            {
                matchCount++;

                // Bonus for exact word boundary matches
                if (Regex.IsMatch(content, $@"\b{Regex.Escape(keyword)}\b", RegexOptions.IgnoreCase))
                {
                    matchCount += 0.5f; // Bonus for exact word match
                }
            }
        }

        return Math.Min(matchCount / totalKeywords, 1.0f);
    }

    /// <summary>
    /// Calculate exact phrase matching score
    /// </summary>
    private float CalculatePhraseScore(string content, string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 3)
            return 0f;

        // Check for exact phrase match
        if (content.Contains(query))
            return 1.0f;

        // Check for partial phrase matches (sequences of 3+ words)
        var queryWords = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (queryWords.Length < 3)
            return 0f;

        var maxPhraseScore = 0f;
        for (int i = 0; i <= queryWords.Length - 3; i++)
        {
            var phrase = string.Join(" ", queryWords.Skip(i).Take(3));
            if (content.Contains(phrase))
            {
                maxPhraseScore = Math.Max(maxPhraseScore, 0.7f);
            }
        }

        return maxPhraseScore;
    }

    /// <summary>
    /// Calculate context-based scoring factors
    /// </summary>
    private float CalculateContextScore(dynamic chunk)
    {
        var score = 0f;

        try
        {
            // Favor chunks with more content (but not too long)
            var tokenCount = (int)chunk.TokenCount;
            if (tokenCount >= 50 && tokenCount <= 500)
                score += 0.3f;
            else if (tokenCount > 20)
                score += 0.1f;

            // Slight preference for earlier pages (assuming they might contain more important content)
            var pageNumber = (int?)chunk.PageNumber ?? 1;
            if (pageNumber <= 3)
                score += 0.2f;
            else if (pageNumber <= 10)
                score += 0.1f;

            // Favor more recent documents slightly
            if (chunk.Document?.UploadedAt != null)
            {
                var uploadDate = (DateTime)chunk.Document.UploadedAt;
                var daysSinceUpload = (DateTime.Now - uploadDate).TotalDays;
                if (daysSinceUpload <= 7)
                    score += 0.1f;
                else if (daysSinceUpload <= 30)
                    score += 0.05f;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error calculating context score for chunk");
        }

        return Math.Min(score, 1.0f);
    }

    /// <summary>
    /// Apply advanced ranking with diversity filtering and result optimization
    /// </summary>
    private List<SearchResultDto> ApplyAdvancedRanking(List<SearchResultDto> results, SearchRequestDto request)
    {
        if (!results.Any())
            return results;

        // Primary sort by relevance score
        var sortedResults = results.OrderByDescending(r => r.RelevanceScore).ToList();

        // Apply diversity filtering to avoid too many results from the same document
        var diversifiedResults = new List<SearchResultDto>();
        var documentChunkCounts = new Dictionary<Guid, int>();
        var maxChunksPerDocument = Math.Max(1, request.TopK / 3); // Allow up to 1/3 of results from same document

        foreach (var result in sortedResults)
        {
            var currentCount = documentChunkCounts.GetValueOrDefault(result.DocumentId, 0);

            if (currentCount < maxChunksPerDocument || diversifiedResults.Count < request.TopK / 2)
            {
                diversifiedResults.Add(result);
                documentChunkCounts[result.DocumentId] = currentCount + 1;
            }

            if (diversifiedResults.Count >= request.TopK * 2) // Get extra for final filtering
                break;
        }

        // Final sort to ensure best results are at the top
        return diversifiedResults
            .OrderByDescending(r => r.RelevanceScore)
            .ToList();
    }

    /// <summary>
    /// Enhance search results with surrounding context from adjacent chunks
    /// </summary>
    private async Task<List<SearchResultDto>> EnhanceResultsWithContext(List<SearchResultDto> results)
    {
        if (!results.Any())
            return results;

        try
        {
            var enhancedResults = new List<SearchResultDto>();

            foreach (var result in results)
            {
                // Get surrounding chunks for better context
                var adjacentChunks = await _context.DocumentChunks
                    .Where(c => c.DocumentId == result.DocumentId &&
                               c.ChunkSequence >= result.ChunkSequence - 1 &&
                               c.ChunkSequence <= result.ChunkSequence + 1)
                    .OrderBy(c => c.ChunkSequence)
                    .Select(c => new { c.Content, c.ChunkSequence })
                    .ToListAsync();

                var enhancedContent = new StringBuilder();

                foreach (var chunk in adjacentChunks)
                {
                    if (chunk.ChunkSequence == result.ChunkSequence)
                    {
                        // Mark the main chunk
                        enhancedContent.AppendLine($"[MAIN RESULT] {chunk.Content}");
                    }
                    else
                    {
                        // Add context chunks
                        enhancedContent.AppendLine($"[CONTEXT] {chunk.Content.Substring(0, Math.Min(200, chunk.Content.Length))}...");
                    }
                }

                // Create enhanced result
                var enhancedResult = new SearchResultDto
                {
                    ChunkId = result.ChunkId,
                    DocumentId = result.DocumentId,
                    DocumentName = result.DocumentName,
                    Content = enhancedContent.ToString(),
                    RelevanceScore = result.RelevanceScore,
                    PageNumber = result.PageNumber,
                    ChunkSequence = result.ChunkSequence,
                    Metadata = result.Metadata
                };

                enhancedResults.Add(enhancedResult);
            }

            return enhancedResults;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enhance results with context, returning original results");
            return results;
        }
    }
}
