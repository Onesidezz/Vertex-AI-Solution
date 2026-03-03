using DocumentProcessingAPI.Core.DTOs;
using DocumentProcessingAPI.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DocumentProcessingAPI.Infrastructure.Services
{
    /// <summary>
    /// Service using local Ollama for AI operations.
    /// Supports any Ollama model — configured via Ollama:ModelName in appsettings.json.
    /// Default model: qwen2.5:7b (best JSON/structured output for keyword extraction).
    /// Recommended alternatives: llama3.1:8b (best all-round), mistral:7b-instruct-v0.3 (best Q&A).
    /// </summary>
    public class OllamaGemma7bService : IRecordSearchGoogleServices
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<OllamaGemma7bService> _logger;
        private readonly HttpClient _httpClient;
        private readonly string _ollamaBaseUrl;
        private readonly string _modelName;

        // Pre-compiled regex to extract a JSON array from anywhere in the model response.
        // This is the key fallback when the model wraps JSON in markdown or text.
        private static readonly Regex JsonArrayRegex = new(
            @"\[[\s\S]*?\]",
            RegexOptions.Compiled | RegexOptions.Singleline);

        public OllamaGemma7bService(
            IConfiguration configuration,
            ILogger<OllamaGemma7bService> logger)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _ollamaBaseUrl = _configuration["Ollama:BaseUrl"] ?? "http://localhost:11434";
            // Default changed to qwen2.5:7b — much better JSON output than gemma:7b.
            // Change in appsettings.json: "ModelName": "llama3.1:8b" for best all-round quality.
            _modelName = _configuration["Ollama:ModelName"] ?? "qwen2.5:7b";

            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(_ollamaBaseUrl),
                Timeout = TimeSpan.FromMinutes(5)
            };

            _logger.LogInformation("Ollama LLM Service initialized - BaseUrl: {BaseUrl}, Model: {Model}",
                _ollamaBaseUrl, _modelName);
        }

        /// <summary>
        /// Call Ollama /api/generate for text generation. Implements IRecordSearchGoogleServices.
        /// </summary>
        public Task<string> CallGeminiModelAsync(string prompt, int maxOutputTokens = 512, int numCtx = 32768)
            => CallOllamaAsync(prompt, maxOutputTokens, systemPrompt: null, numCtx: numCtx);

        /// <summary>
        /// Internal Ollama call with full options.
        /// Supports an optional system prompt and configurable context window.
        /// </summary>
        private async Task<string> CallOllamaAsync(
            string prompt,
            int maxOutputTokens = 512,
            string? systemPrompt = null,
            int numCtx = 8192)
        {
            try
            {
                _logger.LogInformation("Calling Ollama model: {Model} (maxTokens={MaxTokens}, ctx={Ctx})",
                    _modelName, maxOutputTokens, numCtx);

                // Build request — include system prompt if provided
                object requestBody;
                if (!string.IsNullOrWhiteSpace(systemPrompt))
                {
                    requestBody = new
                    {
                        model = _modelName,
                        system = systemPrompt,
                        prompt = prompt,
                        stream = false,
                        options = new
                        {
                            temperature = 0.1,
                            top_p = 0.9,
                            top_k = 20,
                            num_predict = maxOutputTokens,
                            num_ctx = numCtx,
                            repeat_penalty = 1.1
                        }
                    };
                }
                else
                {
                    requestBody = new
                    {
                        model = _modelName,
                        prompt = prompt,
                        stream = false,
                        options = new
                        {
                            temperature = 0.1,
                            top_p = 0.9,
                            top_k = 20,
                            num_predict = maxOutputTokens,
                            num_ctx = numCtx,
                            repeat_penalty = 1.1
                        }
                    };
                }

                var jsonContent = JsonSerializer.Serialize(requestBody);
                var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("/api/generate", httpContent);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var jsonResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);

                    if (jsonResponse.TryGetProperty("response", out var responseText))
                    {
                        var result = responseText.GetString() ?? "";
                        _logger.LogInformation("Ollama response received ({Length} chars)", result.Length);
                        return result;
                    }
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Ollama API error: {StatusCode} - {Error}", response.StatusCode, errorContent);
                }

                return "";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Ollama API");
                return "";
            }
        }

        /// <summary>
        /// Not used for Ollama — kept for interface compatibility.
        /// </summary>
        public Task<string> GetGoogleCloudAccessTokenAsync()
        {
            return Task.FromResult("");
        }

        /// <summary>
        /// Extract core search keywords from a natural language query.
        /// Uses a focused system prompt + smaller context window (2048) for speed.
        /// Falls back to regex JSON extraction if the model returns messy output.
        /// </summary>
        public async Task<List<string>> ExtractKeywordsWithGemini(string query)
        {
            try
            {
                _logger.LogDebug("Extracting keywords from query: {Query}", query);

                // System prompt: force the model to ONLY return a JSON array.
                // This dramatically improves reliability on qwen2.5, llama3.1, and mistral.
                var systemPrompt =
                    "You are a search keyword extractor. " +
                    "You ONLY output a valid JSON array of strings. " +
                    "No markdown, no explanation, no extra text — just the JSON array.";

                // Simplified prompt with fewer rules — smaller models follow fewer rules better.
                var prompt = $@"Extract the meaningful content keywords from this search query.

QUERY: ""{query}""

Rules:
1. Extract: names, topics, specific terms, technical terms, numbers with context (e.g. ""17 years"", ""Q3"")
2. Exclude: calendar dates (January 2026, 2026-01-28), action words (find, show, get), file type words (PDF, Word), generic words (documents, records, files)
3. If the query is ONLY about dates or file types, return []

Examples:
Query: ""Find documents about WEAI"" → [""WEAI""]
Query: ""resumes with 5+ years Python experience"" → [""resumes"", ""5+ years"", ""Python"", ""experience""]
Query: ""Show me documents from January 2026"" → []
Query: ""financial reports Q3"" → [""financial reports"", ""Q3""]

Return ONLY a JSON array:";

                // Use a small context window for fast keyword extraction
                var response = await CallOllamaAsync(
                    prompt,
                    maxOutputTokens: 256,
                    systemPrompt: systemPrompt,
                    numCtx: 2048);

                if (string.IsNullOrWhiteSpace(response))
                {
                    _logger.LogWarning("Empty response from Ollama keyword extraction");
                    return new List<string>();
                }

                var keywords = ParseKeywordsFromGeminiResponse(response);

                _logger.LogDebug("Extracted {Count} keyword(s): {Keywords}",
                    keywords.Count, string.Join(", ", keywords));

                return keywords;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract keywords with Ollama: {Message}", ex.Message);
                return new List<string>();
            }
        }

        /// <summary>
        /// Generate an AI-synthesised answer from search results.
        /// Uses larger context window (8192) to handle multiple records.
        /// </summary>
        public async Task<string> SynthesizeRecordAnswerAsync(string query, List<RecordSearchResultDto> results)
        {
            if (!results.Any())
                return "";

            var uniqueRecords = results
                .OrderByDescending(r => r.RelevanceScore)
                .Take(5)   // top 5 only — more records = more noise for the model
                .ToList();

            _logger.LogInformation("Synthesizing answer from {Count} records", uniqueRecords.Count);

            // Token budget: numCtx(32768) - output(2000) - systemPrompt(~60) - overhead(~300) = ~30408 tokens
            // 1 token ≈ 4 chars → content budget ≈ 121 632 chars split across records
            const int ContentBudgetChars = (32768 - 2000 - 60 - 300) * 4;
            int perRecordBudget = ContentBudgetChars / Math.Max(uniqueRecords.Count, 1);

            var contextBuilder = new StringBuilder();
            contextBuilder.AppendLine("Context from documents:");
            contextBuilder.AppendLine("---");

            foreach (var result in uniqueRecords)
            {
                contextBuilder.AppendLine($"[Record {result.RecordUri}: {result.RecordTitle}]");

                if (!string.IsNullOrWhiteSpace(result.ContentPreview))
                {
                    // Trim preview to per-record budget so total never exceeds numCtx
                    var preview = result.ContentPreview.Length > perRecordBudget
                        ? result.ContentPreview[..perRecordBudget] + "..."
                        : result.ContentPreview;
                    contextBuilder.AppendLine(preview);
                }

                foreach (var meta in result.Metadata.Where(m => m.Key.StartsWith("meta_")).Take(3))
                {
                    var fieldName = meta.Key.Replace("meta_", "").Replace("_", " ");
                    contextBuilder.AppendLine($"{fieldName}: {meta.Value}");
                }

                contextBuilder.AppendLine();
            }

            contextBuilder.AppendLine("---");
            contextBuilder.AppendLine();
            // *** Question goes at the END — never lost even if context is long ***
            contextBuilder.AppendLine($"Question: {query}");
            contextBuilder.AppendLine();
            contextBuilder.AppendLine("Instructions:");
            contextBuilder.AppendLine("1. Read the context carefully and find information that answers the question");
            contextBuilder.AppendLine("2. Provide a direct answer in 2-4 sentences");
            contextBuilder.AppendLine("3. Only use information from the context above");
            contextBuilder.AppendLine("4. If the answer is not in the context, say so");
            contextBuilder.AppendLine("5. List the relevant record URIs at the end");
            contextBuilder.AppendLine();
            contextBuilder.AppendLine("Answer:");

            var systemPrompt =
                "You are a precise document search assistant. " +
                "Answer ONLY using facts explicitly stated in the provided context. " +
                "NEVER guess, infer, approximate, or make connections not directly stated. " +
                "NEVER say a value is 'close to' or 'similar to' another value unless the document says so. " +
                "If the exact answer is not in the context, say: 'The provided documents do not contain information about this.'";

            return await CallOllamaAsync(
                contextBuilder.ToString(),
                maxOutputTokens: 2000,
                systemPrompt: systemPrompt,
                numCtx: 32768);
        }

        /// <summary>
        /// Parse a JSON array of keywords from a model response.
        /// Strategy 1: Try direct JSON parse of the trimmed response.
        /// Strategy 2: Strip markdown code fences (```json ... ```) then parse.
        /// Strategy 3: Use regex to find any JSON array in the response.
        /// Strategy 4: Extract quoted strings manually as final fallback.
        /// </summary>
        public List<string> ParseKeywordsFromGeminiResponse(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return new List<string>();

            var cleaned = response.Trim();

            // ── Strategy 1: Direct parse ──────────────────────────────────────────
            var result = TryParseJsonArray(cleaned);
            if (result != null)
            {
                _logger.LogDebug("Keyword parse: Strategy 1 (direct) succeeded");
                return result;
            }

            // ── Strategy 2: Strip markdown code fences ────────────────────────────
            var stripped = StripMarkdownFences(cleaned);
            if (stripped != cleaned)
            {
                result = TryParseJsonArray(stripped);
                if (result != null)
                {
                    _logger.LogDebug("Keyword parse: Strategy 2 (strip fences) succeeded");
                    return result;
                }
            }

            // ── Strategy 3: Regex — find first JSON array anywhere in response ─────
            var match = JsonArrayRegex.Match(response);
            while (match.Success)
            {
                result = TryParseJsonArray(match.Value);
                if (result != null)
                {
                    _logger.LogDebug("Keyword parse: Strategy 3 (regex) succeeded — found array at pos {Pos}", match.Index);
                    return result;
                }
                match = match.NextMatch();
            }

            // ── Strategy 4: Extract quoted strings as last resort ─────────────────
            var quotedStrings = Regex.Matches(response, @"""([^""\\]*(\\.[^""\\]*)*)""")
                .Cast<Match>()
                .Select(m => m.Groups[1].Value.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s) && s.Length > 1)
                .ToList();

            if (quotedStrings.Any())
            {
                _logger.LogWarning("Keyword parse: Strategy 4 (quoted strings fallback) — {Count} strings extracted", quotedStrings.Count);
                return quotedStrings;
            }

            _logger.LogWarning("Keyword parse: All strategies failed. Raw response: {Response}",
                response.Length > 200 ? response.Substring(0, 200) + "..." : response);
            return new List<string>();
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        /// <summary>
        /// Attempt to parse a string as a JSON array of strings.
        /// Returns null if parsing fails or result is not an array.
        /// </summary>
        private List<string>? TryParseJsonArray(string input)
        {
            try
            {
                input = input.Trim();
                if (!input.StartsWith("[")) return null;

                var jsonDoc = JsonSerializer.Deserialize<JsonElement>(input);
                if (jsonDoc.ValueKind != JsonValueKind.Array) return null;

                var keywords = new List<string>();
                foreach (var element in jsonDoc.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.String)
                    {
                        var kw = element.GetString();
                        if (!string.IsNullOrWhiteSpace(kw))
                            keywords.Add(kw.Trim());
                    }
                }
                return keywords;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Remove markdown code fences (```json ... ``` or ``` ... ```) from model output.
        /// </summary>
        private static string StripMarkdownFences(string text)
        {
            // Remove ```json ... ``` or ``` ... ```
            text = Regex.Replace(text, @"^```(?:json)?\s*", "", RegexOptions.Multiline);
            text = Regex.Replace(text, @"```\s*$", "", RegexOptions.Multiline);
            return text.Trim();
        }
    }
}
