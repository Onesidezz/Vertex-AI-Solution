using DocumentProcessingAPI.Core.DTOs;
using DocumentProcessingAPI.Core.Entities;
using DocumentProcessingAPI.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.RegularExpressions;

namespace DocumentProcessingAPI.Infrastructure.Services
{
    /// <summary>
    /// AI-powered record service using local Ollama Gemma 7B for summary and Q&A functionality
    /// Provides intelligent analysis of Content Manager records
    /// </summary>
    public class AIRecordService : IAIRecordService
    {
        private readonly ContentManagerServices _contentManagerServices;
        private readonly PgVectorService _pgVectorService;
        private readonly IRecordSearchGoogleServices _googleServices;
        private readonly IDocumentProcessor _documentProcessor;
        private readonly IEmbeddingService _embeddingService;
        private readonly ILogger<AIRecordService> _logger;

        public AIRecordService(
            ContentManagerServices contentManagerServices,
            PgVectorService pgVectorService,
            IRecordSearchGoogleServices googleServices,
            IDocumentProcessor documentProcessor,
            IEmbeddingService embeddingService,
            ILogger<AIRecordService> logger)
        {
            _contentManagerServices = contentManagerServices;
            _pgVectorService = pgVectorService;
            _googleServices = googleServices;
            _documentProcessor = documentProcessor;
            _embeddingService = embeddingService;
            _logger = logger;
        }

        /// <summary>
        /// Generate an AI summary of a record using local Ollama Gemma 7B
        /// </summary>
        public async Task<string> GetRecordSummaryAsync(long recordUri)
        {
            try
            {
                _logger.LogInformation("🤖 Generating AI summary for record URI: {RecordUri}", recordUri);

                // Get record embeddings from PostgreSQL
                var embeddings = await _pgVectorService.GetPointIdsByRecordUriAsync(recordUri);

                if (!embeddings.Any())
                {
                    _logger.LogWarning("⚠️ No embeddings found for record URI: {RecordUri}", recordUri);
                    return "No data found for this record. Please ensure the record has been processed and embedded.";
                }

                // Get the first embedding to extract metadata
                var firstEmbedding = await _pgVectorService.GetEmbeddingAsync(embeddings.First());

                if (firstEmbedding == null)
                {
                    return "Unable to retrieve record data.";
                }

                var metadata = firstEmbedding.Value.metadata;

                // Build comprehensive record context
                var contextBuilder = new StringBuilder();
                contextBuilder.AppendLine("Generate a comprehensive summary of this Content Manager record.");
                contextBuilder.AppendLine();
                contextBuilder.AppendLine("RECORD INFORMATION:");
                contextBuilder.AppendLine("==================");
                contextBuilder.AppendLine($"Record URI: {GetMetadataValue(metadata, "record_uri", recordUri)}");
                contextBuilder.AppendLine($"Title: {GetMetadataValue(metadata, "record_title", "Unknown")}");
                contextBuilder.AppendLine($"Date Created: {GetMetadataValue(metadata, "date_created", "Unknown")}");
                contextBuilder.AppendLine($"Record Type: {GetMetadataValue(metadata, "record_type", "Unknown")}");
                contextBuilder.AppendLine($"Container: {GetMetadataValue(metadata, "container", "N/A")}");
                contextBuilder.AppendLine($"Assignee: {GetMetadataValue(metadata, "assignee", "N/A")}");
                contextBuilder.AppendLine($"File Type: {GetMetadataValue(metadata, "file_type", "N/A")}");
                contextBuilder.AppendLine();

                // Collect all chunk content
                contextBuilder.AppendLine("CONTENT:");
                contextBuilder.AppendLine("==================");

                var allChunks = new List<string>();
                foreach (var embeddingId in embeddings.Take(50)) // Limit to first 50 chunks
                {
                    var embedding = await _pgVectorService.GetEmbeddingAsync(embeddingId);
                    if (embedding.HasValue && embedding.Value.metadata.ContainsKey("chunk_content"))
                    {
                        var chunkContent = embedding.Value.metadata["chunk_content"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(chunkContent))
                        {
                            allChunks.Add(chunkContent);
                        }
                    }
                }

                // Combine all content (limit total size)
                var fullContent = string.Join("\n\n", allChunks);
                if (fullContent.Length > 50000)
                {
                    fullContent = fullContent.Substring(0, 50000) + "\n\n... [Content truncated for length]";
                }

                contextBuilder.AppendLine(fullContent);
                contextBuilder.AppendLine();
                contextBuilder.AppendLine("==================");
                contextBuilder.AppendLine();
                contextBuilder.AppendLine("INSTRUCTIONS:");
                contextBuilder.AppendLine("[Classification Type: TechnicalDoc|Invoice|Contract|Report|Resume|LegalBrief|FinancialStatement|PolicyDocument|MeetingMinutes|ResearchPaper|Unknown]");
                contextBuilder.AppendLine();
                contextBuilder.AppendLine("Provide a clear and concise summary (2-4 sentences) that includes:");
                contextBuilder.AppendLine("1. What type of document this is (classification)");
                contextBuilder.AppendLine("2. The main purpose or topic");
                contextBuilder.AppendLine("3. Key entities, dates, or amounts (if applicable)");
                contextBuilder.AppendLine("4. Most important information someone should know");
                contextBuilder.AppendLine();
                contextBuilder.AppendLine("Be direct and factual. Focus on what's actually in the document.");
                contextBuilder.AppendLine();
                contextBuilder.AppendLine("SUMMARY:");

                var prompt = contextBuilder.ToString();
                var summary = await _googleServices.CallGeminiModelAsync(prompt, maxOutputTokens: 2000);

                if (string.IsNullOrWhiteSpace(summary))
                {
                    return "Unable to generate summary. Please try again.";
                }

                _logger.LogInformation("✅ Successfully generated summary for record URI: {RecordUri}", recordUri);
                return summary;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to generate summary for record URI: {RecordUri}", recordUri);
                return $"Error generating summary: {ex.Message}";
            }
        }

        /// <summary>
        /// Ask a question about a specific record using local Ollama Gemma 7B with semantic search + context window approach
        /// Uses semantic search to find the most relevant chunk (1 embedding generation per question)
        /// Then retrieves surrounding context (3 chunks before/after based on ChunkSequence)
        /// </summary>
        public async Task<string> AskAboutRecordAsync(long recordUri, string question)
        {
            try
            {
                _logger.LogInformation("🤖 Processing AI question for record URI: {RecordUri}", recordUri);
                _logger.LogInformation("   Question: {Question}", question);

                if (string.IsNullOrWhiteSpace(question))
                {
                    return "Please provide a question.";
                }

                // STEP 1: Preprocess question → strip question words so the embedding focuses
                // on the TOPIC, not the question structure.
                // e.g. "what is the discussion about $20,000" → "$20,000"
                // e.g. "who approved the merger"             → "merger"
                // e.g. "what happened with the contract"     → "contract"
                var embeddingQuery = PreprocessQueryForEmbedding(question);
                _logger.LogInformation("   🔍 Embedding query (preprocessed): '{EmbeddingQuery}'", embeddingQuery);
                var questionEmbedding = await _embeddingService.GenerateEmbeddingAsync(embeddingQuery);

                // STEP 2: Hybrid search — sequential to avoid EF Core DbContext concurrency error
                // (DbContext is NOT thread-safe; Task.WhenAll on same context causes InvalidOperationException)
                _logger.LogInformation("   🎯 Running hybrid search (keyword first, then semantic)");

                // Keyword search first — literal match, highest priority for specific terms/numbers
                var keywordChunks = await _pgVectorService.FindTopChunksByKeywordAsync(
                    question, recordUri, topN: 5);

                // Semantic search second — topic-focused embedding
                var semanticChunks = await _pgVectorService.FindTopRelevantChunksAsync(
                    questionEmbedding, recordUri, topN: 5, threshold: 0.2f);

                _logger.LogInformation("   📊 Keyword hits: {K}, Semantic hits: {S}",
                    keywordChunks.Count, semanticChunks.Count);

                // Merge: keyword results first (exact literal match = highest confidence),
                // then append semantic results not already present
                var seenIds   = new HashSet<long>(keywordChunks.Select(c => c.Id));
                var topChunks = keywordChunks.ToList();
                foreach (var c in semanticChunks)
                {
                    if (seenIds.Add(c.Id))
                        topChunks.Add(c);
                }
                topChunks = topChunks.Take(5).ToList(); // cap at 5 anchor chunks

                if (!topChunks.Any())
                {
                    _logger.LogWarning("⚠️ No relevant content found for question");
                    return "No relevant information found for your question in this record. Please try rephrasing your question.";
                }

                var primaryChunk = topChunks.First();

                // STEP 3: Log top chunks found
                _logger.LogInformation("   📋 Top {Count} anchor chunks:", topChunks.Count);
                foreach (var tc in topChunks)
                    _logger.LogInformation("      Seq={Seq}, Page={Page}", tc.ChunkSequence, tc.PageNumber);

                // STEP 4: Fetch ±2 chunks around each anchor chunk INDIVIDUALLY and union them.
                // Do NOT use a single merged min→max range — that pulls in every chunk
                // between distant results (e.g. seq 18 and seq 59 → 41 irrelevant chunks).
                var seenContextIds = new HashSet<long>();
                var contextChunks  = new List<Embedding>();

                foreach (var tc in topChunks)
                {
                    var winMin = Math.Max(0, tc.ChunkSequence - 2);
                    var winMax = tc.ChunkSequence + 2;
                    var window = await _pgVectorService.GetChunksBySequenceRangeAsync(
                        recordUri, winMin, winMax);
                    foreach (var c in window.Where(c => seenContextIds.Add(c.Id)))
                        contextChunks.Add(c);
                }

                // Sort by sequence so the AI reads in document order; cap at 15 chunks
                // Token-budget-aware selection: fill up to the available context window.
                // Budget = numCtx(32768) - maxOutputTokens(2000) - promptOverhead(~700) = ~30068 tokens
                // 1 token ≈ 4 chars  →  content budget ≈ 30068 × 4 = 120 272 chars
                const int NumCtx           = 32768;
                const int MaxOutputTokens  = 2000;
                const int PromptOverhead   = 700;   // header + metadata + instructions
                const int CharsPerToken    = 4;
                int contentBudgetChars = (NumCtx - MaxOutputTokens - PromptOverhead) * CharsPerToken;

                var budgetedChunks = new List<Embedding>();
                int usedChars = 0;
                foreach (var c in contextChunks.OrderBy(x => x.ChunkSequence))
                {
                    var len = c.ChunkContent?.Length ?? 0;
                    if (usedChars + len > contentBudgetChars) break;
                    budgetedChunks.Add(c);
                    usedChars += len;
                }
                contextChunks = budgetedChunks;

                _logger.LogInformation("   📐 Content budget: {Budget} chars, using {Used} chars across {Count} chunks",
                    contentBudgetChars, usedChars, contextChunks.Count);

                if (!contextChunks.Any())
                {
                    _logger.LogWarning("⚠️ No context chunks retrieved for top anchors");
                    return "Unable to retrieve document content. Please try again.";
                }

                // STEP 6: Build the prompt with metadata and context
                var topChunkIds = new HashSet<long>(topChunks.Select(tc => tc.Id));
                var contextBuilder = new StringBuilder();
                contextBuilder.AppendLine("You are a helpful AI assistant answering questions about a Content Manager record.");
                contextBuilder.AppendLine();
                contextBuilder.AppendLine($"USER QUESTION: {question}");
                contextBuilder.AppendLine();
                contextBuilder.AppendLine("RECORD INFORMATION:");
                contextBuilder.AppendLine("==================");
                contextBuilder.AppendLine($"Record URI: {primaryChunk.RecordUri}");
                contextBuilder.AppendLine($"Title: {primaryChunk.RecordTitle ?? "Unknown"}");
                contextBuilder.AppendLine($"Date Created: {primaryChunk.DateCreated?.ToString("yyyy-MM-dd") ?? "Unknown"}");
                contextBuilder.AppendLine($"Record Type: {primaryChunk.RecordType ?? "Unknown"}");
                contextBuilder.AppendLine($"Container: {primaryChunk.Container ?? "N/A"}");
                contextBuilder.AppendLine($"Assignee: {primaryChunk.Assignee ?? "N/A"}");
                contextBuilder.AppendLine();

                // STEP 7: Add document content with merged context window
                contextBuilder.AppendLine("DOCUMENT CONTENT (Multi-Section Context):");
                contextBuilder.AppendLine("==================");
                contextBuilder.AppendLine($"Note: Showing {contextChunks.Count} chunks covering {topChunks.Count} most relevant sections.");
                contextBuilder.AppendLine($"Sections marked [RELEVANT] were identified as most relevant to your question.");
                contextBuilder.AppendLine();

                // Add chunks in order, highlighting the top relevant ones
                foreach (var chunk in contextChunks)
                {
                    var isRelevant = topChunkIds.Contains(chunk.Id);

                    if (isRelevant)
                    {
                        contextBuilder.AppendLine(">>> RELEVANT SECTION <<<");
                    }

                    contextBuilder.AppendLine($"[Sequence {chunk.ChunkSequence}, Page {chunk.PageNumber}, Index {chunk.ChunkIndex}]");
                    contextBuilder.AppendLine(chunk.ChunkContent);
                    contextBuilder.AppendLine();

                    if (isRelevant)
                    {
                        contextBuilder.AppendLine(">>> END RELEVANT SECTION <<<");
                        contextBuilder.AppendLine();
                    }
                }

                contextBuilder.AppendLine("==================");
                contextBuilder.AppendLine();
                contextBuilder.AppendLine("INSTRUCTIONS:");
                contextBuilder.AppendLine("1. Answer ONLY using facts explicitly stated in the document content above");
                contextBuilder.AppendLine("2. Focus on the sections marked >>> RELEVANT SECTION <<<");
                contextBuilder.AppendLine("3. Be specific — quote exact values, names, or figures from the document");
                contextBuilder.AppendLine("4. NEVER guess, approximate, or infer information not directly stated");
                contextBuilder.AppendLine("5. If the answer is not present in the content, say exactly: 'This information is not found in the document.'");
                contextBuilder.AppendLine("6. Keep your answer concise (2-4 sentences)");
                contextBuilder.AppendLine();
                contextBuilder.AppendLine("ANSWER:");

                var prompt = contextBuilder.ToString();

                int promptTokens = prompt.Length / CharsPerToken;
                _logger.LogInformation("   📊 Context stats: {ChunkCount} chunks, {CharCount} chars, ~{TokenCount}/{NumCtx} tokens",
                    contextChunks.Count, prompt.Length, promptTokens, NumCtx);

                // STEP 8: Call model with explicit context window size
                var answer = await _googleServices.CallGeminiModelAsync(
                    prompt, maxOutputTokens: MaxOutputTokens, numCtx: NumCtx);

                if (string.IsNullOrWhiteSpace(answer))
                {
                    return "Unable to generate an answer. Please try again.";
                }

                _logger.LogInformation("✅ Successfully generated answer for record URI: {RecordUri}", recordUri);
                return answer;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to generate answer for record URI: {RecordUri}", recordUri);
                return $"Error generating answer: {ex.Message}";
            }
        }

        /// <summary>
        /// Strips question words from the front of a query so the embedding focuses
        /// on the actual topic. Works for any dynamic content type.
        /// Examples:
        /// Standard English function words (question words, articles, prepositions,
        /// auxiliary verbs, pronouns, conjunctions) that carry no topic meaning.
        /// Keeping only content words makes embeddings topic-focused, not question-structure-focused.
        private static readonly HashSet<string> _functionWords = new(StringComparer.OrdinalIgnoreCase)
        {
            // Question / WH words
            "what","who","whose","whom","which","where","when","why","how",
            // Articles
            "a","an","the",
            // Prepositions
            "about","above","across","after","against","along","among","around","at",
            "before","behind","below","beneath","beside","between","beyond","by",
            "despite","down","during","except","for","from","in","inside","into",
            "like","near","of","off","on","onto","out","outside","over","past",
            "since","than","through","throughout","till","to","toward","under",
            "underneath","until","up","upon","with","within","without",
            // Auxiliary / modal verbs
            "is","are","was","were","be","been","being","am",
            "has","have","had","do","does","did",
            "will","would","could","should","may","might","shall","can",
            // Pronouns
            "i","me","my","myself","we","our","ours","ourselves",
            "you","your","yours","yourself","yourselves",
            "he","him","his","himself","she","her","hers","herself",
            "it","its","itself","they","them","their","theirs","themselves",
            // Demonstratives
            "this","that","these","those",
            // Conjunctions / connectors
            "and","but","or","nor","so","yet","both","either","neither","not",
            "if","while","although","though","because","since","unless","whether",
            // Common question-filler verbs (the "ask verb", not the topic verb)
            "tell","give","show","find","explain","describe","summarize","list",
            "please","can","could","would","get","make",
            // Misc function words
            "very","just","also","too","only","again","here","there","then","once",
            "further","any","all","each","every","few","more","most","other",
            "some","such","no","much","many","same","own","long","often","already",
        };

        /// <summary>
        /// Removes function words from the question, keeping only content words
        /// (nouns, content verbs, adjectives, numbers, names, symbols).
        /// This is dynamic — no hardcoded patterns — works for any question.
        ///
        /// Examples:
        ///   "what is the discussion about $20,000"      → "discussion $20,000"
        ///   "who approved the merger deal"              → "approved merger deal"
        ///   "what happened with the annual leave policy"→ "happened annual leave policy"
        ///   "how much was the total contract value"     → "total contract value"
        ///   "tell me about the safety incident report"  → "safety incident report"
        /// </summary>
        private static string PreprocessQueryForEmbedding(string question)
        {
            var tokens = question.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var contentTokens = tokens.Where(token =>
            {
                // Strip punctuation to get the bare word for stopword lookup,
                // but always KEEP tokens that are mostly non-alphabetic (numbers, $20,000, codes, dates)
                var letters = Regex.Replace(token, @"[^a-zA-Z]", "");
                if (letters.Length == 0) return true;               // pure symbol/number → always keep
                if (letters.Length < token.Length * 0.5) return true; // mostly numeric/symbol → keep
                return !_functionWords.Contains(letters);            // keep if not a function word
            }).ToList();

            var result = string.Join(" ", contentTokens).Trim();

            // Safety: if stripping removed almost everything, fall back to original
            return result.Length > 2 ? result : question;
        }

        /// <summary>
        /// Helper method to safely get metadata values
        /// </summary>
        private T GetMetadataValue<T>(Dictionary<string, object> metadata, string key, T defaultValue)
        {
            if (metadata.TryGetValue(key, out var value) && value != null)
            {
                try
                {
                    return (T)Convert.ChangeType(value, typeof(T));
                }
                catch
                {
                    return defaultValue;
                }
            }
            return defaultValue;
        }
    }
}
