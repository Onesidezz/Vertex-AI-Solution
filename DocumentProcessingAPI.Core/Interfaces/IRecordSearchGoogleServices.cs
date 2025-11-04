using DocumentProcessingAPI.Core.DTOs;

namespace DocumentProcessingAPI.Core.Interfaces
{
    /// <summary>
    /// Interface for Google Vertex AI / Gemini related services for record search
    /// Handles AI-powered keyword extraction and answer synthesis
    /// </summary>
    public interface IRecordSearchGoogleServices
    {
        /// <summary>
        /// Call Vertex AI Generative AI API for text generation
        /// </summary>
        Task<string> CallGeminiModelAsync(string prompt);

        /// <summary>
        /// Get Google Cloud access token using gcloud CLI
        /// </summary>
        Task<string> GetGoogleCloudAccessTokenAsync();

        /// <summary>
        /// Extract main search keywords from natural language queries using Gemini
        /// Returns entity names, product names, topics while excluding date/time and file type terms
        /// </summary>
        Task<List<string>> ExtractKeywordsWithGemini(string query);

        /// <summary>
        /// Generate AI synthesized answer based on search results
        /// </summary>
        Task<string> SynthesizeRecordAnswerAsync(string query, List<RecordSearchResultDto> results);

        /// <summary>
        /// Parse keywords from Gemini JSON response
        /// Expected format: ["keyword1", "keyword2"] or [] for empty
        /// </summary>
        List<string> ParseKeywordsFromGeminiResponse(string response);
    }
}
