namespace DocumentProcessingAPI.Core.DTOs
{
    /// <summary>
    /// Response DTO for record search operations
    /// </summary>
    public class RecordSearchResponseDto
    {
        public string Query { get; set; } = string.Empty;
        public List<RecordSearchResultDto> Results { get; set; } = new();
        public int TotalResults { get; set; }
        public float QueryTime { get; set; }
        public string? SynthesizedAnswer { get; set; }
    }

    /// <summary>
    /// Individual record search result
    /// </summary>
    public class RecordSearchResultDto
    {
        public long RecordUri { get; set; }
        public string RecordTitle { get; set; } = string.Empty;
        public string DateCreated { get; set; } = string.Empty;
        public string RecordType { get; set; } = string.Empty;
        public float RelevanceScore { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
        public string? ContentPreview { get; set; }
        public string? ACL { get; set; }
    }

    /// <summary>
    /// Request DTO for record search
    /// </summary>
    public class RecordSearchRequestDto
    {
        public string Query { get; set; } = string.Empty;
        public int TopK { get; set; } = 10;
        public float MinimumScore { get; set; } = 0.7f;
        public Dictionary<string, object>? MetadataFilters { get; set; }
    }
}
