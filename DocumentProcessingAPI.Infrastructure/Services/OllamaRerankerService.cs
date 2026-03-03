using DocumentProcessingAPI.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace DocumentProcessingAPI.Infrastructure.Services;

/// <summary>
/// Reranking service using Ollama's native /api/rerank endpoint with bge-reranker-v2-m3.
/// bge-reranker-v2-m3 is a cross-encoder from BAAI — the same model family as the
/// bge-m3 embedding model already used for semantic search.
///
/// Prerequisites:
///   - Ollama v0.5 or later
///   - ollama pull bge-reranker-v2-m3
///
/// The service gracefully falls back to the original hybrid-scored order on any failure,
/// so it never blocks search results from being returned.
/// </summary>
public class OllamaRerankerService : IRerankerService
{
    private readonly ILogger<OllamaRerankerService> _logger;
    private readonly HttpClient _httpClient;
    private readonly bool _enabled;
    private readonly string _rerankModel;
    private readonly int _candidateCount;

    public OllamaRerankerService(
        ILogger<OllamaRerankerService> logger,
        IConfiguration configuration)
    {
        _logger = logger;

        var baseUrl = configuration["Reranker:BaseUrl"] ?? "http://localhost:11434";
        _rerankModel = configuration["Reranker:ModelName"] ?? "bge-reranker-v2-m3";
        _enabled = configuration.GetValue<bool>("Reranker:Enabled", true);

        if (!int.TryParse(configuration["Reranker:CandidateCount"], out _candidateCount))
            _candidateCount = 50;

        if (!int.TryParse(configuration["Reranker:TimeoutSeconds"], out var timeoutSeconds))
            timeoutSeconds = 60;

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(timeoutSeconds)
        };

        _logger.LogInformation("OllamaRerankerService initialized | Enabled={Enabled} | Model={Model} | BaseUrl={BaseUrl} | CandidateCount={CandidateCount}",
            _enabled, _rerankModel, baseUrl, _candidateCount);
    }

    public async Task<List<(string id, float similarity, Dictionary<string, object> metadata)>> RerankAsync(
        string query,
        List<(string id, float similarity, Dictionary<string, object> metadata)> candidates,
        int topK)
    {
        // Guard: feature flag
        if (!_enabled)
        {
            _logger.LogDebug("Reranker disabled via configuration. Returning original order.");
            return candidates;
        }

        // Guard: nothing to rerank
        if (candidates == null || candidates.Count == 0)
            return candidates ?? new List<(string, float, Dictionary<string, object>)>();

        // Guard: skip when candidate pool is already at or below topK — no benefit
        if (candidates.Count <= topK)
        {
            _logger.LogDebug("Skipping rerank: {Count} candidates <= topK ({TopK}). Returning original order.",
                candidates.Count, topK);
            return candidates;
        }

        var sw = Stopwatch.StartNew();

        try
        {
            // Cap candidate pool to bound latency. Caller has already sorted by hybrid score,
            // so Take() preserves the best candidates for the reranker to work with.
            var candidatesToRerank = candidates.Count > _candidateCount
                ? candidates.Take(_candidateCount).ToList()
                : candidates;

            // Extract document text for each candidate.
            // chunk_content is the full text of the chunk stored in pgvector — gives the
            // cross-encoder the most signal. Fall back to record_title if absent.
            var documents = candidatesToRerank.Select(c =>
            {
                var chunkContent = GetStringMetadata(c.metadata, "chunk_content");
                if (!string.IsNullOrWhiteSpace(chunkContent))
                    return chunkContent;

                _logger.LogDebug("chunk_content missing for record {Uri}; using record_title as fallback.",
                    GetStringMetadata(c.metadata, "record_uri"));
                return GetStringMetadata(c.metadata, "record_title") ?? string.Empty;
            }).ToList();

            _logger.LogInformation("   🔁 Sending {Count} candidates to reranker model {Model}",
                candidatesToRerank.Count, _rerankModel);

            // Build Ollama /api/rerank request
            // Ollama rerank API (v0.5+):
            //   POST /api/rerank
            //   { "model": "...", "query": "...", "documents": ["...", "..."] }
            //   Response: { "results": [ { "index": 0, "relevance_score": 0.987 }, ... ] }
            var requestBody = new
            {
                model = _rerankModel,
                query,
                documents
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/api/rerank", content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Reranker API returned {Status}: {Error}. Falling back to original order.",
                    response.StatusCode, error);
                return candidates;
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            var parsed = JsonSerializer.Deserialize<JsonElement>(responseJson);

            if (!parsed.TryGetProperty("results", out var resultsEl)
                || resultsEl.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning("Reranker response missing 'results' array. Falling back to original order.");
                return candidates;
            }

            // Parse (originalIndex, relevanceScore) pairs
            var scored = new List<(int index, float score)>();
            foreach (var item in resultsEl.EnumerateArray())
            {
                if (item.TryGetProperty("index", out var idxEl)
                    && item.TryGetProperty("relevance_score", out var scoreEl))
                {
                    scored.Add((idxEl.GetInt32(), (float)scoreEl.GetDouble()));
                }
            }

            if (scored.Count == 0)
            {
                _logger.LogWarning("Reranker returned zero scored results. Falling back to original order.");
                return candidates;
            }

            // Min-max normalize scores to [0,1] so RelevanceScore in the DTO stays consistent
            // with the hybrid similarity scores (which are already in [0,1]).
            // bge-reranker-v2-m3 outputs raw logits that can be negative or exceed 1.
            var minScore = scored.Min(s => s.score);
            var maxScore = scored.Max(s => s.score);
            var scoreRange = maxScore - minScore;

            // Rebuild list ordered by reranker score descending, replacing the similarity field
            var reranked = scored
                .OrderByDescending(s => s.score)
                .Select(s =>
                {
                    var original = candidatesToRerank[s.index];
                    float normalized = scoreRange > 0f
                        ? (s.score - minScore) / scoreRange
                        : 1.0f;
                    return (original.id, normalized, original.metadata);
                })
                .ToList();

            sw.Stop();

            _logger.LogInformation("   ✅ Reranking complete in {ElapsedMs}ms | Input={Input} | Score range=[{Min:F4}, {Max:F4}]",
                sw.ElapsedMilliseconds, candidatesToRerank.Count, minScore, maxScore);

            // Log top-3 reranked records for troubleshooting
            foreach (var (record, rank) in reranked.Take(3).Select((r, i) => (r, i + 1)))
            {
                _logger.LogDebug("   #{Rank}: URI={Uri} | Title={Title} | RerankerScore={Score:F4}",
                    rank,
                    GetStringMetadata(record.metadata, "record_uri"),
                    GetStringMetadata(record.metadata, "record_title"),
                    record.normalized);
            }

            return reranked;
        }
        catch (TaskCanceledException)
        {
            sw.Stop();
            _logger.LogWarning("Reranker timed out after {ElapsedMs}ms. Falling back to original order.",
                sw.ElapsedMilliseconds);
            return candidates;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Reranker failed after {ElapsedMs}ms. Falling back to original order.",
                sw.ElapsedMilliseconds);
            return candidates;
        }
    }

    private static string? GetStringMetadata(Dictionary<string, object> metadata, string key)
        => metadata.TryGetValue(key, out var value) ? value?.ToString() : null;
}
