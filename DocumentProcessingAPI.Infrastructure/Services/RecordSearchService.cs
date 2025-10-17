using DocumentProcessingAPI.Core.DTOs;
using DocumentProcessingAPI.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel;
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
        private readonly QdrantVectorService _qdrantService;
        private readonly ILogger<RecordSearchService> _logger;
        private readonly IConfiguration _configuration;
        private readonly ContentManagerServices _contentManagerServices;

        public RecordSearchService(
            IEmbeddingService embeddingService,
            QdrantVectorService qdrantService,
            ILogger<RecordSearchService> logger,
            IConfiguration configuration,
            ContentManagerServices contentManagerServices)
        {
            _embeddingService = embeddingService;
            _qdrantService = qdrantService;
            _logger = logger;
            _configuration = configuration;
            _contentManagerServices = contentManagerServices;
        }

        /// <summary>
        /// Search records by natural language query with optional metadata filters
        /// Enhanced with better error handling, validation, and dynamic query processing
        ///
        /// Example queries:
        ///
        /// SPECIFIC DATES:
        /// - "get me records created on 22-10-2024"
        /// - "records created today"
        /// - "documents from yesterday"
        /// - "files created on October 9, 2025"
        ///
        /// DATE RANGES:
        /// - "Show me all documents created after October 9, 2025"
        /// - "documents before January 1, 2024"
        /// - "records from last week"
        /// - "files from last 7 days"
        /// - "documents from last 3 months"
        /// - "records from last 2 years"
        ///
        /// WEEKS:
        /// - "records from this week"
        /// - "documents from week 1"
        /// - "files from week 42 of 2024"
        /// - "records from the week of October 3"
        /// - "documents from last 4 weeks"
        ///
        /// MONTHS:
        /// - "records from this month"
        /// - "documents from last month"
        /// - "files from October 2024"
        /// - "records from last October"
        /// - "documents from September"
        /// - "files from last 6 months"
        ///
        /// YEARS:
        /// - "records from this year"
        /// - "documents from last year"
        /// - "files from 2024"
        /// - "records from year 2023"
        /// - "documents from last 2 years"
        ///
        /// QUARTERS:
        /// - "records from Q1 2024"
        /// - "documents from Q2"
        /// - "files from first quarter"
        /// - "records from second quarter 2023"
        /// - "documents from fourth quarter"
        ///
        /// SORTING:
        /// - "Which record has the earliest creation date?"
        /// - "What are the most recently created documents?"
        /// - "Show me the latest files"
        /// - "Find the oldest records"
        ///
        /// TIME-BASED:
        /// - "List all Word or Excel documents added after 3:45 PM"
        /// - "documents created after 15:30"
        ///
        /// COMBINED QUERIES:
        /// - "What are the most recently created documents related to API or Service?"
        /// - "invoice details of Umar khan from last month"
        /// - "Excel files from Q1 2024"
        /// - "Word documents created in October 2024"
        /// </summary>
        public async Task<RecordSearchResponseDto> SearchRecordsAsync(
            string query,
            Dictionary<string, object>? metadataFilters = null,
            int topK = 20,
            float minimumScore = 0.3f)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Input validation
                if (string.IsNullOrWhiteSpace(query))
                {
                    _logger.LogWarning("Empty or null query provided");
                    return new RecordSearchResponseDto
                    {
                        Query = query ?? "",
                        Results = new List<RecordSearchResultDto>(),
                        TotalResults = 0,
                        QueryTime = 0,
                        SynthesizedAnswer = "Please provide a search query."
                    };
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
                var cleanQuery = CleanAndNormalizeQuery(query);
                _logger.LogInformation("   Original Query: {OriginalQuery}", query);
                _logger.LogInformation("   Normalized Query: {CleanQuery}", cleanQuery);

                // Extract date filter from query if present
                var (startDate, endDate) = ExtractDateRangeFromQuery(cleanQuery);
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
                var fileTypeFilters = ExtractFileTypeFilters(cleanQuery);
                if (fileTypeFilters.Any())
                {
                    _logger.LogInformation("   ✅ File type filters: {FileTypes}", string.Join(", ", fileTypeFilters));
                }

                // Extract sorting intent
                var (isEarliest, isLatest) = ExtractSortingIntent(cleanQuery);
                if (isEarliest || isLatest)
                {
                    _logger.LogInformation("   ✅ Sorting intent: Earliest={IsEarliest}, Latest={IsLatest}",
                        isEarliest, isLatest);
                }

                // ============================================================
                // STEP 2: CONTENT MANAGER IDOL INDEX SEARCH (Fast Pre-filter)
                // ============================================================
                _logger.LogInformation("🔍 STEP 2: Content Manager IDOL Index Search");

                HashSet<long> candidateRecordUris = null;
                List<string> recordDetails = null;

                // Prepare content query for CM search (remove stop words)
                var contentQuery = RemoveCommonQueryWords(cleanQuery);

                // Check if we should use CM Index search
                // Use it if we have dates, content, or file type filters
                var shouldUseCMSearch = startDate.HasValue || endDate.HasValue ||
                                       !string.IsNullOrWhiteSpace(contentQuery) ||
                                       fileTypeFilters.Any();

                if (shouldUseCMSearch)
                {
                    try
                    {
                        // Execute CM IDOL index search with separate parameters
                        // SetSearchString can only accept ONE field at a time
                        // Returns both URIs and record details (Title, DateCreated, etc.)
                        (candidateRecordUris, recordDetails) = await ExecuteContentManagerSearchAsync(
                            contentQuery,
                            startDate,
                            endDate,
                            fileTypeFilters);

                        _logger.LogInformation("   ✅ CM Index returned {Count} candidate records with details",
                            candidateRecordUris?.Count ?? 0);

                        // Continue to semantic search even if CM returns 0 or null
                        // Semantic search might still find relevant results
                        if (candidateRecordUris == null || candidateRecordUris.Count == 0)
                        {
                            _logger.LogInformation("   ℹ️ No records from CM Index, continuing with full semantic search");
                            candidateRecordUris = null; // Set to null to skip intersection later
                            recordDetails = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "   ⚠️ CM Index search failed, falling back to full semantic search");
                        candidateRecordUris = null; // Fall back to full search
                        recordDetails = null;
                    }
                }
                else
                {
                    _logger.LogInformation("   ℹ️ Skipping CM Index search - using full semantic search");
                }

                // ============================================================
                // STEP 3: SEMANTIC SEARCH WITH QDRANT (Narrow down with embeddings)
                // ============================================================
                _logger.LogInformation("🔍 STEP 3: Semantic Search with Qdrant");

                // Dynamic search limit calculation
                var searchLimit = CalculateDynamicSearchLimit(topK, isEarliest, isLatest, startDate, endDate,
                    fileTypeFilters.Count, 0);

                // Increase search limit if we have CM candidates to ensure we find them
                if (candidateRecordUris != null && candidateRecordUris.Count > 0)
                {
                    searchLimit = Math.Max(searchLimit, Math.Min(candidateRecordUris.Count * 2, 1000));
                }

                var adjustedMinScore = CalculateDynamicMinimumScore(minimumScore, cleanQuery, new List<string>());

                _logger.LogInformation("   📊 Search Parameters: Limit={SearchLimit}, MinScore={AdjustedMinScore}",
                    searchLimit, adjustedMinScore);

                // If we have record details from CM Index, append them to enhance the query
                // This enriches the query with record metadata for better semantic matching
                var enhancedQuery = cleanQuery;
                if (recordDetails != null && recordDetails.Count > 0)
                {
                    // Append all record details to the query
                    enhancedQuery = $"{cleanQuery} {string.Join(" ", recordDetails)}";
                    _logger.LogInformation("   ✅ Enhanced query with {Count} record details from CM Index",
                        recordDetails.Count);
                    _logger.LogDebug("   Enhanced query preview: {Preview}...",
                        enhancedQuery.Length > 200 ? enhancedQuery.Substring(0, 200) : enhancedQuery);
                }

                // Generate embedding for the query (with record details if available)
                var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(enhancedQuery);

                // Search similar embeddings from Qdrant
                var similarResults = await _qdrantService.SearchSimilarAsync(
                    queryEmbedding,
                    searchLimit,
                    adjustedMinScore);

                _logger.LogInformation("   ✅ Qdrant returned {Count} results", similarResults.Count);

                if (!similarResults.Any())
                {
                    _logger.LogWarning("   ⚠️ No results found from Qdrant search");
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
                // STEP 4: INTERSECT CM CANDIDATES WITH SEMANTIC RESULTS
                // ============================================================
                if (candidateRecordUris != null && candidateRecordUris.Count > 0)
                {
                    _logger.LogInformation("🔍 STEP 4: Intersecting CM candidates with semantic results");

                    var beforeIntersect = recordResults.Count;
                    recordResults = recordResults
                        .Where(r => candidateRecordUris.Contains(GetMetadataValue<long>(r.metadata, "record_uri")))
                        .ToList();

                    _logger.LogInformation("   ✅ After intersection: {Count} results (filtered out {Removed})",
                        recordResults.Count, beforeIntersect - recordResults.Count);

                    if (!recordResults.Any())
                    {
                        _logger.LogWarning("   ⚠️ No overlap between CM Index and Semantic Search results");
                        return new RecordSearchResponseDto
                        {
                            Query = query,
                            Results = new List<RecordSearchResultDto>(),
                            TotalResults = 0,
                            QueryTime = (float)stopwatch.Elapsed.TotalSeconds,
                            SynthesizedAnswer = "No records found matching both the index search criteria and semantic relevance. Try broadening your search."
                        };
                    }
                }

                // ============================================================
                // STEP 5: APPLY POST-FILTERS (if not already applied by CM search)
                // ============================================================
                _logger.LogInformation("🔍 STEP 5: Applying Post-Filters");

                // Apply file type filtering if specified (only if not already applied by CM search)
                if (fileTypeFilters.Any())
                {
                    _logger.LogInformation("   📋 Applying file type filter ({FileTypes})...", string.Join(", ", fileTypeFilters));
                    recordResults = ApplyFileTypeFilter(recordResults, fileTypeFilters);
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
                    recordResults = ApplyDateRangeFilter(recordResults, startDate, endDate);

                    _logger.LogInformation("   ✅ After date filtering: {Count} results (filtered out {Removed})",
                        recordResults.Count, beforeCount - recordResults.Count);
                }

                // Apply additional metadata filters if provided
                if (metadataFilters != null && metadataFilters.Any())
                {
                    recordResults = ApplyMetadataFilters(recordResults, metadataFilters);
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
                    .GroupBy(r => GetMetadataValue<long>(r.metadata, "record_uri"))
                    .Select(g => g.OrderByDescending(r => r.similarity).First())
                    .ToList();

                _logger.LogInformation("After deduplication by record_uri: {Count} unique records", deduplicatedResults.Count);

                // Apply sorting based on query intent
                if (isEarliest || isLatest)
                {
                    deduplicatedResults = ApplyDateSorting(deduplicatedResults, isEarliest);
                    _logger.LogInformation("Applied date sorting - earliest: {IsEarliest}", isEarliest);
                }
                else
                {
                    // Default: sort by relevance score
                    deduplicatedResults = deduplicatedResults.OrderByDescending(r => r.similarity).ToList();
                }

                // Take final results
                var finalResults = deduplicatedResults.Take(topK).ToList();

                // Convert to search result DTOs
                var searchResults = finalResults.Select(result => new RecordSearchResultDto
                {
                    RecordUri = GetMetadataValue<long>(result.metadata, "record_uri"),
                    RecordTitle = GetMetadataValue<string>(result.metadata, "record_title") ?? "",
                    DateCreated = GetMetadataValue<string>(result.metadata, "date_created") ?? "",
                    RecordType = GetMetadataValue<string>(result.metadata, "record_type") ?? "",
                    RelevanceScore = result.similarity,
                    Metadata = result.metadata,
                    ContentPreview = GetMetadataValue<string>(result.metadata, "chunk_content") ?? BuildContentPreview(result.metadata)
                }).ToList();

                // Generate AI synthesis of results
                var synthesizedAnswer = "";
                try
                {
                    synthesizedAnswer = await SynthesizeRecordAnswerAsync(query, searchResults);
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
        /// Clean and normalize the input query for better processing
        /// </summary>
        private string CleanAndNormalizeQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return "";

            // Remove extra whitespace and normalize
            query = System.Text.RegularExpressions.Regex.Replace(query.Trim(), @"\s+", " ");
            
            // Convert smart quotes to regular quotes
            query = query.Replace(""", "\"").Replace(""", "\"").Replace("'", "'").Replace("'", "'");
            
            // Normalize common variations
            query = query.Replace(" & ", " and ");
            query = query.Replace(" + ", " and ");
            
            return query;
        }


        /// <summary>
        /// Calculate dynamic search limit based on query complexity
        /// </summary>
        private int CalculateDynamicSearchLimit(int topK, bool isEarliest, bool isLatest, DateTime? startDate, DateTime? endDate, int fileTypeFiltersCount, int contentKeywordsCount)
        {
            var baseMultiplier = 3; // Base multiplier for simple queries

            // Increase multiplier for complex queries
            if (isEarliest || isLatest)
                baseMultiplier = Math.Max(baseMultiplier, 20); // Need more results to sort properly

            if (startDate.HasValue || endDate.HasValue)
                baseMultiplier = Math.Max(baseMultiplier, 10); // Date filtering needs more results

            if (fileTypeFiltersCount > 0)
                baseMultiplier += 5; // File type filtering

            if (contentKeywordsCount > 2)
                baseMultiplier += 3; // Complex content queries

            // Cap the maximum
            return Math.Min(topK * baseMultiplier, 1000);
        }

        /// <summary>
        /// Calculate dynamic minimum score based on query characteristics
        /// </summary>
        private float CalculateDynamicMinimumScore(float originalMinScore, string query, List<string> contentKeywords)
        {
            var adjustedScore = originalMinScore;
            var lowerQuery = query.ToLowerInvariant();

            // Lower threshold for specific content searches
            if (contentKeywords.Any(k => new[] { "api", "service", "system", "application" }.Contains(k.ToLowerInvariant())))
            {
                adjustedScore = Math.Max(0.1f, adjustedScore - 0.2f);
            }

            // Lower threshold for name searches
            if (contentKeywords.Any(k => char.IsUpper(k[0]))) // Proper nouns
            {
                adjustedScore = Math.Max(0.15f, adjustedScore - 0.15f);
            }

            // Lower threshold for document type searches
            if (lowerQuery.Contains("invoice") || lowerQuery.Contains("document") || lowerQuery.Contains("file"))
            {
                adjustedScore = Math.Max(0.2f, adjustedScore - 0.1f);
            }

            return adjustedScore;
        }

        /// <summary>
        /// Extract date or date range from natural language query with comprehensive support
        /// Now supports date ranges for terms like "recently", "last week", etc.
        /// Also supports "after", "before", "earliest", "latest", and file type filtering
        /// Returns (startDate, endDate) - both can be null if no date found
        /// </summary>
        private (DateTime? startDate, DateTime? endDate) ExtractDateRangeFromQuery(string query)
        {
            var lowerQuery = query.ToLowerInvariant();
            var now = DateTime.Now.Date;

            // Handle "earliest" or "oldest" - return a very early date to get all records, then we'll sort
            if (lowerQuery.Contains("earliest") || lowerQuery.Contains("oldest"))
            {
                _logger.LogInformation("Date filter: earliest/oldest - will sort by creation date ascending");
                // Return a very early date to include all records
                return (new DateTime(1900, 1, 1), DateTime.MaxValue);
            }

            // Handle "latest" or "newest" or "most recent" - return recent date range
            if (lowerQuery.Contains("latest") || lowerQuery.Contains("newest") || lowerQuery.Contains("most recent"))
            {
                // For "most recently created", get last 30 days but we'll sort by creation date descending
                var startDate = now.AddDays(-30);
                _logger.LogInformation("Date range filter: latest/newest = {StartDate} to {EndDate}", startDate, now);
                return (startDate, now);
            }

            // Handle "after" date queries (e.g., "after October 9, 2025", "created after 10/9/2025")
            var afterMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery, @"after\s+(.+?)(?:\s|$)");
            if (afterMatch.Success)
            {
                var dateString = afterMatch.Groups[1].Value.Trim();
                var parsedDate = ParseDateFromString(dateString, now);
                if (parsedDate.HasValue)
                {
                    _logger.LogInformation("Date filter: after {Date}", parsedDate.Value);
                    return (parsedDate.Value.AddDays(1), DateTime.MaxValue); // Start from day after
                }
            }

            // Handle "before" date queries (e.g., "before October 9, 2025", "created before 10/9/2025")
            var beforeMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery, @"before\s+(.+?)(?:\s|$)");
            if (beforeMatch.Success)
            {
                var dateString = beforeMatch.Groups[1].Value.Trim();
                var parsedDate = ParseDateFromString(dateString, now);
                if (parsedDate.HasValue)
                {
                    _logger.LogInformation("Date filter: before {Date}", parsedDate.Value);
                    return (new DateTime(1900, 1, 1), parsedDate.Value.AddDays(-1)); // End day before
                }
            }

            // Handle "between" date queries with enhanced support
            // Pattern 1: "between X and Y on [date]" - time range on specific date
            var betweenTimeOnDateMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery,
                @"between\s+(\d{1,2})\s*(am|pm)?\s+and\s+(\d{1,2})\s*(am|pm)?\s+on\s+(.+?)(?:\s|$)");
            if (betweenTimeOnDateMatch.Success)
            {
                var startHour = int.Parse(betweenTimeOnDateMatch.Groups[1].Value);
                var startAmPm = betweenTimeOnDateMatch.Groups[2].Value.ToLowerInvariant();
                var endHour = int.Parse(betweenTimeOnDateMatch.Groups[3].Value);
                var endAmPm = betweenTimeOnDateMatch.Groups[4].Value.ToLowerInvariant();
                var dateString = betweenTimeOnDateMatch.Groups[5].Value.Trim();

                // Convert to 24-hour format
                if (startAmPm == "pm" && startHour != 12) startHour += 12;
                if (startAmPm == "am" && startHour == 12) startHour = 0;
                if (endAmPm == "pm" && endHour != 12) endHour += 12;
                if (endAmPm == "am" && endHour == 12) endHour = 0;

                var baseDate = ParseDateFromString(dateString, now);
                if (baseDate.HasValue)
                {
                    var startDateTime = baseDate.Value.Date.AddHours(startHour);
                    var endDateTime = baseDate.Value.Date.AddHours(endHour);
                    _logger.LogInformation("Time range filter: between {StartTime} and {EndTime} on {Date}",
                        startDateTime, endDateTime, baseDate.Value.Date);
                    return (startDateTime, endDateTime);
                }
            }

            // Pattern 2: "between [month year] and [month year]" - month range
            var monthNamesForBetween = new Dictionary<string, int>
            {
                { "january", 1 }, { "jan", 1 },
                { "february", 2 }, { "feb", 2 },
                { "march", 3 }, { "mar", 3 },
                { "april", 4 }, { "apr", 4 },
                { "may", 5 },
                { "june", 6 }, { "jun", 6 },
                { "july", 7 }, { "jul", 7 },
                { "august", 8 }, { "aug", 8 },
                { "september", 9 }, { "sep", 9 }, { "sept", 9 },
                { "october", 10 }, { "oct", 10 },
                { "november", 11 }, { "nov", 11 },
                { "december", 12 }, { "dec", 12 }
            };

            // Build month pattern string
            var monthPattern = string.Join("|", monthNamesForBetween.Keys);

            // Pattern: "between January 2025 and March 2025" or "from Jan 2025 to Mar 2025"
            var betweenMonthsMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery,
                $@"(?:between|from)\s+({monthPattern})\s+(\d{{4}})\s+(?:and|to)\s+({monthPattern})\s+(\d{{4}})");
            if (betweenMonthsMatch.Success)
            {
                var startMonth = monthNamesForBetween[betweenMonthsMatch.Groups[1].Value];
                var startYear = int.Parse(betweenMonthsMatch.Groups[2].Value);
                var endMonth = monthNamesForBetween[betweenMonthsMatch.Groups[3].Value];
                var endYear = int.Parse(betweenMonthsMatch.Groups[4].Value);

                var startDate = new DateTime(startYear, startMonth, 1);
                var endDate = new DateTime(endYear, endMonth, DateTime.DaysInMonth(endYear, endMonth));

                _logger.LogInformation("Month range filter: between {StartMonth} {StartYear} and {EndMonth} {EndYear}",
                    betweenMonthsMatch.Groups[1].Value, startYear, betweenMonthsMatch.Groups[3].Value, endYear);
                return (startDate, endDate);
            }

            // Pattern 3: General "between X and Y" - handles various date formats
            // Enhanced to support multiple date format patterns with explicit date pattern matching
            var betweenPatterns = new[]
            {
                // Match explicit date patterns: DD/MM/YYYY or DD-MM-YYYY
                @"between\s+(\d{1,2}[/-]\d{1,2}[/-]\d{4})\s+(?:and|to)\s+(\d{1,2}[/-]\d{1,2}[/-]\d{4})",
                @"from\s+(\d{1,2}[/-]\d{1,2}[/-]\d{4})\s+to\s+(\d{1,2}[/-]\d{1,2}[/-]\d{4})",
                // Match month names: "January 2025 to March 2025"
                @"between\s+([a-z]+\s+\d{1,2},?\s+\d{4})\s+(?:and|to)\s+([a-z]+\s+\d{1,2},?\s+\d{4})",
                @"from\s+([a-z]+\s+\d{1,2},?\s+\d{4})\s+to\s+([a-z]+\s+\d{1,2},?\s+\d{4})"
            };

            foreach (var pattern in betweenPatterns)
            {
                var betweenMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery, pattern);
                if (betweenMatch.Success)
                {
                    var startDateString = betweenMatch.Groups[1].Value.Trim();
                    var endDateString = betweenMatch.Groups[2].Value.Trim();

                    _logger.LogInformation("   🔍 Pattern matched - Start: '{StartDateString}', End: '{EndDateString}'",
                        startDateString, endDateString);

                    // Try to parse with enhanced parsing that handles all formats
                    var parsedStartDate = ParseDateFromString(startDateString, now);
                    var parsedEndDate = ParseDateFromString(endDateString, now);

                    _logger.LogInformation("   📅 Parsed - Start: {StartDate}, End: {EndDate}",
                        parsedStartDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "FAILED",
                        parsedEndDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "FAILED");

                    if (parsedStartDate.HasValue && parsedEndDate.HasValue)
                    {
                        // For date range queries, include the full end date (end of day)
                        var adjustedEndDate = parsedEndDate.Value.Date.AddDays(1).AddSeconds(-1);
                        _logger.LogInformation("✅ Date range filter: from {StartDate} to {EndDate} (adjusted to end of day)",
                            parsedStartDate.Value.ToString("yyyy-MM-dd HH:mm:ss"),
                            adjustedEndDate.ToString("yyyy-MM-dd HH:mm:ss"));
                        return (parsedStartDate.Value, adjustedEndDate);
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ Failed to parse one or both dates, trying next pattern");
                    }
                }
            }

            // Handle "around" time queries (e.g., "around noon", "around 3 PM", "around midnight")
            var aroundTimeResult = ExtractAroundTimeFilter(lowerQuery, now);
            if (aroundTimeResult.HasValue)
            {
                return aroundTimeResult.Value;
            }

            // Handle "at [time]" queries (e.g., "at 10:39", "created at 15:30", "at 3:45 PM")
            var atTimeMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery, @"(?:at|created\s+at)\s+(\d{1,2}):?(\d{2})?\s*(am|pm)?");
            if (atTimeMatch.Success)
            {
                var hour = int.Parse(atTimeMatch.Groups[1].Value);
                var minute = atTimeMatch.Groups[2].Success ? int.Parse(atTimeMatch.Groups[2].Value) : 0;
                var ampm = atTimeMatch.Groups[3].Success ? atTimeMatch.Groups[3].Value.ToLowerInvariant() : "";

                // Convert to 24-hour format if AM/PM specified
                if (!string.IsNullOrEmpty(ampm))
                {
                    if (ampm == "pm" && hour != 12) hour += 12;
                    if (ampm == "am" && hour == 12) hour = 0;
                }

                // Create a time window: exact time ±1 minute
                var targetTime = now.Date.AddHours(hour).AddMinutes(minute);
                var startTime = targetTime.AddMinutes(-1);
                var endTime = targetTime.AddMinutes(1);
                _logger.LogInformation("Time filter: at {TargetTime} = {StartTime} to {EndTime}",
                    targetTime.ToString("HH:mm"), startTime, endTime);
                return (startTime, endTime);
            }

            // Handle time-based queries (e.g., "after 3:45 PM", "added after 15:30")
            var timeAfterMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery, @"after\s+(\d{1,2}):(\d{2})\s*(am|pm)?");
            if (timeAfterMatch.Success)
            {
                var hour = int.Parse(timeAfterMatch.Groups[1].Value);
                var minute = int.Parse(timeAfterMatch.Groups[2].Value);
                var ampm = timeAfterMatch.Groups[3].Value.ToLowerInvariant();

                // Convert to 24-hour format
                if (ampm == "pm" && hour != 12) hour += 12;
                if (ampm == "am" && hour == 12) hour = 0;

                var timeThreshold = now.Date.AddHours(hour).AddMinutes(minute);
                _logger.LogInformation("Time filter: after {Time}", timeThreshold);
                return (timeThreshold, DateTime.MaxValue);
            }

            // 1. Check for "today"
            if (lowerQuery.Contains("today"))
            {
                _logger.LogInformation("Date filter: today = {Date}", now);
                return (now, now);
            }

            // 2. Check for "recently" or "recent" (last 14 days)
            if (lowerQuery.Contains("recently") || lowerQuery.Contains("recent"))
            {
                var startDate = now.AddDays(-14);
                _logger.LogInformation("Date range filter: recently = {StartDate} to {EndDate}", startDate, now);
                return (startDate, now);
            }

            // 3. Check for "yesterday"
            if (lowerQuery.Contains("yesterday") && !lowerQuery.Contains("before yesterday"))
            {
                var yesterday = now.AddDays(-1);
                _logger.LogInformation("Date filter: yesterday = {Date} ({DateString}). Current date: {Now}",
                    yesterday, yesterday.ToString("yyyy-MM-dd"), now.ToString("yyyy-MM-dd"));
                return (yesterday, yesterday);
            }

            // 4. Check for "day before yesterday"
            if (lowerQuery.Contains("day before yesterday") || lowerQuery.Contains("before yesterday"))
            {
                var dayBeforeYesterday = now.AddDays(-2);
                _logger.LogInformation("Date filter: day before yesterday = {Date}", dayBeforeYesterday);
                return (dayBeforeYesterday, dayBeforeYesterday);
            }

            // 5. Check for "X days ago" (e.g., "3 days ago", "5 days ago") - treat as single day
            var daysAgoMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery, @"(\d+)\s*days?\s*ago");
            if (daysAgoMatch.Success)
            {
                var daysAgo = int.Parse(daysAgoMatch.Groups[1].Value);
                var date = now.AddDays(-daysAgo);
                _logger.LogInformation("Date filter: {Days} days ago = {Date}", daysAgo, date);
                return (date, date);
            }

            // 6. Check for "last X days" (e.g., "last 7 days", "last 30 days") - treat as range
            var lastDaysMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery, @"last\s+(\d+)\s*days?");
            if (lastDaysMatch.Success)
            {
                var days = int.Parse(lastDaysMatch.Groups[1].Value);
                var startDate = now.AddDays(-days);
                _logger.LogInformation("Date range filter: last {Days} days = {StartDate} to {EndDate}", days, startDate, now);
                return (startDate, now);
            }

            // 7. Check for "last week" (last 7 days)
            if (lowerQuery.Contains("last week"))
            {
                var startDate = now.AddDays(-7);
                _logger.LogInformation("Date range filter: last week = {StartDate} to {EndDate}", startDate, now);
                return (startDate, now);
            }

            // 8. Check for "this week" (from Monday to today)
            if (lowerQuery.Contains("this week"))
            {
                var daysSinceMonday = (int)now.DayOfWeek - 1;
                if (daysSinceMonday < 0) daysSinceMonday = 6; // Sunday
                var startOfWeek = now.AddDays(-daysSinceMonday);
                _logger.LogInformation("Date range filter: this week = {StartDate} to {EndDate}", startOfWeek, now);
                return (startOfWeek, now);
            }

            // 9. Check for "last month" (last 30 days)
            if (lowerQuery.Contains("last month"))
            {
                var startDate = now.AddDays(-30);
                _logger.LogInformation("Date range filter: last month = {StartDate} to {EndDate}", startDate, now);
                return (startDate, now);
            }

            // 10. Check for "this month" (from 1st to today)
            if (lowerQuery.Contains("this month"))
            {
                var startOfMonth = new DateTime(now.Year, now.Month, 1);
                _logger.LogInformation("Date range filter: this month = {StartDate} to {EndDate}", startOfMonth, now);
                return (startOfMonth, now);
            }

            // 10a. Check for "last X weeks" (e.g., "last 2 weeks", "last 4 weeks")
            var lastWeeksMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery, @"last\s+(\d+)\s*weeks?");
            if (lastWeeksMatch.Success)
            {
                var weeks = int.Parse(lastWeeksMatch.Groups[1].Value);
                var startDate = now.AddDays(-weeks * 7);
                _logger.LogInformation("Date range filter: last {Weeks} weeks = {StartDate} to {EndDate}", weeks, startDate, now);
                return (startDate, now);
            }

            // 10b. Check for "last X months" (e.g., "last 3 months", "last 6 months")
            var lastMonthsMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery, @"last\s+(\d+)\s*months?");
            if (lastMonthsMatch.Success)
            {
                var months = int.Parse(lastMonthsMatch.Groups[1].Value);
                var startDate = now.AddMonths(-months);
                _logger.LogInformation("Date range filter: last {Months} months = {StartDate} to {EndDate}", months, startDate, now);
                return (startDate, now);
            }

            // 10c. Check for "last X years" (e.g., "last 2 years")
            var lastYearsMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery, @"last\s+(\d+)\s*years?");
            if (lastYearsMatch.Success)
            {
                var years = int.Parse(lastYearsMatch.Groups[1].Value);
                var startDate = now.AddYears(-years);
                _logger.LogInformation("Date range filter: last {Years} years = {StartDate} to {EndDate}", years, startDate, now);
                return (startDate, now);
            }

            // 10d. Check for "this year" (from Jan 1st to today)
            if (lowerQuery.Contains("this year"))
            {
                var startOfYear = new DateTime(now.Year, 1, 1);
                _logger.LogInformation("Date range filter: this year = {StartDate} to {EndDate}", startOfYear, now);
                return (startOfYear, now);
            }

            // 10e. Check for "last year" (entire previous year)
            if (lowerQuery.Contains("last year"))
            {
                var lastYear = now.Year - 1;
                var startOfLastYear = new DateTime(lastYear, 1, 1);
                var endOfLastYear = new DateTime(lastYear, 12, 31);
                _logger.LogInformation("Date range filter: last year = {StartDate} to {EndDate}", startOfLastYear, endOfLastYear);
                return (startOfLastYear, endOfLastYear);
            }

            // 10f. Check for specific year (e.g., "2024", "year 2023", "in 2022")
            // IMPORTANT: Use negative lookbehind and lookahead to avoid matching years that are part of dates
            // This prevents "09-10-2025" from being interpreted as "year 2025"
            // Also prevents matching years that follow month names (e.g., "September 2025" should be handled by month+year pattern)
            var yearMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery, @"(?<![\d-/])(?<!january\s)(?<!jan\s)(?<!february\s)(?<!feb\s)(?<!march\s)(?<!mar\s)(?<!april\s)(?<!apr\s)(?<!may\s)(?<!june\s)(?<!jun\s)(?<!july\s)(?<!jul\s)(?<!august\s)(?<!aug\s)(?<!september\s)(?<!sep\s)(?<!sept\s)(?<!october\s)(?<!oct\s)(?<!november\s)(?<!nov\s)(?<!december\s)(?<!dec\s)(?:year\s+|in\s+)?(\d{4})(?![-/\d])(?:\s|$|,)");
            if (yearMatch.Success)
            {
                var year = int.Parse(yearMatch.Groups[1].Value);
                if (year >= 1900 && year <= now.Year + 10) // Reasonable year range
                {
                    var startOfYear = new DateTime(year, 1, 1);
                    var endOfYear = new DateTime(year, 12, 31);
                    _logger.LogInformation("Date range filter: year {Year} = {StartDate} to {EndDate}", year, startOfYear, endOfYear);
                    return (startOfYear, endOfYear);
                }
            }

            // 10g. Check for quarters (e.g., "Q1 2024", "Q2", "first quarter 2023")
            var quarterMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery, @"(?:q|quarter)\s*([1-4])(?:\s+(\d{4}))?");
            if (quarterMatch.Success)
            {
                var quarter = int.Parse(quarterMatch.Groups[1].Value);
                var year = quarterMatch.Groups[2].Success ? int.Parse(quarterMatch.Groups[2].Value) : now.Year;

                var startMonth = (quarter - 1) * 3 + 1;
                var startDate = new DateTime(year, startMonth, 1);
                var endDate = startDate.AddMonths(3).AddDays(-1);

                _logger.LogInformation("Date range filter: Q{Quarter} {Year} = {StartDate} to {EndDate}", quarter, year, startDate, endDate);
                return (startDate, endDate);
            }

            // 10h. Check for "first quarter", "second quarter", etc.
            if (lowerQuery.Contains("first quarter"))
            {
                var startDate = new DateTime(now.Year, 1, 1);
                var endDate = new DateTime(now.Year, 3, 31);
                _logger.LogInformation("Date range filter: first quarter = {StartDate} to {EndDate}", startDate, endDate);
                return (startDate, endDate);
            }
            if (lowerQuery.Contains("second quarter"))
            {
                var startDate = new DateTime(now.Year, 4, 1);
                var endDate = new DateTime(now.Year, 6, 30);
                _logger.LogInformation("Date range filter: second quarter = {StartDate} to {EndDate}", startDate, endDate);
                return (startDate, endDate);
            }
            if (lowerQuery.Contains("third quarter"))
            {
                var startDate = new DateTime(now.Year, 7, 1);
                var endDate = new DateTime(now.Year, 9, 30);
                _logger.LogInformation("Date range filter: third quarter = {StartDate} to {EndDate}", startDate, endDate);
                return (startDate, endDate);
            }
            if (lowerQuery.Contains("fourth quarter"))
            {
                var startDate = new DateTime(now.Year, 10, 1);
                var endDate = new DateTime(now.Year, 12, 31);
                _logger.LogInformation("Date range filter: fourth quarter = {StartDate} to {EndDate}", startDate, endDate);
                return (startDate, endDate);
            }

            // 10i. Check for specific weeks (e.g., "week 1", "week 42 of 2024")
            var weekMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery, @"week\s+(\d+)(?:\s+of\s+(\d{4}))?");
            if (weekMatch.Success)
            {
                var weekNumber = int.Parse(weekMatch.Groups[1].Value);
                var year = weekMatch.Groups[2].Success ? int.Parse(weekMatch.Groups[2].Value) : now.Year;

                if (weekNumber >= 1 && weekNumber <= 53)
                {
                    var jan1 = new DateTime(year, 1, 1);
                    var daysOffset = (weekNumber - 1) * 7;
                    var startDate = jan1.AddDays(daysOffset - (int)jan1.DayOfWeek);
                    var endDate = startDate.AddDays(6);

                    _logger.LogInformation("Date range filter: week {Week} of {Year} = {StartDate} to {EndDate}", weekNumber, year, startDate, endDate);
                    return (startDate, endDate);
                }
            }

            // 10j. Check for "week of [date]" (e.g., "week of October 3")
            var weekOfMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery, @"week\s+of\s+(.+)");
            if (weekOfMatch.Success)
            {
                var dateString = weekOfMatch.Groups[1].Value.Trim();
                var parsedDate = ParseDateFromString(dateString, now);
                if (parsedDate.HasValue)
                {
                    var dayOfWeek = (int)parsedDate.Value.DayOfWeek;
                    var startOfWeek = parsedDate.Value.AddDays(-dayOfWeek); // Start on Sunday
                    var endOfWeek = startOfWeek.AddDays(6);

                    _logger.LogInformation("Date range filter: week of {Date} = {StartDate} to {EndDate}", parsedDate.Value, startOfWeek, endOfWeek);
                    return (startOfWeek, endOfWeek);
                }
            }

            // 10k. Check for specific month and year (e.g., "October 2024", "Jan 2023", "last October")
            var monthNames = new Dictionary<string, int>
            {
                { "january", 1 }, { "jan", 1 },
                { "february", 2 }, { "feb", 2 },
                { "march", 3 }, { "mar", 3 },
                { "april", 4 }, { "apr", 4 },
                { "may", 5 },
                { "june", 6 }, { "jun", 6 },
                { "july", 7 }, { "jul", 7 },
                { "august", 8 }, { "aug", 8 },
                { "september", 9 }, { "sep", 9 }, { "sept", 9 },
                { "october", 10 }, { "oct", 10 },
                { "november", 11 }, { "nov", 11 },
                { "december", 12 }, { "dec", 12 }
            };

            // Check for "last [month]" (e.g., "last October", "last Jan")
            foreach (var monthKvp in monthNames)
            {
                var monthName = monthKvp.Key;
                var monthNum = monthKvp.Value;
                var pattern = $@"last\s+{monthName}";
                var match = System.Text.RegularExpressions.Regex.Match(lowerQuery, pattern);
                if (match.Success)
                {
                    // Find the most recent occurrence of this month
                    var targetYear = now.Year;
                    if (now.Month < monthNum)
                    {
                        targetYear = now.Year - 1; // Last year if the month hasn't occurred this year yet
                    }
                    else if (now.Month == monthNum && now.Day < 15)
                    {
                        targetYear = now.Year - 1; // If we're early in the current month, "last October" means last year
                    }

                    var startDate = new DateTime(targetYear, monthNum, 1);
                    var endDate = new DateTime(targetYear, monthNum, DateTime.DaysInMonth(targetYear, monthNum), 23, 59, 59);
                    _logger.LogInformation("Date range filter: last {Month} = {StartDate} to {EndDate}", monthName, startDate, endDate);
                    return (startDate, endDate);
                }
            }

            // Check for specific month with optional year (e.g., "October 2024", "Jan 2023", "September")
            foreach (var monthKvp in monthNames)
            {
                var monthName = monthKvp.Key;
                var monthNum = monthKvp.Value;

                // Pattern: "October 2024" or "Oct 2024"
                var monthYearPattern = $@"{monthName}\s+(\d{{4}})";
                var monthYearMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery, monthYearPattern);
                if (monthYearMatch.Success)
                {
                    var year = int.Parse(monthYearMatch.Groups[1].Value);
                    var startDate = new DateTime(year, monthNum, 1);
                    var endDate = new DateTime(year, monthNum, DateTime.DaysInMonth(year, monthNum), 23, 59, 59);
                    _logger.LogInformation("Date range filter: {Month} {Year} = {StartDate} to {EndDate}", monthName, year, startDate, endDate);
                    return (startDate, endDate);
                }

                // Pattern: just month name without year (e.g., "October", "September") - use current year
                // But avoid matching if it's part of a date like "October 3"
                var justMonthPattern = $@"\b{monthName}\b(?!\s+\d{{1,2}})";
                var justMonthMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery, justMonthPattern);
                if (justMonthMatch.Success && !lowerQuery.Contains("last " + monthName))
                {
                    var year = now.Year;
                    var startDate = new DateTime(year, monthNum, 1);
                    var endDate = new DateTime(year, monthNum, DateTime.DaysInMonth(year, monthNum), 23, 59, 59);
                    _logger.LogInformation("Date range filter: {Month} (current year) = {StartDate} to {EndDate}", monthName, startDate, endDate);
                    return (startDate, endDate);
                }
            }

            // 11. Check for month names with day (e.g., "oct 3", "october 10", "3rd october", "10 oct")
            foreach (var monthKvp in monthNames)
            {
                var monthName = monthKvp.Key;
                var monthNum = monthKvp.Value;

                // Pattern: "oct 3", "october 10"
                var pattern1 = $@"{monthName}\s+(\d{{1,2}})(?:st|nd|rd|th)?(?:,?\s*(\d{{4}}))?";
                var match1 = System.Text.RegularExpressions.Regex.Match(lowerQuery, pattern1);
                if (match1.Success)
                {
                    var day = int.Parse(match1.Groups[1].Value);
                    var year = match1.Groups[2].Success ? int.Parse(match1.Groups[2].Value) : now.Year;
                    var date = new DateTime(year, monthNum, day);
                    _logger.LogInformation("Date filter: {Month} {Day}, {Year} = {Date}", monthName, day, year, date);
                    return (date, date);
                }

                // Pattern: "3rd october", "10 oct"
                var pattern2 = $@"(\d{{1,2}})(?:st|nd|rd|th)?\s+{monthName}(?:,?\s*(\d{{4}}))?";
                var match2 = System.Text.RegularExpressions.Regex.Match(lowerQuery, pattern2);
                if (match2.Success)
                {
                    var day = int.Parse(match2.Groups[1].Value);
                    var year = match2.Groups[2].Success ? int.Parse(match2.Groups[2].Value) : now.Year;
                    var date = new DateTime(year, monthNum, day);
                    _logger.LogInformation("Date filter: {Day} {Month}, {Year} = {Date}", day, monthName, year, date);
                    return (date, date);
                }
            }

            // 12. Try to extract specific dates (dd-mm-yyyy, dd/mm/yyyy, yyyy-mm-dd formats)
            // IMPORTANT: This handles ambiguous dates like "09-10-2025" by trying both interpretations
            var datePatterns = new[]
            {
                @"\b(\d{1,2})[-/](\d{1,2})[-/](\d{4})\b", // dd-mm-yyyy or mm-dd-yyyy
                @"\b(\d{4})[-/](\d{1,2})[-/](\d{1,2})\b"  // yyyy-mm-dd
            };

            foreach (var pattern in datePatterns)
            {
                var match = System.Text.RegularExpressions.Regex.Match(query, pattern);
                if (match.Success)
                {
                    try
                    {
                        var num1 = int.Parse(match.Groups[1].Value);
                        var num2 = int.Parse(match.Groups[2].Value);
                        var num3 = int.Parse(match.Groups[3].Value);

                        // Check if it's yyyy-mm-dd format (year is > 31)
                        if (num1 > 31)
                        {
                            var year = num1;
                            var month = num2;
                            var day = num3;

                            if (month >= 1 && month <= 12 && day >= 1 && day <= 31)
                            {
                                var date = new DateTime(year, month, day);
                                _logger.LogInformation("Date filter: specific date (yyyy-mm-dd) = {Date} ({DateString})", date, date.ToString("yyyy-MM-dd"));
                                return (date, date);
                            }
                        }
                        // For ambiguous dates (e.g., 09-10-2025), try both dd-mm-yyyy and mm-dd-yyyy
                        else
                        {
                            DateTime? parsedDate = null;
                            string parsedFormat = "";

                            // Try dd-mm-yyyy format first (European: day-month-year)
                            if (num1 >= 1 && num1 <= 31 && num2 >= 1 && num2 <= 12)
                            {
                                try
                                {
                                    parsedDate = new DateTime(num3, num2, num1);
                                    parsedFormat = "dd-MM-yyyy";
                                    _logger.LogInformation("Date filter: parsed as dd-MM-yyyy = {Date} ({DateString})", parsedDate, parsedDate.Value.ToString("yyyy-MM-dd"));
                                }
                                catch
                                {
                                    // Invalid date for this format, try next
                                }
                            }

                            // If dd-mm-yyyy didn't work or is ambiguous, try mm-dd-yyyy (American: month-day-year)
                            if (!parsedDate.HasValue && num1 >= 1 && num1 <= 12 && num2 >= 1 && num2 <= 31)
                            {
                                try
                                {
                                    parsedDate = new DateTime(num3, num1, num2);
                                    parsedFormat = "MM-dd-yyyy";
                                    _logger.LogInformation("Date filter: parsed as MM-dd-yyyy = {Date} ({DateString})", parsedDate, parsedDate.Value.ToString("yyyy-MM-dd"));
                                }
                                catch
                                {
                                    // Invalid date for this format
                                }
                            }

                            if (parsedDate.HasValue)
                            {
                                _logger.LogInformation("✅ Successfully parsed date: {Date} using format {Format}",
                                    parsedDate.Value.ToString("yyyy-MM-dd"), parsedFormat);
                                return (parsedDate.Value, parsedDate.Value);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse date from pattern: {Pattern}", match.Value);
                    }
                }
            }

            return (null, null);
        }

        /// <summary>
        /// Parse date from natural language string with support for all common date formats
        /// Supports: MM/DD/YYYY, DD/MM/YYYY, YYYY-MM-DD, ISO 8601, compact formats, month names, etc.
        ///
        /// Supported formats:
        /// - ISO 8601: 2025-10-16, 2025-10-16T14:30:00, 2025-10-16T14:30:00Z
        /// - US format: 10/16/2025, 10-16-2025
        /// - UK/European format: 16/10/2025, 16-10-2025, 16.10.2025
        /// - Full date: Thursday, October 16, 2025
        /// - Short month: 16 Oct 2025, Oct 16 2025
        /// - Abbreviated: 16-Oct-2025 2:30 PM, 16-Oct-2025
        /// - Compact: 20251016, 20251016143000
        /// - Short year: 16/10/25, 16-10-25
        /// </summary>
        private DateTime? ParseDateFromString(string dateString, DateTime referenceDate)
        {
            if (string.IsNullOrWhiteSpace(dateString))
                return null;

            var originalString = dateString;
            dateString = dateString.Trim().ToLowerInvariant();

            // Handle month names with day and year
            var monthNames = new Dictionary<string, int>
            {
                { "january", 1 }, { "jan", 1 },
                { "february", 2 }, { "feb", 2 },
                { "march", 3 }, { "mar", 3 },
                { "april", 4 }, { "apr", 4 },
                { "may", 5 },
                { "june", 6 }, { "jun", 6 },
                { "july", 7 }, { "jul", 7 },
                { "august", 8 }, { "aug", 8 },
                { "september", 9 }, { "sep", 9 }, { "sept", 9 },
                { "october", 10 }, { "oct", 10 },
                { "november", 11 }, { "nov", 11 },
                { "december", 12 }, { "dec", 12 }
            };

            // 1. Try ISO 8601 formats first (highest priority for unambiguous dates)
            // Format: 2025-10-16T14:30:00Z or 2025-10-16T14:30:00 or 2025-10-16
            var iso8601Formats = new[]
            {
                "yyyy-MM-ddTHH:mm:ssZ",
                "yyyy-MM-ddTHH:mm:ss",
                "yyyy-MM-ddTHH:mm:ss.fffZ",
                "yyyy-MM-ddTHH:mm:ss.fff",
                "yyyy-MM-dd HH:mm:ss",
                "yyyy-MM-dd",
                "yyyy/MM/dd"
            };

            foreach (var format in iso8601Formats)
            {
                if (DateTime.TryParseExact(originalString, format,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var parsedDate))
                {
                    _logger.LogDebug("Parsed date '{DateString}' using ISO 8601 format '{Format}' -> {ParsedDate}",
                        originalString, format, parsedDate);
                    return parsedDate;
                }
            }

            // 2. Try compact formats: 20251016 (yyyyMMdd) or 20251016143000 (yyyyMMddHHmmss)
            var compactPattern = @"^(\d{8})(\d{6})?$";
            var compactMatch = System.Text.RegularExpressions.Regex.Match(originalString, compactPattern);
            if (compactMatch.Success)
            {
                var dateFormats = compactMatch.Groups[2].Success
                    ? new[] { "yyyyMMddHHmmss" }
                    : new[] { "yyyyMMdd" };

                foreach (var format in dateFormats)
                {
                    if (DateTime.TryParseExact(originalString, format,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var parsedDate))
                    {
                        _logger.LogDebug("Parsed date '{DateString}' using compact format '{Format}' -> {ParsedDate}",
                            originalString, format, parsedDate);
                        return parsedDate;
                    }
                }
            }

            // 3. Try full date format: "Thursday, October 16, 2025"
            var dayNames = new[] { "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday" };
            foreach (var dayName in dayNames)
            {
                if (dateString.StartsWith(dayName))
                {
                    // Remove day name and comma, then try to parse the rest
                    var withoutDay = dateString.Replace(dayName, "").TrimStart(',', ' ');
                    var parsedDate = ParseDateFromString(withoutDay, referenceDate);
                    if (parsedDate.HasValue)
                    {
                        _logger.LogDebug("Parsed full date '{DateString}' -> {ParsedDate}", originalString, parsedDate.Value);
                        return parsedDate.Value;
                    }
                }
            }

            // 4. Try month name patterns with various formats
            foreach (var monthKvp in monthNames)
            {
                var monthName = monthKvp.Key;
                var monthNum = monthKvp.Value;

                // Pattern 1: "16 Oct 2025" or "16-Oct-2025" or "16-Oct-2025 2:30 PM"
                var pattern1 = $@"(\d{{1,2}})[\s\-]{{1}}{monthName}[\s\-]{{1}}(\d{{2,4}})(?:\s+(\d{{1,2}}):(\d{{2}})\s*(am|pm)?)?";
                var match1 = System.Text.RegularExpressions.Regex.Match(dateString, pattern1);
                if (match1.Success)
                {
                    var day = int.Parse(match1.Groups[1].Value);
                    var yearStr = match1.Groups[2].Value;
                    var year = yearStr.Length == 2 ? 2000 + int.Parse(yearStr) : int.Parse(yearStr);

                    if (match1.Groups[3].Success) // Has time component
                    {
                        var hour = int.Parse(match1.Groups[3].Value);
                        var minute = int.Parse(match1.Groups[4].Value);
                        var ampm = match1.Groups[5].Success ? match1.Groups[5].Value : "";

                        // Convert to 24-hour format if AM/PM specified
                        if (!string.IsNullOrEmpty(ampm))
                        {
                            if (ampm == "pm" && hour != 12) hour += 12;
                            if (ampm == "am" && hour == 12) hour = 0;
                        }

                        var dateWithTime = new DateTime(year, monthNum, day, hour, minute, 0);
                        _logger.LogDebug("Parsed date '{DateString}' as {Day}-{Month}-{Year} with time -> {ParsedDate}",
                            originalString, day, monthName, year, dateWithTime);
                        return dateWithTime;
                    }

                    var date = new DateTime(year, monthNum, day);
                    _logger.LogDebug("Parsed date '{DateString}' as {Day}-{Month}-{Year} -> {ParsedDate}",
                        originalString, day, monthName, year, date);
                    return date;
                }

                // Pattern 2: "Oct 16 2025" or "October 16, 2025"
                var pattern2 = $@"{monthName}\s+(\d{{1,2}})(?:st|nd|rd|th)?(?:,?\s*(\d{{2,4}}))?";
                var match2 = System.Text.RegularExpressions.Regex.Match(dateString, pattern2);
                if (match2.Success)
                {
                    var day = int.Parse(match2.Groups[1].Value);
                    var yearStr = match2.Groups[2].Success ? match2.Groups[2].Value : referenceDate.Year.ToString();
                    var year = yearStr.Length == 2 ? 2000 + int.Parse(yearStr) : int.Parse(yearStr);

                    var date = new DateTime(year, monthNum, day);
                    _logger.LogDebug("Parsed date '{DateString}' as {Month} {Day}, {Year} -> {ParsedDate}",
                        originalString, monthName, day, year, date);
                    return date;
                }
            }

            // 5. Try explicit date format patterns with dots: 16.10.2025
            var dotFormatPatterns = new[]
            {
                "dd.MM.yyyy", "d.M.yyyy", "dd.MM.yy", "d.M.yy"
            };

            foreach (var format in dotFormatPatterns)
            {
                if (DateTime.TryParseExact(originalString, format,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var parsedDate))
                {
                    _logger.LogDebug("Parsed date '{DateString}' using dot format '{Format}' -> {ParsedDate}",
                        originalString, format, parsedDate);
                    return parsedDate;
                }
            }

            // 6. Try standard date formats (MM/DD/YYYY, DD/MM/YYYY, etc.)
            var standardFormats = new[]
            {
                // 4-digit year formats
                "MM/dd/yyyy", "M/d/yyyy", "MM-dd-yyyy", "M-d-yyyy",
                "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy",
                // With time
                "MM/dd/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm:ss", "M/d/yyyy h:mm tt", "d/M/yyyy h:mm tt",
                "MM-dd-yyyy h:mm tt", "dd-MM-yyyy h:mm tt",
                // 2-digit year formats
                "MM/dd/yy", "M/d/yy", "MM-dd-yy", "M-d-yy",
                "dd/MM/yy", "d/M/yy", "dd-MM-yy", "d-M-yy"
            };

            foreach (var format in standardFormats)
            {
                if (DateTime.TryParseExact(originalString, format,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var parsedDate))
                {
                    _logger.LogDebug("Parsed date '{DateString}' using standard format '{Format}' -> {ParsedDate}",
                        originalString, format, parsedDate);
                    return parsedDate;
                }
            }

            // 7. Try ambiguous patterns with 4-digit year (DD/MM/YYYY vs MM/DD/YYYY)
            var ambiguousPattern4 = @"^(\d{1,2})[/-](\d{1,2})[/-](\d{4})$";
            var ambiguousMatch4 = System.Text.RegularExpressions.Regex.Match(originalString, ambiguousPattern4);
            if (ambiguousMatch4.Success)
            {
                var num1 = int.Parse(ambiguousMatch4.Groups[1].Value);
                var num2 = int.Parse(ambiguousMatch4.Groups[2].Value);
                var year = int.Parse(ambiguousMatch4.Groups[3].Value);

                // If first number is > 12, it must be day (DD/MM/YYYY)
                if (num1 > 12 && num2 >= 1 && num2 <= 12)
                {
                    try
                    {
                        var date = new DateTime(year, num2, num1);
                        _logger.LogDebug("Parsed ambiguous date '{DateString}' as DD/MM/YYYY -> {ParsedDate}", originalString, date);
                        return date;
                    }
                    catch { }
                }

                // If second number is > 12, it must be day (MM/DD/YYYY)
                if (num2 > 12 && num1 >= 1 && num1 <= 12)
                {
                    try
                    {
                        var date = new DateTime(year, num1, num2);
                        _logger.LogDebug("Parsed ambiguous date '{DateString}' as MM/DD/YYYY -> {ParsedDate}", originalString, date);
                        return date;
                    }
                    catch { }
                }

                // Both numbers are <= 12, ambiguous - try DD/MM/YYYY first (European format)
                if (num1 >= 1 && num1 <= 31 && num2 >= 1 && num2 <= 12)
                {
                    try
                    {
                        var date = new DateTime(year, num2, num1);
                        _logger.LogDebug("Parsed ambiguous date '{DateString}' as DD/MM/YYYY (default) -> {ParsedDate}", originalString, date);
                        return date;
                    }
                    catch
                    {
                        // DD/MM/YYYY failed, try MM/DD/YYYY as fallback
                        if (num1 >= 1 && num1 <= 12 && num2 >= 1 && num2 <= 31)
                        {
                            try
                            {
                                var date = new DateTime(year, num1, num2);
                                _logger.LogDebug("Parsed ambiguous date '{DateString}' as MM/DD/YYYY (fallback) -> {ParsedDate}", originalString, date);
                                return date;
                            }
                            catch { }
                        }
                    }
                }
            }

            // 8. Try ambiguous patterns with 2-digit year (DD/MM/YY vs MM/DD/YY)
            var ambiguousPattern2 = @"^(\d{1,2})[/-](\d{1,2})[/-](\d{2})$";
            var ambiguousMatch2 = System.Text.RegularExpressions.Regex.Match(originalString, ambiguousPattern2);
            if (ambiguousMatch2.Success)
            {
                var num1 = int.Parse(ambiguousMatch2.Groups[1].Value);
                var num2 = int.Parse(ambiguousMatch2.Groups[2].Value);
                var yearShort = int.Parse(ambiguousMatch2.Groups[3].Value);
                var year = yearShort >= 0 && yearShort <= 99 ? 2000 + yearShort : yearShort;

                // If first number is > 12, it must be day (DD/MM/YY)
                if (num1 > 12 && num2 >= 1 && num2 <= 12)
                {
                    try
                    {
                        var date = new DateTime(year, num2, num1);
                        _logger.LogDebug("Parsed ambiguous date '{DateString}' as DD/MM/YY -> {ParsedDate}", originalString, date);
                        return date;
                    }
                    catch { }
                }

                // If second number is > 12, it must be day (MM/DD/YY)
                if (num2 > 12 && num1 >= 1 && num1 <= 12)
                {
                    try
                    {
                        var date = new DateTime(year, num1, num2);
                        _logger.LogDebug("Parsed ambiguous date '{DateString}' as MM/DD/YY -> {ParsedDate}", originalString, date);
                        return date;
                    }
                    catch { }
                }

                // Both numbers are <= 12, ambiguous - try DD/MM/YY first (European format)
                if (num1 >= 1 && num1 <= 31 && num2 >= 1 && num2 <= 12)
                {
                    try
                    {
                        var date = new DateTime(year, num2, num1);
                        _logger.LogDebug("Parsed ambiguous date '{DateString}' as DD/MM/YY (default) -> {ParsedDate}", originalString, date);
                        return date;
                    }
                    catch
                    {
                        // DD/MM/YY failed, try MM/DD/YY as fallback
                        if (num1 >= 1 && num1 <= 12 && num2 >= 1 && num2 <= 31)
                        {
                            try
                            {
                                var date = new DateTime(year, num1, num2);
                                _logger.LogDebug("Parsed ambiguous date '{DateString}' as MM/DD/YY (fallback) -> {ParsedDate}", originalString, date);
                                return date;
                            }
                            catch { }
                        }
                    }
                }
            }

            // 9. Last resort: try general date parsing
            if (DateTime.TryParse(originalString, out var result))
            {
                _logger.LogDebug("Parsed date '{DateString}' using general parser -> {ParsedDate}", originalString, result);
                return result;
            }

            _logger.LogWarning("Failed to parse date string: '{DateString}'", originalString);
            return null;
        }

        /// <summary>
        /// Extract "around" time filters from query with ±30 minute window
        /// Supports: "around noon", "around midnight", "around 3 PM", "around 15:00"
        /// </summary>
        private (DateTime startDate, DateTime endDate)? ExtractAroundTimeFilter(string lowerQuery, DateTime referenceDate)
        {
            const int DEFAULT_WINDOW_MINUTES = 30; // ±30 minutes window

            // Check for "around noon" or "around midday"
            if (lowerQuery.Contains("around noon") || lowerQuery.Contains("around midday"))
            {
                var noonTime = referenceDate.Date.AddHours(12);
                var startTime = noonTime.AddMinutes(-DEFAULT_WINDOW_MINUTES);
                var endTime = noonTime.AddMinutes(DEFAULT_WINDOW_MINUTES);
                _logger.LogInformation("Time filter: around noon = {StartTime} to {EndTime}", startTime, endTime);
                return (startTime, endTime);
            }

            // Check for "around midnight"
            if (lowerQuery.Contains("around midnight"))
            {
                var midnightTime = referenceDate.Date; // 00:00
                var startTime = midnightTime.AddMinutes(-DEFAULT_WINDOW_MINUTES); // Previous day 23:30
                var endTime = midnightTime.AddMinutes(DEFAULT_WINDOW_MINUTES); // Today 00:30
                _logger.LogInformation("Time filter: around midnight = {StartTime} to {EndTime}", startTime, endTime);
                return (startTime, endTime);
            }

            // Check for "morning" - full morning period (5 AM to 12 PM)
            if (lowerQuery.Contains("morning") || lowerQuery.Contains("in the morning"))
            {
                var morningStart = referenceDate.Date.AddHours(5); // 5:00 AM
                var morningEnd = referenceDate.Date.AddHours(12); // 12:00 PM
                _logger.LogInformation("Time filter: morning = {StartTime} to {EndTime}", morningStart, morningEnd);
                return (morningStart, morningEnd);
            }

            // Check for "afternoon" - full afternoon period (12 PM to 5 PM)
            if (lowerQuery.Contains("afternoon") || lowerQuery.Contains("in the afternoon"))
            {
                var afternoonStart = referenceDate.Date.AddHours(12); // 12:00 PM
                var afternoonEnd = referenceDate.Date.AddHours(17); // 5:00 PM
                _logger.LogInformation("Time filter: afternoon = {StartTime} to {EndTime}", afternoonStart, afternoonEnd);
                return (afternoonStart, afternoonEnd);
            }

            // Check for "evening" - full evening period (5 PM to 9 PM)
            if (lowerQuery.Contains("evening") || lowerQuery.Contains("in the evening"))
            {
                var eveningStart = referenceDate.Date.AddHours(17); // 5:00 PM
                var eveningEnd = referenceDate.Date.AddHours(21); // 9:00 PM
                _logger.LogInformation("Time filter: evening = {StartTime} to {EndTime}", eveningStart, eveningEnd);
                return (eveningStart, eveningEnd);
            }

            // Check for "night" - full night period (9 PM to 5 AM next day)
            if (lowerQuery.Contains("night") || lowerQuery.Contains("at night"))
            {
                var nightStart = referenceDate.Date.AddHours(21); // 9:00 PM
                var nightEnd = referenceDate.Date.AddDays(1).AddHours(5); // 5:00 AM next day
                _logger.LogInformation("Time filter: night = {StartTime} to {EndTime}", nightStart, nightEnd);
                return (nightStart, nightEnd);
            }

            // Check for "around [specific time]" (e.g., "around 3 PM", "around 15:00", "around 3:45 PM")
            var aroundTimeMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery, @"around\s+(\d{1,2})(?::(\d{2}))?\s*(am|pm)?");
            if (aroundTimeMatch.Success)
            {
                var hour = int.Parse(aroundTimeMatch.Groups[1].Value);
                var minute = aroundTimeMatch.Groups[2].Success ? int.Parse(aroundTimeMatch.Groups[2].Value) : 0;
                var ampm = aroundTimeMatch.Groups[3].Success ? aroundTimeMatch.Groups[3].Value.ToLowerInvariant() : "";

                // Convert to 24-hour format if AM/PM specified
                if (!string.IsNullOrEmpty(ampm))
                {
                    if (ampm == "pm" && hour != 12) hour += 12;
                    if (ampm == "am" && hour == 12) hour = 0;
                }

                var targetTime = referenceDate.Date.AddHours(hour).AddMinutes(minute);
                var startTime = targetTime.AddMinutes(-DEFAULT_WINDOW_MINUTES);
                var endTime = targetTime.AddMinutes(DEFAULT_WINDOW_MINUTES);
                _logger.LogInformation("Time filter: around {TargetTime} = {StartTime} to {EndTime}", targetTime, startTime, endTime);
                return (startTime, endTime);
            }

            return null;
        }

        /// <summary>
        /// Extract file type filters from query
        /// </summary>
        private List<string> ExtractFileTypeFilters(string query)
        {
            var lowerQuery = query.ToLowerInvariant();
            var fileTypes = new List<string>();

            // Check for specific file type mentions
            if (lowerQuery.Contains("pdf"))
                fileTypes.Add("pdf");
            
            if (lowerQuery.Contains("word") || lowerQuery.Contains("docx") || lowerQuery.Contains("doc"))
            {
                fileTypes.AddRange(new[] { "docx", "doc" });
            }
            
            if (lowerQuery.Contains("excel") || lowerQuery.Contains("xlsx") || lowerQuery.Contains("xls"))
            {
                fileTypes.AddRange(new[] { "xlsx", "xls" });
            }
            
            if (lowerQuery.Contains("powerpoint") || lowerQuery.Contains("pptx") || lowerQuery.Contains("ppt"))
            {
                fileTypes.AddRange(new[] { "pptx", "ppt" });
            }

            if (lowerQuery.Contains("text") || lowerQuery.Contains("txt"))
                fileTypes.Add("txt");

            return fileTypes.Distinct().ToList();
        }

        /// <summary>
        /// Check if query is asking for earliest/latest records
        /// </summary>
        private (bool isEarliest, bool isLatest) ExtractSortingIntent(string query)
        {
            var lowerQuery = query.ToLowerInvariant();
            
            var isEarliest = lowerQuery.Contains("earliest") || lowerQuery.Contains("oldest") || 
                           lowerQuery.Contains("first created");
            
            var isLatest = lowerQuery.Contains("latest") || lowerQuery.Contains("newest") || 
                         lowerQuery.Contains("most recent") || lowerQuery.Contains("recently created");

            return (isEarliest, isLatest);
        }
        
        /// <summary>
        /// Apply date range filter to results by comparing date_created field
        /// Supports filtering by start date, end date, or both (including time)
        /// </summary>
        private List<(string id, float similarity, Dictionary<string, object> metadata)> ApplyDateRangeFilter(
            List<(string id, float similarity, Dictionary<string, object> metadata)> results,
            DateTime? startDate,
            DateTime? endDate)
        {
            _logger.LogInformation("🔍 ApplyDateRangeFilter called with: StartDate={StartDate}, EndDate={EndDate}, TotalResults={Count}",
                startDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "null",
                endDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "null",
                results.Count);

            var filteredResults = results.Where(result =>
            {
                var dateCreated = GetMetadataValue<string>(result.metadata, "date_created");
                if (string.IsNullOrEmpty(dateCreated))
                {
                    _logger.LogInformation("❌ Record has no date_created field");
                    return false;
                }

                // Try to parse the date_created field
                // IMPORTANT: MM/dd/yyyy must come FIRST since dates in Qdrant are stored in MM/DD/YYYY format
                var dateFormats = new[] { "MM/dd/yyyy", "M/d/yyyy", "MM/dd/yyyy HH:mm:ss",
                                        "dd/MM/yyyy", "dd-MM-yyyy", "d/M/yyyy", "d-M-yyyy", "dd/MM/yyyy HH:mm:ss",
                                        "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss" };
                DateTime parsedDate = default;
                bool dateParseSuccess = false;
                string usedFormat = "";

                foreach (var format in dateFormats)
                {
                    if (DateTime.TryParseExact(dateCreated, format, null, System.Globalization.DateTimeStyles.None, out parsedDate))
                    {
                        dateParseSuccess = true;
                        usedFormat = format;
                        break;
                    }
                }

                // Fallback: try general parse
                if (!dateParseSuccess)
                {
                    dateParseSuccess = DateTime.TryParse(dateCreated, out parsedDate);
                    if (dateParseSuccess)
                        usedFormat = "General parse";
                }

                if (!dateParseSuccess)
                {
                    _logger.LogWarning("❌ Failed to parse date_created: '{DateCreated}'", dateCreated);
                    return false;
                }

                _logger.LogInformation("✅ Parsed date_created: '{DateCreated}' -> {ParsedDate} (format: {Format})",
                    dateCreated, parsedDate.ToString("yyyy-MM-dd HH:mm:ss"), usedFormat);

                // Apply date range filtering
                bool withinRange = true;

                if (startDate.HasValue)
                {
                    // If startDate has time component, use exact DateTime comparison
                    // Otherwise, use date-only comparison
                    if (startDate.Value.TimeOfDay != TimeSpan.Zero)
                    {
                        withinRange &= parsedDate >= startDate.Value;
                        _logger.LogInformation("  StartDate check: {ParsedDate} >= {StartDate} = {Result}",
                            parsedDate, startDate.Value, parsedDate >= startDate.Value);
                    }
                    else
                    {
                        withinRange &= parsedDate.Date >= startDate.Value.Date;
                        _logger.LogInformation("  StartDate check (date only): {ParsedDate} >= {StartDate} = {Result}",
                            parsedDate.Date, startDate.Value.Date, parsedDate.Date >= startDate.Value.Date);
                    }
                }

                if (endDate.HasValue)
                {
                    // If endDate has time component, use exact DateTime comparison
                    // Otherwise, use date-only comparison
                    if (endDate.Value.TimeOfDay != TimeSpan.Zero)
                    {
                        withinRange &= parsedDate <= endDate.Value;
                        _logger.LogInformation("  EndDate check: {ParsedDate} <= {EndDate} = {Result}",
                            parsedDate, endDate.Value, parsedDate <= endDate.Value);
                    }
                    else
                    {
                        withinRange &= parsedDate.Date <= endDate.Value.Date;
                        _logger.LogInformation("  EndDate check (date only): {ParsedDate} <= {EndDate} = {Result}",
                            parsedDate.Date, endDate.Value.Date, parsedDate.Date <= endDate.Value.Date);
                    }
                }

                return withinRange;
            }).ToList();

            _logger.LogInformation("✅ Date filtering complete: {FilteredCount} / {TotalCount} records match date criteria",
                filteredResults.Count, results.Count);

            return filteredResults;
        }

        /// <summary>
        /// Apply metadata filters to search results
        /// </summary>
        private List<(string id, float similarity, Dictionary<string, object> metadata)> ApplyMetadataFilters(
            List<(string id, float similarity, Dictionary<string, object> metadata)> results,
            Dictionary<string, object> filters)
        {
            return results.Where(result =>
            {
                foreach (var filter in filters)
                {
                    var safeKey = $"meta_{MakeSafeKey(filter.Key)}";

                    if (!result.metadata.ContainsKey(safeKey))
                        return false;

                    var metadataValue = result.metadata[safeKey]?.ToString() ?? "";
                    var filterValue = filter.Value?.ToString() ?? "";

                    // Perform case-insensitive comparison
                    if (!metadataValue.Equals(filterValue, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                return true;
            }).ToList();
        }

        /// <summary>
        /// Apply file type filter to search results based on file extensions or document types
        /// </summary>
        private List<(string id, float similarity, Dictionary<string, object> metadata)> ApplyFileTypeFilter(
            List<(string id, float similarity, Dictionary<string, object> metadata)> results,
            List<string> fileTypes)
        {
            return results.Where(result =>
            {
                // Check if the record has document content (electronic documents)
                var recordType = GetMetadataValue<string>(result.metadata, "record_type") ?? "";
                var chunkContent = GetMetadataValue<string>(result.metadata, "chunk_content") ?? "";
                var recordTitle = GetMetadataValue<string>(result.metadata, "record_title") ?? "";
                var fileExtension = GetMetadataValue<string>(result.metadata, "file_extension") ?? "";
                var storedFileType = GetMetadataValue<string>(result.metadata, "file_type") ?? "";
                var documentCategory = GetMetadataValue<string>(result.metadata, "document_category") ?? "";

                // Skip containers - we only want document files
                if (recordType.Equals("Container", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // Check file extension from metadata first (most reliable)
                foreach (var fileType in fileTypes)
                {
                    var extension = $".{fileType}";
                    
                    // Check stored metadata fields
                    if (fileExtension.Equals(extension, StringComparison.OrdinalIgnoreCase) ||
                        storedFileType.Equals(fileType, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    // Check document category
                    if (documentCategory.Contains(fileType, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    // Check if title contains the file extension
                    if (recordTitle.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    // For semantic matching, check if content mentions the file type
                    if (chunkContent.Contains(fileType, StringComparison.OrdinalIgnoreCase) ||
                        chunkContent.Contains(extension, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    // Check for common document type patterns
                    if (fileType == "pdf" && (recordTitle.Contains("pdf", StringComparison.OrdinalIgnoreCase) ||
                        chunkContent.Contains("portable document", StringComparison.OrdinalIgnoreCase) ||
                        documentCategory.Contains("PDF", StringComparison.OrdinalIgnoreCase)))
                    {
                        return true;
                    }

                    if ((fileType == "docx" || fileType == "doc") && 
                        (recordTitle.Contains("word", StringComparison.OrdinalIgnoreCase) ||
                         chunkContent.Contains("microsoft word", StringComparison.OrdinalIgnoreCase) ||
                         documentCategory.Contains("Word", StringComparison.OrdinalIgnoreCase)))
                    {
                        return true;
                    }

                    if ((fileType == "xlsx" || fileType == "xls") && 
                        (recordTitle.Contains("excel", StringComparison.OrdinalIgnoreCase) ||
                         chunkContent.Contains("microsoft excel", StringComparison.OrdinalIgnoreCase) ||
                         chunkContent.Contains("spreadsheet", StringComparison.OrdinalIgnoreCase) ||
                         documentCategory.Contains("Excel", StringComparison.OrdinalIgnoreCase)))
                    {
                        return true;
                    }

                    if ((fileType == "pptx" || fileType == "ppt") && 
                        (recordTitle.Contains("powerpoint", StringComparison.OrdinalIgnoreCase) ||
                         chunkContent.Contains("microsoft powerpoint", StringComparison.OrdinalIgnoreCase) ||
                         chunkContent.Contains("presentation", StringComparison.OrdinalIgnoreCase) ||
                         documentCategory.Contains("PowerPoint", StringComparison.OrdinalIgnoreCase)))
                    {
                        return true;
                    }
                }

                return false;
            }).ToList();
        }

        /// <summary>
        /// Apply sorting by date created to the results
        /// </summary>
        private List<(string id, float similarity, Dictionary<string, object> metadata)> ApplyDateSorting(
            List<(string id, float similarity, Dictionary<string, object> metadata)> results,
            bool earliest)
        {
            if (earliest)
            {
                // Sort by date_created ascending
                return results.OrderBy(r => GetMetadataValue<string>(r.metadata, "date_created")).ToList();
            }
            else
            {
                // Sort by date_created descending
                return results.OrderByDescending(r => GetMetadataValue<string>(r.metadata, "date_created")).ToList();
            }
        }

        /// <summary>
        /// Build a content preview from metadata
        /// </summary>
        private string BuildContentPreview(Dictionary<string, object> metadata)
        {
            var preview = new StringBuilder();

            // Add key fields
            var title = GetMetadataValue<string>(metadata, "record_title");
            var dateCreated = GetMetadataValue<string>(metadata, "date_created");
            var recordType = GetMetadataValue<string>(metadata, "record_type");
            var container = GetMetadataValue<string>(metadata, "container");
            var assignee = GetMetadataValue<string>(metadata, "assignee");

            if (!string.IsNullOrEmpty(title))
                preview.AppendLine($"Title: {title}");

            if (!string.IsNullOrEmpty(dateCreated))
                preview.AppendLine($"Created: {dateCreated}");

            if (!string.IsNullOrEmpty(recordType))
                preview.AppendLine($"Type: {recordType}");

            if (!string.IsNullOrEmpty(container))
                preview.AppendLine($"Container: {container}");

            if (!string.IsNullOrEmpty(assignee))
                preview.AppendLine($"Assignee: {assignee}");

            // Add some metadata fields
            var metaFields = metadata
                .Where(kvp => kvp.Key.StartsWith("meta_") && kvp.Value != null)
                .Take(5);

            foreach (var field in metaFields)
            {
                var fieldName = field.Key.Replace("meta_", "").Replace("_", " ");
                preview.AppendLine($"{fieldName}: {field.Value}");
            }

            return preview.ToString();
        }

        /// <summary>
        /// Generate AI synthesized answer based on search results
        /// </summary>
        private async Task<string> SynthesizeRecordAnswerAsync(string query, List<RecordSearchResultDto> results)
        {
            if (!results.Any())
                return "";

            var apiKey = _configuration["Gemini:ApiKey"];
            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.LogWarning("Gemini API key not configured");
                return "";
            }

            // Results are already deduplicated at this point, so we can use them directly
            var uniqueRecords = results
                .OrderByDescending(r => r.RelevanceScore)
                .Take(20) // Take up to 20 records for synthesis
                .ToList();

            _logger.LogInformation("Synthesizing answer from {UniqueCount} unique records", uniqueRecords.Count);

            var contextBuilder = new StringBuilder();
            contextBuilder.AppendLine("You are a helpful assistant that answers questions about Content Manager records.");
            contextBuilder.AppendLine();
            contextBuilder.AppendLine($"QUESTION: {query}");
            contextBuilder.AppendLine();
            contextBuilder.AppendLine("RECORDS FOUND:");
            contextBuilder.AppendLine("==================");

            foreach (var result in uniqueRecords)
            {
                contextBuilder.AppendLine($"--- Record URI: {result.RecordUri} (Relevance: {result.RelevanceScore:F2}) ---");
                contextBuilder.AppendLine($"Title: {result.RecordTitle}");
                contextBuilder.AppendLine($"Date Created: {result.DateCreated}");
                contextBuilder.AppendLine($"Type: {result.RecordType}");

                // Add the actual chunk content if available
                if (!string.IsNullOrWhiteSpace(result.ContentPreview))
                {
                    contextBuilder.AppendLine($"Content: {result.ContentPreview}");
                }

                // Add important metadata fields
                foreach (var meta in result.Metadata.Where(m => m.Key.StartsWith("meta_")).Take(10))
                {
                    var fieldName = meta.Key.Replace("meta_", "").Replace("_", " ");
                    contextBuilder.AppendLine($"{fieldName}: {meta.Value}");
                }

                contextBuilder.AppendLine();
            }

            contextBuilder.AppendLine("==================");
            contextBuilder.AppendLine();
            contextBuilder.AppendLine("INSTRUCTIONS:");
            contextBuilder.AppendLine("- Answer the question using the records found above");
            contextBuilder.AppendLine("- List ALL relevant records with their URIs and key information");
            contextBuilder.AppendLine("- If asked about specific dates or metadata, cite the exact values found");
            contextBuilder.AppendLine("- Be concise but comprehensive - include all matching records, not just a subset");
            contextBuilder.AppendLine();
            contextBuilder.AppendLine("ANSWER:");

            var prompt = contextBuilder.ToString();
            return await CallGeminiModelAsync(prompt);
        }

        /// <summary>
        /// Call Gemini API for text generation
        /// </summary>
        private async Task<string> CallGeminiModelAsync(string prompt)
        {
            try
            {
                var apiKey = _configuration["Gemini:ApiKey"];
                if (string.IsNullOrEmpty(apiKey))
                    return "";

                using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };

                var requestBody = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new[] { new { text = prompt } }
                        }
                    }
                };

                var jsonContent = JsonSerializer.Serialize(requestBody);
                var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                httpClient.DefaultRequestHeaders.Add("x-goog-api-key", apiKey);

                var url = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent";
                var response = await httpClient.PostAsync(url, httpContent);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var jsonResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);

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
                                return textElement.GetString() ?? "";
                            }
                        }
                    }
                }

                return "";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Gemini API");
                return "";
            }
        }

        /// <summary>
        /// Get metadata value with type conversion
        /// </summary>
        private T? GetMetadataValue<T>(Dictionary<string, object> metadata, string key)
        {
            if (!metadata.TryGetValue(key, out var value))
                return default;

            if (value is JsonElement jsonElement)
            {
                return JsonSerializer.Deserialize<T>(jsonElement.GetRawText());
            }

            try
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return default;
            }
        }

        /// <summary>
        /// Make a safe key name for metadata storage
        /// </summary>
        private string MakeSafeKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return "unknown";

            var safeKey = new string(key
                .ToLowerInvariant()
                .Select(c => char.IsLetterOrDigit(c) ? c : '_')
                .ToArray());

            while (safeKey.Contains("__"))
                safeKey = safeKey.Replace("__", "_");

            return safeKey.Trim('_');
        }

        /// <summary>
        /// Build Content Manager IDOL search string with date filters and file types
        /// CM Search String Syntax Reference (based on TRIM/Content Manager):
        /// - content:keyword - Search in indexed content (MUST use "content:" NOT "text:")
        /// - createdOn:MM/dd/yyyy - Filter by EXACT creation date (use MM/dd/yyyy format ONLY)
        /// - extension:pdf - Filter by file extension
        /// - number:* - Wildcard search
        /// - and, or, not - Boolean operators (lowercase)
        ///
        /// IMPORTANT from reference code:
        /// - For dates, ONLY use format: createdOn:MM/dd/yyyy (NO ranges, NO >= or <=)
        /// - For content, use: content:keyword (NOT text:)
        /// Reference: ApplySequentialFilters pattern (createdOn:{dateValue.Date:MM/dd/yyyy})
        /// </summary>
        private string BuildContentManagerSearchString(
            string query,
            DateTime? startDate,
            DateTime? endDate,
            List<string> fileTypeFilters)
        {
            var searchParts = new List<string>();

            // 1. Add content search using the clean query directly
            // MUST use "content:" prefix (not "text:") as per CM requirements
            if (!string.IsNullOrWhiteSpace(query))
            {
                // Remove common query words for better CM index matching
                var cleanedQuery = RemoveCommonQueryWords(query);
                if (!string.IsNullOrWhiteSpace(cleanedQuery))
                {
                    // Search in content - IMPORTANT: use "content:" not "text:"
                    searchParts.Add($"content:{cleanedQuery}");
                }
            }

            // 2. Add date filter if provided
            // IMPORTANT: Content Manager only supports exact date matching with createdOn:MM/dd/yyyy
            // NO support for ranges (>=, <=) - must use exact dates with "or" operator
            if (startDate.HasValue && endDate.HasValue)
            {
                // If same date, use single filter
                if (startDate.Value.Date == endDate.Value.Date)
                {
                    searchParts.Add($"createdOn:{startDate.Value:MM/dd/yyyy}");
                }
                else
                {
                    // For date range, list all dates between start and end with "or"
                    // This is the only way CM supports date ranges
                    var dates = new List<string>();
                    var currentDate = startDate.Value.Date;

                    // Limit to reasonable range to avoid huge queries
                    var daysDiff = (endDate.Value.Date - startDate.Value.Date).Days;
                    if (daysDiff <= 31) // Only process if range is 31 days or less
                    {
                        while (currentDate <= endDate.Value.Date)
                        {
                            dates.Add($"createdOn:{currentDate:MM/dd/yyyy}");
                            currentDate = currentDate.AddDays(1);
                        }

                        if (dates.Count > 1)
                        {
                            searchParts.Add($"({string.Join(" or ", dates)})");
                        }
                        else if (dates.Count == 1)
                        {
                            searchParts.Add(dates[0]);
                        }
                    }
                    else
                    {
                        // For ranges > 31 days, just use start date with warning
                        _logger.LogWarning("Date range too large ({Days} days). Using start date only: {StartDate}",
                            daysDiff, startDate.Value.ToString("MM/dd/yyyy"));
                        searchParts.Add($"createdOn:{startDate.Value:MM/dd/yyyy}");
                    }
                }
            }
            else if (startDate.HasValue)
            {
                // Only start date - use exact date (no range support)
                searchParts.Add($"createdOn:{startDate.Value:MM/dd/yyyy}");
            }
            else if (endDate.HasValue)
            {
                // Only end date - use exact date (no range support)
                searchParts.Add($"createdOn:{endDate.Value:MM/dd/yyyy}");
            }

            // 3. Add file type filters if provided
            if (fileTypeFilters.Any())
            {
                var extensionTerms = fileTypeFilters.Select(ft => $"extension:{ft}").ToList();
                if (extensionTerms.Count > 1)
                {
                    searchParts.Add($"({string.Join(" or ", extensionTerms)})");
                }
                else
                {
                    searchParts.Add(extensionTerms.First());
                }
            }

            // If no search parts at all, return wildcard search
            if (!searchParts.Any())
            {
                return "number:*"; // Wildcard to get all records (as per reference pattern)
            }

            // Combine all parts with "and" operator (lowercase as per CM IDOL syntax)
            return string.Join(" and ", searchParts);
        }

        /// <summary>
        /// Remove common query words that don't add value to CM index search
        /// </summary>
        private string RemoveCommonQueryWords(string query)
        {
            var stopWords = new HashSet<string>
            {
                "show", "me", "get", "find", "search", "the", "a", "an", "from", "to", "between", "and", "or",
                "records", "record", "documents", "document", "files", "file", "created", "made", "added", "uploaded",
                "on", "at", "in", "which", "is", "that", "these", "those", "with", "for", "all", "any"
            };

            var words = query.ToLowerInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => !stopWords.Contains(w))
                .ToList();

            return string.Join(" ", words);
        }

        /// <summary>
        /// Execute Content Manager IDOL index search and return candidate record URIs with details
        /// This uses the CM's native TrimMainObjectSearch for fast content-based retrieval
        /// IMPORTANT: SetSearchString can only accept ONE field at a time
        /// - Use content:{value} for content search
        /// - Use createdOn:{MM/dd/yyyy} for date search
        /// Reference pattern from ApplySequentialFilters
        /// </summary>
        /// <returns>Tuple of (URIs, RecordDetailStrings) for query enhancement</returns>
        private async Task<(HashSet<long> uris, List<string> recordDetails)> ExecuteContentManagerSearchAsync(
            string contentQuery,
            DateTime? startDate,
            DateTime? endDate,
            List<string> fileTypeFilters)
        {
            try
            {
                _logger.LogInformation("   🔍 Executing CM IDOL Index Search");
                _logger.LogInformation("      Content: {Content}", contentQuery ?? "none");
                _logger.LogInformation("      Date Range: {Start} to {End}",
                    startDate?.ToString("MM/dd/yyyy") ?? "none",
                    endDate?.ToString("MM/dd/yyyy") ?? "none");

                // Get database connection from ContentManagerServices
                var database = await _contentManagerServices.GetDatabaseAsync();

                if (database == null)
                {
                    _logger.LogError("   ❌ Database connection is not available");
                    throw new Exception("Database connection is not available");
                }

                var candidateUris = new HashSet<long>();
                var recordDetails = new List<string>();

                // Create TrimMainObjectSearch for IDOL index search
                var search = new TrimMainObjectSearch(database, BaseObjectTypes.Record);

                // IMPORTANT: SetSearchString can only accept ONE field at a time
                // Priority: Date > Content > FileType

                // Step 1: If we have a date, use ONLY date filter
                if (startDate.HasValue || endDate.HasValue)
                {
                    if (startDate.HasValue && endDate.HasValue && startDate.Value.Date == endDate.Value.Date)
                    {
                        // Single date (same start and end)
                        var dateStr = $"createdOn:{startDate.Value:MM/dd/yyyy}";
                        search.SetSearchString(dateStr);
                        _logger.LogInformation("   📋 Using single date filter: {DateFilter}", dateStr);
                    }
                    else if (startDate.HasValue && endDate.HasValue)
                    {
                        // Date range (different start and end)
                        var dateStr = $"createdOn:{startDate.Value:MM/dd/yyyy} to {endDate.Value:MM/dd/yyyy}";
                        search.SetSearchString(dateStr);
                        _logger.LogInformation("   📋 Using date range filter: {DateFilter}", dateStr);
                    }
                    else if (startDate.HasValue)
                    {
                        // Only start date
                        var dateStr = $"createdOn:{startDate.Value:MM/dd/yyyy}";
                        search.SetSearchString(dateStr);
                        _logger.LogInformation("   📋 Using start date filter: {DateFilter}", dateStr);
                    }
                    else if (endDate.HasValue)
                    {
                        // Only end date
                        var dateStr = $"createdOn:{endDate.Value:MM/dd/yyyy}";
                        search.SetSearchString(dateStr);
                        _logger.LogInformation("   📋 Using end date filter: {DateFilter}", dateStr);
                    }
                }
                // Step 2: If no date but we have content, use content filter
                else if (!string.IsNullOrWhiteSpace(contentQuery))
                {
                    // Content must be wrapped in quotes: content:"value"
                    var contentStr = $"content:\"{contentQuery}\"";
                    search.SetSearchString(contentStr);
                    _logger.LogInformation("   📋 Using content filter: {ContentFilter}", contentStr);
                }
                // Step 3: If no date or content but have file type, use file type filter
                else if (fileTypeFilters != null && fileTypeFilters.Any())
                {
                    var extensionStr = $"extension:{fileTypeFilters.First()}";
                    search.SetSearchString(extensionStr);
                    _logger.LogInformation("   📋 Using extension filter: {ExtensionFilter}", extensionStr);
                }
                else
                {
                    // No filters, use wildcard
                    search.SetSearchString("number:*");
                    _logger.LogInformation("   📋 Using wildcard filter: number:*");
                }

                _logger.LogInformation("   📊 CM Index Search initiated. Estimated count: {Count}", search.Count);

                if (search.Count == 0)
                {
                    _logger.LogInformation("   ℹ️ CM IDOL Index Search returned 0 results");
                    return (candidateUris, recordDetails);
                }

                // Iterate through search results and collect records with details
                var recordCount = 0;
                foreach (Record record in search)
                {
                    try
                    {
                        var uri = record.Uri.Value;
                        candidateUris.Add(uri);

                        // Build record information string (matching the embedding format)
                        var recordInfo = $"[Record: {record.Title} | URI: {uri} | Created: {record.DateCreated}]";
                        recordDetails.Add(recordInfo);

                        recordCount++;

                        // Log progress for large result sets
                        if (recordCount % 100 == 0)
                        {
                            _logger.LogDebug("   📊 Processed {Count} records so far...", recordCount);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "   ⚠️ Failed to process record: {Error}", ex.Message);
                        // If fetch fails, just add the URI
                        if (candidateUris.Contains(record.Uri.Value))
                        {
                            recordDetails.Add($"URI: {record.Uri.Value}");
                        }
                    }
                }

                _logger.LogInformation("   ✅ CM IDOL Index Search completed: Found {Count} unique record URIs with details",
                    candidateUris.Count);

                return (candidateUris, recordDetails);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "   ❌ Error executing CM IDOL Index search: {Message}", ex.Message);
                throw; // Re-throw to let calling code handle fallback
            }
        }
    }
}
