using DocumentProcessingAPI.Core.DTOs;
using DocumentProcessingAPI.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;

namespace DocumentProcessingAPI.Infrastructure.Services
{
    /// <summary>
    /// Service containing Google Vertex AI / Gemini related methods for record search operations
    /// Handles AI-powered keyword extraction and answer synthesis
    /// </summary>
    public class RecordSearchGoogleServices : IRecordSearchGoogleServices
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<RecordSearchGoogleServices> _logger;

        public RecordSearchGoogleServices(
            IConfiguration configuration,
            ILogger<RecordSearchGoogleServices> logger)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Call Vertex AI Generative AI API for text generation
        /// </summary>
        public async Task<string> CallGeminiModelAsync(string prompt)
        {
            try
            {
                var projectId = _configuration["VertexAI:ProjectId"];
                var location = _configuration["VertexAI:Location"] ?? "us-central1";
                var model = _configuration["VertexAI:GenerativeModel"] ?? "gemini-2.5-flash";

                if (string.IsNullOrEmpty(projectId))
                {
                    _logger.LogWarning("VertexAI ProjectId not configured");
                    return "";
                }

                _logger.LogInformation("🔄 Calling Vertex AI {Model} for text generation", model);

                // Build the endpoint URL for Vertex AI
                var endpoint = $"https://{location}-aiplatform.googleapis.com/v1/projects/{projectId}/locations/{location}/publishers/google/models/{model}:generateContent";

                using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };

                // Get access token from gcloud CLI
                var accessToken = await GetGoogleCloudAccessTokenAsync();
                if (string.IsNullOrEmpty(accessToken))
                {
                    _logger.LogError("Failed to get Google Cloud access token");
                    return "";
                }

                var requestBody = new
                {
                    contents = new[]
                    {
                        new
                        {
                            role = "user",
                            parts = new[] { new { text = prompt } }
                        }
                    },
                    generationConfig = new
                    {
                        temperature = 0.7,
                        topP = 0.95,
                        topK = 40,
                        maxOutputTokens = 8192
                    }
                };

                var jsonContent = JsonSerializer.Serialize(requestBody);
                var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");

                var response = await httpClient.PostAsync(endpoint, httpContent);

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
                                var result = textElement.GetString() ?? "";
                                _logger.LogInformation("✅ Successfully generated text from Vertex AI ({Length} chars)", result.Length);
                                return result;
                            }
                        }
                    }
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Vertex AI API error: {StatusCode} - {Error}", response.StatusCode, errorContent);
                }

                return "";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Vertex AI Generative API");
                return "";
            }
        }

        /// <summary>
        /// Get Google Cloud access token using Service Account Key
        /// </summary>
        public async Task<string> GetGoogleCloudAccessTokenAsync()
        {
            try
            {
                var serviceAccountKeyPath = _configuration["VertexAI:ServiceAccountKeyPath"];

                if (string.IsNullOrEmpty(serviceAccountKeyPath))
                {
                    _logger.LogError("VertexAI:ServiceAccountKeyPath not configured in appsettings.json");
                    throw new InvalidOperationException("Service account key path is not configured. Please set VertexAI:ServiceAccountKeyPath in appsettings.json");
                }

                if (!File.Exists(serviceAccountKeyPath))
                {
                    _logger.LogError("Service account key file not found at: {Path}", serviceAccountKeyPath);
                    throw new FileNotFoundException($"Service account key file not found at: {serviceAccountKeyPath}");
                }

                _logger.LogDebug("Loading service account credentials from: {Path}", serviceAccountKeyPath);

                // Load service account credentials
                GoogleCredential credential;
                using (var stream = new FileStream(serviceAccountKeyPath, FileMode.Open, FileAccess.Read))
                {
                    credential = GoogleCredential.FromStream(stream)
                        .CreateScoped("https://www.googleapis.com/auth/cloud-platform");
                }

                // Get access token
                var token = await credential.UnderlyingCredential.GetAccessTokenForRequestAsync();

                if (string.IsNullOrWhiteSpace(token))
                {
                    _logger.LogError("Failed to obtain access token from service account");
                    throw new InvalidOperationException("Failed to obtain access token from service account");
                }

                _logger.LogInformation("✅ Successfully obtained Google Cloud access token from service account");
                return token;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Google Cloud access token from service account");
                throw;
            }
        }

        /// <summary>
        /// Extract main search keywords from natural language queries using Gemini
        /// Returns entity names, product names, topics while excluding date/time and file type terms
        /// </summary>
        public async Task<List<string>> ExtractKeywordsWithGemini(string query)
        {
            try
            {
                _logger.LogDebug("   🔍 Extracting keywords from query using Gemini");

                // Build prompt for keyword extraction
                var prompt = @$"You are a search query analyzer. Extract the core search terms from the user's query that should be used to find matching documents in an index.

QUERY: ""{query}""

TASK:
Extract ONLY the meaningful content keywords - the specific things the user wants to find.

WHAT TO EXTRACT:
- Names (people, companies, products, projects)
- Topics and subjects
- Specific terms and phrases
- Technical terms
- Concepts and themes
- Numbers WITH context (e.g., ""17 years"", ""5+ years experience"", ""10 years"", ""2 years"")
- Any word or phrase that identifies WHAT document content to search for

WHAT TO EXCLUDE (handled elsewhere):
- Calendar dates: month names (January, February, etc.), specific dates (""October 2025"", ""2025-10-14"")
- Time references: times (""3:40 PM""), temporal words (""today"", ""yesterday"", ""recent"", ""latest"")
- File type words: PDF, Excel, Word, PowerPoint, document, file
- Action words: find, show, get, search, display, list
- Generic words: documents, records, files, items, data
- Prepositions ALONE: about, from, in, on, at, to (but keep if part of meaningful phrase)

IMPORTANT - NUMBERS WITH CONTEXT:
- DO extract: ""17 years"", ""5+ years"", ""10 years experience"", ""2 years"" (these describe content attributes)
- DO NOT extract: standalone years like ""2025"", ""2024"" (these are calendar dates)

RULES:
1. If the query is ONLY about dates/times/file types (no content to search), return empty array []
2. Keep multi-word phrases together, especially numbers with context words like ""years"", ""experience""
3. Return as JSON array of strings
4. No explanations, ONLY return the JSON array

EXAMPLES:

Input: ""Find documents about WEAI""
Output: [""WEAI""]

Input: ""Show me ServiceAPI documentation""
Output: [""ServiceAPI"", ""documentation""]

Input: ""Get me resumes of candidates with 17 years and 1 years of experience""
Output: [""resumes"", ""candidates"", ""17 years"", ""1 years"", ""experience""]

Input: ""candidates with 5+ years of experience in Python""
Output: [""candidates"", ""5+ years"", ""experience"", ""Python""]

Input: ""Show me documents from October 2025""
Output: []

Input: ""Find all PDF documents""
Output: []

Input: ""financial reports for Q3""
Output: [""financial reports"", ""Q3""]

Now extract keywords from the QUERY above. Return ONLY the JSON array:";

                // Call Gemini API
                var response = await CallGeminiModelAsync(prompt);

                if (string.IsNullOrWhiteSpace(response))
                {
                    _logger.LogWarning("   ⚠️ Empty response from Gemini keyword extraction");
                    return new List<string>();
                }

                // Parse JSON response
                var keywords = ParseKeywordsFromGeminiResponse(response);

                if (keywords.Any())
                {
                    _logger.LogDebug("   ✅ Extracted {Count} keyword(s): {Keywords}",
                        keywords.Count, string.Join(", ", keywords));
                }
                else
                {
                    _logger.LogDebug("   ℹ️ No keywords extracted (query may be date/time/file-type only)");
                }

                return keywords;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "   ⚠️ Failed to extract keywords with Gemini: {Message}", ex.Message);
                return new List<string>();
            }
        }

        /// <summary>
        /// Generate AI synthesized answer based on search results
        /// </summary>
        public async Task<string> SynthesizeRecordAnswerAsync(string query, List<RecordSearchResultDto> results)
        {
            if (!results.Any())
                return "";

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
                // Gemini will handle OCR artifacts and formatting automatically
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
            contextBuilder.AppendLine("IMPORTANT NOTES:");
            contextBuilder.AppendLine("- Content may contain OCR artifacts - interpret intelligently and fix errors");
            contextBuilder.AppendLine("- Answer must be COMPACT, CONTEXTUAL, and POINT-TO-POINT");
            contextBuilder.AppendLine();
            contextBuilder.AppendLine("INSTRUCTIONS:");
            contextBuilder.AppendLine("1. ANSWER FORMAT:");
            contextBuilder.AppendLine("   - Provide direct, point-to-point answers to the question");
            contextBuilder.AppendLine("   - Keep it compact (2-4 sentences max for summary)");
            contextBuilder.AppendLine("   - Include actual context from records, not generic responses");
            contextBuilder.AppendLine("   - Use bullet points for listing multiple items");
            contextBuilder.AppendLine();
            contextBuilder.AppendLine("2. CONTENT HANDLING:");
            contextBuilder.AppendLine("   - Fix OCR errors automatically (e.g., 'ServiceAP I' → 'ServiceAPI')");
            contextBuilder.AppendLine("   - Extract ONLY the most relevant information");
            contextBuilder.AppendLine("   - Skip unnecessary details unless directly related to the query");
            contextBuilder.AppendLine();
            contextBuilder.AppendLine("3. RECORD REFERENCES:");
            contextBuilder.AppendLine("   - List relevant records with: Record URI, Title (keep it brief)");
            contextBuilder.AppendLine("   - Only include records that directly answer the query");
            contextBuilder.AppendLine("   - Skip records that are marginally relevant");
            contextBuilder.AppendLine();
            contextBuilder.AppendLine("4. ACCURACY:");
            contextBuilder.AppendLine("   - Only use information present in the records");
            contextBuilder.AppendLine("   - If information is not found, say so directly");
            contextBuilder.AppendLine();
            contextBuilder.AppendLine("EXAMPLE COMPACT ANSWER:");
            contextBuilder.AppendLine("Found 3 records about API configuration. The ServiceAPI uses JSON format with OAuth2 authentication. Key settings are stored in web.config.");
            contextBuilder.AppendLine("- Record 12345: API Configuration Guide");
            contextBuilder.AppendLine("- Record 67890: OAuth2 Setup Instructions");
            contextBuilder.AppendLine();
            contextBuilder.AppendLine("ANSWER (Be compact, contextual, and point-to-point):");

            var prompt = contextBuilder.ToString();
            return await CallGeminiModelAsync(prompt);
        }

        /// <summary>
        /// Parse keywords from Gemini JSON response
        /// Expected format: ["keyword1", "keyword2"] or [] for empty
        /// </summary>
        public List<string> ParseKeywordsFromGeminiResponse(string response)
        {
            try
            {
                // Clean up response - remove markdown code blocks if present
                response = response.Trim();
                if (response.StartsWith("```json"))
                {
                    response = response.Substring(7);
                }
                if (response.StartsWith("```"))
                {
                    response = response.Substring(3);
                }
                if (response.EndsWith("```"))
                {
                    response = response.Substring(0, response.Length - 3);
                }
                response = response.Trim();

                // Parse JSON array
                var jsonDoc = JsonSerializer.Deserialize<JsonElement>(response);

                if (jsonDoc.ValueKind != JsonValueKind.Array)
                {
                    _logger.LogWarning("   ⚠️ Gemini response is not a JSON array: {Response}", response);
                    return new List<string>();
                }

                var keywords = new List<string>();
                foreach (var element in jsonDoc.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.String)
                    {
                        var keyword = element.GetString();
                        if (!string.IsNullOrWhiteSpace(keyword))
                        {
                            keywords.Add(keyword.Trim());
                        }
                    }
                }

                return keywords;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse keywords from Gemini response: {Response}", response);
                return new List<string>();
            }
        }
    }
}
