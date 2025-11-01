namespace DocumentProcessingAPI.Core.DTOs
{
    /// <summary>
    /// Result DTO for Content Manager IDOL index search operations
    /// </summary>
    public class ContentManagerSearchResultDto
    {
        /// <summary>
        /// Set of candidate record URIs returned from the search
        /// </summary>
        public HashSet<long> CandidateRecordUris { get; set; } = new();

        /// <summary>
        /// List of record detail strings (Title, DateCreated, etc.)
        /// </summary>
        public List<string> RecordDetails { get; set; } = new();
    }
}
