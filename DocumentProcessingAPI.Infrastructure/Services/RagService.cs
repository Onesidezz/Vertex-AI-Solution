using DocumentProcessingAPI.Core.DTOs;
using DocumentProcessingAPI.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text;

namespace DocumentProcessingAPI.Infrastructure.Services;

/// <summary>
/// RAG (Retrieval-Augmented Generation) service implementation
/// Combines vector search with LLM generation using Gemini embeddings
/// </summary>
public class RagService : IRagService
{
    private readonly ISearchService _searchService;
    private readonly ILogger<RagService> _logger;

    public RagService(ISearchService searchService, ILogger<RagService> logger)
    {
        _searchService = searchService;
        _logger = logger;
    }

    public async Task<RagResponseDto> AskQuestionAsync(RagRequestDto request)
    {
        try
        {
            _logger.LogInformation("Processing RAG question: {Question}", request.Question);

            var startTime = DateTime.UtcNow;

            // Convert RAG request to search request
            var searchRequest = new SearchRequestDto
            {
                Query = request.Question,
                TopK = request.MaxSources,
                MinimumScore = request.MinimumScore,
                DocumentId = request.DocumentId,
                PageNumber = 1,
                PageSize = request.MaxSources
            };

            // Perform semantic search to find relevant chunks
            var searchResults = await _searchService.SearchAsync(searchRequest);

            if (!searchResults.Results.Any())
            {
                _logger.LogWarning("No relevant chunks found for question: {Question}", request.Question);
                return new RagResponseDto
                {
                    Question = request.Question,
                    Answer = "I couldn't find any relevant information in the documents to answer your question.",
                    Sources = new List<RagSourceDto>(),
                    ConfidenceScore = 0.0f,
                    ResponseTime = (float)(DateTime.UtcNow - startTime).TotalMilliseconds,
                    Model = "gemini-embedding-001 + gemma-7b",
                    TokensUsed = 0
                };
            }

            // Build context from search results
            var contextBuilder = new StringBuilder();
            contextBuilder.AppendLine($"QUESTION: {request.Question}");
            contextBuilder.AppendLine("CONTEXT DOCUMENTS:");

            var sources = new List<RagSourceDto>();
            int contextLength = 0;

            foreach (var result in searchResults.Results.Take(request.MaxSources))
            {
                var cleanContent = CleanTextForContext(result.Content);

                // Check if adding this chunk would exceed max context length
                if (contextLength + cleanContent.Length > request.MaxContextLength)
                {
                    break;
                }

                contextBuilder.AppendLine($"\n--- Document: {result.DocumentName} (Page {result.PageNumber}, Relevance: {result.RelevanceScore:F2}) ---");
                contextBuilder.AppendLine(cleanContent);

                contextLength += cleanContent.Length;

                // Add to sources
                sources.Add(new RagSourceDto
                {
                    ChunkId = result.ChunkId,
                    DocumentId = result.DocumentId,
                    DocumentName = result.DocumentName,
                    Content = request.IncludeSourceText ? cleanContent : string.Empty,
                    RelevanceScore = result.RelevanceScore,
                    PageNumber = result.PageNumber,
                    ChunkSequence = result.ChunkSequence
                });
            }

            contextBuilder.AppendLine("\nINSTRUCTIONS:");
            contextBuilder.AppendLine("Based on the context documents above, provide a comprehensive and accurate answer to the question.");
            contextBuilder.AppendLine("If the information is not available in the context, say so clearly.");
            contextBuilder.AppendLine("Cite specific documents when referencing information.");

            var prompt = contextBuilder.ToString();

            _logger.LogInformation("Generated context with {SourceCount} sources, {ContextLength} characters",
                sources.Count, contextLength);

            // Generate response using LLM
            var llmResponse = await GenerateAnswerWithGemma(prompt);

            var responseTime = (float)(DateTime.UtcNow - startTime).TotalMilliseconds;

            // Calculate confidence based on average relevance score
            var avgRelevanceScore = sources.Any() ? sources.Average(s => s.RelevanceScore) : 0.0f;
            var confidenceScore = Math.Min(avgRelevanceScore * 1.2f, 1.0f); // Boost but cap at 1.0

            var response = new RagResponseDto
            {
                Question = request.Question,
                Answer = llmResponse ?? "I'm unable to generate a response at this time.",
                Sources = sources,
                ConfidenceScore = confidenceScore,
                ResponseTime = responseTime,
                Model = "gemini-embedding-001 + gemma-7b",
                TokensUsed = EstimateTokenCount(prompt + llmResponse)
            };

            _logger.LogInformation("RAG response generated in {ResponseTime}ms with {SourceCount} sources, confidence: {Confidence:F2}",
                responseTime, sources.Count, confidenceScore);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing RAG question: {Question}", request.Question);
            throw new InvalidOperationException($"RAG processing failed: {ex.Message}", ex);
        }
    }

    public async IAsyncEnumerable<RagStreamResponseDto> AskQuestionStreamAsync(RagRequestDto request)
    {
        _logger.LogInformation("Processing streaming RAG question: {Question}", request.Question);

        RagResponseDto? response = null;
        var hasError = false;

        try
        {
            response = await AskQuestionAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in streaming RAG: {Question}", request.Question);
            hasError = true;
        }

        if (hasError)
        {
            yield return new RagStreamResponseDto
            {
                Delta = "I'm unable to process your question at this time due to an error.",
                IsComplete = true,
                Sources = new List<RagSourceDto>(),
                ConfidenceScore = 0.0f
            };
            yield break;
        }

        // Simulate streaming by breaking the answer into chunks
        var words = response!.Answer.Split(' ');
        var wordChunks = new List<string>();

        for (int i = 0; i < words.Length; i += 5) // Send 5 words at a time
        {
            var chunk = string.Join(" ", words.Skip(i).Take(5));
            wordChunks.Add(chunk);
        }

        // Stream the chunks
        for (int i = 0; i < wordChunks.Count; i++)
        {
            var isLast = i == wordChunks.Count - 1;

            yield return new RagStreamResponseDto
            {
                Delta = wordChunks[i] + (isLast ? "" : " "),
                IsComplete = isLast,
                Sources = isLast ? response.Sources : null,
                ConfidenceScore = isLast ? response.ConfidenceScore : null
            };

            // Add small delay to simulate real streaming
            await Task.Delay(50);
        }
    }

    public async Task<RagResponseDto> AskQuestionAboutDocumentAsync(Guid documentId, RagRequestDto request)
    {
        try
        {
            _logger.LogInformation("Processing document-specific RAG question for document {DocumentId}: {Question}",
                documentId, request.Question);

            // Set the document ID in the request
            request.DocumentId = documentId;

            // Use the main RAG logic
            var response = await AskQuestionAsync(request);

            _logger.LogInformation("Document-specific RAG completed for document {DocumentId}", documentId);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing document-specific RAG question for document {DocumentId}: {Question}",
                documentId, request.Question);
            throw new InvalidOperationException($"Document-specific RAG processing failed: {ex.Message}", ex);
        }
    }

    private string CleanTextForContext(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // Remove excessive whitespace and normalize
        var cleaned = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

        // Remove common OCR artifacts
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"[^\w\s\-.,;:!?()""'$%&/]", " ");

        // Fix common spacing issues
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", " ");

        return cleaned;
    }

    private int EstimateTokenCount(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        // Rough estimation: ~4 characters per token on average
        return text.Length / 4;
    }

    private async Task<string> GenerateAnswerWithGemma(string prompt)
    {
        try
        {
            using var httpClient = new HttpClient()
            {
                Timeout = TimeSpan.FromMinutes(5) // Extended timeout for LLM
            };

            var payload = new
            {
                model = "gemma:7b",
                prompt = prompt,
                stream = false,
                options = new
                {
                    temperature = 0.1,
                    top_p = 0.9,
                    max_tokens = 1000,
                    stop = new[] { "\n\n", "Human:", "Assistant:" }
                }
            };

            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogDebug("Calling Gemma LLM with prompt length: {PromptLength}", prompt.Length);

            // Replace with your actual Gemma/Ollama endpoint
            var response = await httpClient.PostAsync("http://localhost:11434/api/generate", content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Gemma API error: {Status} - {Error}", response.StatusCode, errorContent);
                return "I apologize, but I'm currently unable to generate a response due to a technical issue with the AI service.";
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            using var document = System.Text.Json.JsonDocument.Parse(responseContent);

            if (document.RootElement.TryGetProperty("response", out var responseElement))
            {
                var generatedText = responseElement.GetString() ?? string.Empty;
                _logger.LogInformation("Generated response length: {Length}", generatedText.Length);
                return generatedText.Trim();
            }

            _logger.LogWarning("No response field found in Gemma API response");
            return "I was unable to generate a proper response. Please try rephrasing your question.";
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Gemma API request timed out");
            return "The request timed out while generating a response. Please try again with a shorter question.";
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error calling Gemma API");
            return "I'm currently unable to connect to the AI service. Please try again later.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error calling Gemma API");
            return "An unexpected error occurred while generating the response. Please try again.";
        }
    }
}