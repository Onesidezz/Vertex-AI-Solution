using DocumentProcessingAPI.Core.DTOs;
using DocumentProcessingAPI.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;

namespace DocumentProcessingAPI.Infrastructure.Services
{
    /// <summary>
    /// AI-powered record service using Gemini for summary and Q&A functionality
    /// Provides intelligent analysis of Content Manager records
    /// </summary>
    public class AIRecordService : IAIRecordService
    {
        private readonly ContentManagerServices _contentManagerServices;
        private readonly PgVectorService _pgVectorService;
        private readonly IRecordSearchGoogleServices _googleServices;
        private readonly IDocumentProcessor _documentProcessor;
        private readonly ILogger<AIRecordService> _logger;

        public AIRecordService(
            ContentManagerServices contentManagerServices,
            PgVectorService pgVectorService,
            IRecordSearchGoogleServices googleServices,
            IDocumentProcessor documentProcessor,
            ILogger<AIRecordService> logger)
        {
            _contentManagerServices = contentManagerServices;
            _pgVectorService = pgVectorService;
            _googleServices = googleServices;
            _documentProcessor = documentProcessor;
            _logger = logger;
        }

        /// <summary>
        /// Generate an AI summary of a record using Gemini
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
                var summary = await _googleServices.CallGeminiModelAsync(prompt);

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
        /// Ask a question about a specific record using Gemini AI
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
                contextBuilder.AppendLine("You are a helpful AI assistant answering questions about a Content Manager record.");
                contextBuilder.AppendLine();
                contextBuilder.AppendLine($"USER QUESTION: {question}");
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
                contextBuilder.AppendLine("DOCUMENT CONTENT:");
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
                contextBuilder.AppendLine("1. Answer the user's question based ONLY on the information in this record");
                contextBuilder.AppendLine("2. Be specific and provide relevant details from the document");
                contextBuilder.AppendLine("3. If the information is not found in the record, clearly state that");
                contextBuilder.AppendLine("4. Keep your answer concise (2-4 sentences) but informative");
                contextBuilder.AppendLine("5. Quote specific parts of the document when relevant");
                contextBuilder.AppendLine();
                contextBuilder.AppendLine("ANSWER:");

                var prompt = contextBuilder.ToString();
                var answer = await _googleServices.CallGeminiModelAsync(prompt);

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
