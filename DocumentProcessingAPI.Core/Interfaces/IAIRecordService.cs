namespace DocumentProcessingAPI.Core.Interfaces
{
    /// <summary>
    /// Service for AI-powered record operations (Summary and Q&A)
    /// Uses Gemini API for intelligent content analysis
    /// </summary>
    public interface IAIRecordService
    {
        /// <summary>
        /// Generate an AI summary of a record using its content and metadata
        /// </summary>
        /// <param name="recordUri">Content Manager record URI</param>
        /// <returns>AI-generated summary of the record</returns>
        Task<string> GetRecordSummaryAsync(long recordUri);

        /// <summary>
        /// Ask a question about a specific record using AI
        /// </summary>
        /// <param name="recordUri">Content Manager record URI</param>
        /// <param name="question">User's question about the record</param>
        /// <returns>AI-generated answer based on record content</returns>
        Task<string> AskAboutRecordAsync(long recordUri, string question);
    }
}
