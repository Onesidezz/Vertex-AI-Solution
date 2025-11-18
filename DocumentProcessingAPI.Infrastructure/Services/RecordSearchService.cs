using DocumentProcessingAPI.Core.DTOs;
using DocumentProcessingAPI.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using TRIM.SDK;

namespace DocumentProcessingAPI.Infrastructure.Services
{
    /// <summary>
    /// Service for searching Content Manager records using semantic embeddings
    /// Supports natural language queries including metadata field searches
    /// </summary>
    public class RecordSearchService : IRecordSearchService
    {
        private readonly IEmbeddingService _embeddingService;
        private readonly PgVectorService _pgVectorService;
        private readonly ILogger<RecordSearchService> _logger;
        private readonly IConfiguration _configuration;
        private readonly ContentManagerServices _contentManagerServices;
        private readonly IRecordSearchHelperServices _helperServices;
        private readonly IRecordSearchGoogleServices _googleServices;

        public RecordSearchService(
            IEmbeddingService embeddingService,
            PgVectorService pgVectorService,
            ILogger<RecordSearchService> logger,
            IConfiguration configuration,
            ContentManagerServices contentManagerServices,
            IRecordSearchHelperServices helperServices,
            IRecordSearchGoogleServices googleServices)
        {
            _embeddingService = embeddingService;
            _pgVectorService = pgVectorService;
            _logger = logger;
            _configuration = configuration;
            _contentManagerServices = contentManagerServices;
            _helperServices = helperServices;
            _googleServices = googleServices;
        }


        public async Task<RecordSearchResponseDto> SearchRecordsAsync(
            string query,
            Dictionary<string, object>? metadataFilters = null,
            int topK = 20,
            float minimumScore = 0.3f,
            bool useAdvancedFilter = false,
            string? uri = null,
            string? clientId = null,
            string? title = null,
            DateTime? dateFrom = null,
            DateTime? dateTo = null,
            string? contentSearch = null)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Input validation
                if (string.IsNullOrWhiteSpace(query) && !useAdvancedFilter)
                {
                    _logger.LogWarning("Empty or null query provided and no advanced filters");
                    return new RecordSearchResponseDto
                    {
                        Query = query ?? "",
                        Results = new List<RecordSearchResultDto>(),
                        TotalResults = 0,
                        QueryTime = 0,
                        SynthesizedAnswer = "Please provide a search query or use advanced filters."
                    };
                }

                // Check if using advanced filters only (no semantic search needed)
                if (useAdvancedFilter)
                {
                    // Check if any advanced filter input is provided
                    bool hasAdvancedFilterInputs = !string.IsNullOrWhiteSpace(uri) ||
                                                   !string.IsNullOrWhiteSpace(clientId) ||
                                                   !string.IsNullOrWhiteSpace(title) ||
                                                   dateFrom.HasValue ||
                                                   dateTo.HasValue ||
                                                   !string.IsNullOrWhiteSpace(contentSearch);

                    if (hasAdvancedFilterInputs)
                    {
                        // Route to dedicated advanced filter search function
                        _logger.LogInformation("Routing to advanced filter search (no semantic search)");
                        return await _contentManagerServices.ExecuteContentManagerAdvanceFilterAsync(
                            uri,
                            clientId,
                            title,
                            dateFrom,
                            dateTo,
                            contentSearch,
                            topK);
                    }
                }

                // If using advanced filter only, set query to a default value for logging
                if (string.IsNullOrWhiteSpace(query) && useAdvancedFilter)
                {
                    query = "[Advanced Filter Search]";
                    _logger.LogInformation("Using advanced filter only mode");
                }

                // Validate and normalize parameters
                topK = Math.Max(1, Math.Min(100, topK)); // Clamp between 1-100
                minimumScore = Math.Max(0.0f, Math.Min(1.0f, minimumScore)); // Clamp between 0-1

                _logger.LogInformation("========== HYBRID SEARCH ARCHITECTURE ==========");
                _logger.LogInformation("Searching Content Manager records for query: {Query} (TopK: {TopK}, MinScore: {MinScore})",
                    query, topK, minimumScore);

                // ============================================================
                // STEP 1: QUERY ANALYSIS & PREPARATION
                // ============================================================
                _logger.LogInformation("🔍 STEP 1: Query Analysis & Preparation");

                // Clean and normalize query
                var cleanQuery = _helperServices.CleanAndNormalizeQuery(query);
                _logger.LogInformation("   Original Query: {OriginalQuery}", query);
                _logger.LogInformation("   Normalized Query: {CleanQuery}", cleanQuery);

                // Extract date filter from query if present
                var (startDate, endDate) = _helperServices.ExtractDateRangeFromQuery(cleanQuery);
                if (startDate.HasValue || endDate.HasValue)
                {
                    _logger.LogInformation("   ✅ Date filter extracted:");
                    _logger.LogInformation("      Start Date: {StartDate}", startDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "null");
                    _logger.LogInformation("      End Date: {EndDate}", endDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "null");
                }
                else
                {
                    _logger.LogInformation("   ℹ️ No date filter found in query");
                }

                // Extract file type filters
                var fileTypeFilters = _helperServices.ExtractFileTypeFilters(cleanQuery);
                if (fileTypeFilters.Any())
                {
                    _logger.LogInformation("   ✅ File type filters: {FileTypes}", string.Join(", ", fileTypeFilters));
                }

                // Extract sorting intent
                var (isEarliest, isLatest) = _helperServices.ExtractSortingIntent(cleanQuery);
                if (isEarliest || isLatest)
                {
                    _logger.LogInformation("   ✅ Sorting intent: Earliest={IsEarliest}, Latest={IsLatest}",
                        isEarliest, isLatest);
                }

                // ============================================================
                // STEP 1.5: SMART KEYWORD EXTRACTION (LOCAL - NO API COST)
                // For fallback if Gemini extraction fails
                // ============================================================
                _logger.LogInformation("🔍 STEP 1.5: Extracting Keywords (Smart Local Extraction)");

                // Extract keywords locally without Gemini API call to save costs
                // These are used as fallback if Gemini keyword extraction fails
                var extractedKeywords = _helperServices.ExtractSmartKeywords(query);

                if (extractedKeywords.Any())
                {
                    _logger.LogInformation("   ✅ Extracted keywords: {Keywords}",
                        string.Join(", ", extractedKeywords));
                }
                else
                {
                    _logger.LogInformation("   ℹ️ No specific keywords extracted");
                }

                // ============================================================
                // STEP 2: SEMANTIC SEARCH WITH PGVECTOR (Embeddings-based search)
                // ============================================================
                _logger.LogInformation("🔍 STEP 2: Semantic Search with pgvector");

                // Dynamic search limit calculation
                var searchLimit = _helperServices.CalculateDynamicSearchLimit(topK, isEarliest, isLatest, startDate, endDate,
                    fileTypeFilters.Count, 0);

                var adjustedMinScore = _helperServices.CalculateDynamicMinimumScore(minimumScore, cleanQuery, new List<string>());

                _logger.LogInformation("   📊 Search Parameters: Limit={SearchLimit}, MinScore={AdjustedMinScore}",
                    searchLimit, adjustedMinScore);

                // Generate embedding for the semantic query
                _logger.LogInformation("   📝 Generating embedding for semantic query");
                var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(cleanQuery);

                // ============================================================
                // STEP 2.5: GEMINI CONTEXT EXTRACTION (FOR VECTOR BOOST ONLY)
                // ============================================================
                _logger.LogInformation("   🤖 Extracting context with Gemini for vector search boosting");

                List<string> geminiContextKeywords = new List<string>();
                try
                {
                    // Use Gemini to extract contextual keywords for intelligent vector boosting
                    // This is different from local extraction - Gemini understands intent and context
                    geminiContextKeywords = await _googleServices.ExtractKeywordsWithGemini(cleanQuery);

                    if (geminiContextKeywords.Any())
                    {
                        _logger.LogInformation("   ✅ Gemini extracted context keywords: {Keywords}",
                            string.Join(", ", geminiContextKeywords));
                    }
                    else
                    {
                        _logger.LogInformation("   ℹ️ Gemini returned no context keywords - using local keywords as fallback");
                        // Fallback to local keywords if Gemini returns nothing
                        extractedKeywords.Add(startDate.HasValue.ToString());
                        extractedKeywords.Add(endDate.HasValue.ToString());
                        geminiContextKeywords = extractedKeywords;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "   ⚠️ Gemini context extraction failed - using local keywords as fallback");
                    // Fallback to local keywords if Gemini API fails
                    geminiContextKeywords = extractedKeywords;
                }

                // ============================================================
                // STEP 3: HYBRID VS SEMANTIC SEARCH DECISION
                // ============================================================
                List<(string id, float similarity, Dictionary<string, object> metadata)> similarResults;

                // Use hybrid search if we have context keywords (from Gemini or fallback)
                if (geminiContextKeywords != null && geminiContextKeywords.Any())
                {
                    _logger.LogInformation("   🔀 Using HYBRID search (semantic + keyword boost) with {Count} context keywords",
                        geminiContextKeywords.Count);
                    similarResults = await _pgVectorService.SearchSimilarWithKeywordBoostAsync(
                        queryEmbedding,
                        geminiContextKeywords, // Use Gemini context keywords for intelligent boosting
                        searchLimit,
                        adjustedMinScore,
                        null, // No URI filtering - full semantic search
                        keywordBoostWeight: 0.4f); // 40% weight to keyword matching
                }
                else
                {
                    _logger.LogInformation("   🎯 Using SEMANTIC search only (no context keywords available)");
                    // Standard semantic search for general queries
                    similarResults = await _pgVectorService.SearchSimilarAsync(
                        queryEmbedding,
                        searchLimit,
                        adjustedMinScore,
                        null); // No URI filtering - full semantic search
                }

                _logger.LogInformation("   ✅ PostgreSQL returned {Count} results", similarResults.Count);

                if (!similarResults.Any())
                {
                    _logger.LogWarning("   ⚠️ No results found from PostgreSQL search");
                    return new RecordSearchResponseDto
                    {
                        Query = query,
                        Results = new List<RecordSearchResultDto>(),
                        TotalResults = 0,
                        QueryTime = (float)stopwatch.Elapsed.TotalSeconds,
                        SynthesizedAnswer = "No matching records found. Try adjusting your search terms or lowering the minimum score."
                    };
                }

                // Filter to only Content Manager records
                var recordResults = similarResults
                    .Where(r => r.metadata.ContainsKey("entity_type") &&
                               r.metadata["entity_type"].ToString() == "content_manager_record")
                    .ToList();

                _logger.LogInformation("   ✅ Filtered to {Count} Content Manager records", recordResults.Count);

                // ============================================================
                // STEP 4: APPLY POST-FILTERS
                // ============================================================
                _logger.LogInformation("🔍 STEP 4: Applying Post-Filters");

                // Apply file type filtering if specified
                if (fileTypeFilters.Any())
                {
                    _logger.LogInformation("   📋 Applying file type filter ({FileTypes})...", string.Join(", ", fileTypeFilters));
                    recordResults = _helperServices.ApplyFileTypeFilter(recordResults, fileTypeFilters);
                    _logger.LogInformation("   ✅ After file type filtering: {Count} results", recordResults.Count);
                }

                // Apply date range filter if extracted from query
                if (startDate.HasValue || endDate.HasValue)
                {
                    _logger.LogInformation("   📋 Applying date range filter...");
                    _logger.LogInformation("      Target Range: {StartDate} to {EndDate}",
                        startDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "any",
                        endDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "any");

                    var beforeCount = recordResults.Count;
                    recordResults = _helperServices.ApplyDateRangeFilter(recordResults, startDate, endDate);

                    _logger.LogInformation("   ✅ After date filtering: {Count} results (filtered out {Removed})",
                        recordResults.Count, beforeCount - recordResults.Count);
                }

                // Apply additional metadata filters if provided
                if (metadataFilters != null && metadataFilters.Any())
                {
                    recordResults = _helperServices.ApplyMetadataFilters(recordResults, metadataFilters);
                    _logger.LogInformation("After metadata filtering: {Count} results", recordResults.Count);
                }

                if (!recordResults.Any())
                {
                    _logger.LogWarning("No results after applying filters");
                    return new RecordSearchResponseDto
                    {
                        Query = query,
                        Results = new List<RecordSearchResultDto>(),
                        TotalResults = 0,
                        QueryTime = (float)stopwatch.Elapsed.TotalSeconds,
                        SynthesizedAnswer = "No records found matching your criteria. Try broadening your search terms or adjusting filters."
                    };
                }

                // IMPORTANT: Deduplicate by record_uri BEFORE sorting and limiting
                var deduplicatedResults = recordResults
                    .GroupBy(r => _helperServices.GetMetadataValue<long>(r.metadata, "record_uri"))
                    .Select(g => g.OrderByDescending(r => r.similarity).First())
                    .ToList();

                _logger.LogInformation("After deduplication by record_uri: {Count} unique records", deduplicatedResults.Count);

                // Apply sorting based on query intent
                if (isEarliest || isLatest)
                {
                    deduplicatedResults = _helperServices.ApplyDateSorting(deduplicatedResults, isEarliest);
                    _logger.LogInformation("Applied date sorting - earliest: {IsEarliest}", isEarliest);
                }
                else
                {
                    // Default: sort by relevance score
                    deduplicatedResults = deduplicatedResults.OrderByDescending(r => r.similarity).ToList();
                }

                // Take final results
                var finalResults = deduplicatedResults.Take(topK).ToList();

                // ============================================================
                // STEP 5: APPLY ACL FILTERING
                // Filter results based on current user's access permissions
                // ============================================================
                _logger.LogInformation("🔒 STEP 5: Applying ACL Filtering");
                var aclFilteredResults = await ApplyAclFilterAsync(finalResults);
                _logger.LogInformation("   ✅ ACL filtering complete: {Accessible} accessible out of {Total} results",
                    aclFilteredResults.Count, finalResults.Count);

                // Convert to search result DTOs
                var searchResults = aclFilteredResults.Select(result => new RecordSearchResultDto
                {
                    RecordUri = _helperServices.GetMetadataValue<long>(result.metadata, "record_uri"),
                    RecordTitle = _helperServices.GetMetadataValue<string>(result.metadata, "record_title") ?? "",
                    DateCreated = _helperServices.GetMetadataValue<string>(result.metadata, "date_created") ?? "",
                    RecordType = _helperServices.GetMetadataValue<string>(result.metadata, "record_type") ?? "",
                    RelevanceScore = result.similarity,
                    Metadata = result.metadata,
                    ContentPreview = _helperServices.GetMetadataValue<string>(result.metadata, "chunk_content") ?? _helperServices.BuildContentPreview(result.metadata)
                }).ToList();

                // Generate AI synthesis of results
                var synthesizedAnswer = "";
                try
                {
                    synthesizedAnswer = await _googleServices.SynthesizeRecordAnswerAsync(query, searchResults);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to synthesize answer for query: {Query}", query);
                    synthesizedAnswer = $"Found {searchResults.Count} matching records. AI summary temporarily unavailable.";
                }

                stopwatch.Stop();

                _logger.LogInformation("Search completed. Found {ResultCount} unique results in {ElapsedMs}ms",
                    searchResults.Count, stopwatch.ElapsedMilliseconds);

                return new RecordSearchResponseDto
                {
                    Query = query,
                    Results = searchResults,
                    TotalResults = searchResults.Count,
                    QueryTime = (float)stopwatch.Elapsed.TotalSeconds,
                    SynthesizedAnswer = synthesizedAnswer
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Search failed for query: {Query}", query);

                return new RecordSearchResponseDto
                {
                    Query = query ?? "",
                    Results = new List<RecordSearchResultDto>(),
                    TotalResults = 0,
                    QueryTime = (float)stopwatch.Elapsed.TotalSeconds,
                    SynthesizedAnswer = $"Search failed due to an error: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Filter search results based on current user's ACL permissions using Trim SDK
        /// Only returns records the current user has access to
        /// </summary>
        /// <param name="results">Search results to filter</param>
        /// <returns>Filtered list containing only accessible records</returns>
        private async Task<List<(string id, float similarity, Dictionary<string, object> metadata)>> ApplyAclFilterAsync(List<(string id, float similarity, Dictionary<string, object> metadata)> results)
        {
            if (results == null || !results.Any())
            {
                return results ?? new List<(string id, float similarity, Dictionary<string, object> metadata)>();
            }

            try
            {
                // Get database connection with current user's context
                var database = await _contentManagerServices.GetDatabaseAsync();
                var currentUser = database.CurrentUser?.Name ?? "Unknown";

                _logger.LogInformation("   🔐 Checking ACL permissions for user: {User}", currentUser);

                var accessibleResults = new List<(string id, float similarity, Dictionary<string, object> metadata)>();
                var deniedCount = 0;
                var unrestrictedCount = 0;
                var restrictedButAccessibleCount = 0;

                foreach (var result in results)
                {
                    try
                    {
                        var recordUri = _helperServices.GetMetadataValue<long>(result.metadata, "record_uri");

                        // Attempt to access record with current user's permissions
                        // Trim SDK will enforce ACL automatically based on database.TrustedUser
                        var record = new Record(database, recordUri);

                        // Try to access a property that requires ViewDocument permission
                        // This will throw TrimException if user doesn't have access
                        var title = record.Title;

                        // If we get here, user has access
                        accessibleResults.Add(result);

                        // Track whether this was an unrestricted or ACL-restricted record
                        var aclString = record.AccessControlList?.ToString() ?? "";
                        if (aclString.Contains("<Unrestricted>") || string.IsNullOrEmpty(aclString))
                        {
                            unrestrictedCount++;
                        }
                        else
                        {
                            restrictedButAccessibleCount++;
                            _logger.LogDebug("   ✅ User {User} granted access to restricted record {Uri}: {Title}",
                                currentUser, recordUri, title);
                        }
                    }
                    catch (Exception ex)
                    {
                        // User doesn't have access to this record
                        var recordUri = _helperServices.GetMetadataValue<long>(result.metadata, "record_uri");
                        var recordTitle = _helperServices.GetMetadataValue<string>(result.metadata, "record_title") ?? "Unknown";

                        _logger.LogDebug("   🔒 User {User} denied access to record {Uri}: {Title} - {Error}",
                            currentUser, recordUri, recordTitle, ex.Message);

                        deniedCount++;
                    }
                }

                // Log ACL filtering summary
                _logger.LogInformation("   📊 ACL Filtering Summary:");
                _logger.LogInformation("      Total Results: {Total}", results.Count);
                _logger.LogInformation("      Accessible: {Accessible}", accessibleResults.Count);
                _logger.LogInformation("      Denied: {Denied}", deniedCount);
                _logger.LogInformation("      ├─ Unrestricted: {Unrestricted}", unrestrictedCount);
                _logger.LogInformation("      └─ Restricted (Accessible): {RestrictedAccessible}", restrictedButAccessibleCount);

                return accessibleResults;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "   ❌ Error during ACL filtering. Returning original results as fallback.");

                // Fallback: return original results if ACL filtering fails
                // This ensures search continues working even if ACL check fails
                return results;
            }
        }

    }
}
