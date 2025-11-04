namespace DocumentProcessingAPI.Core.Interfaces
{
    /// <summary>
    /// Interface for record search helper/utility services
    /// Provides query processing, date extraction, filtering, and metadata handling
    /// </summary>
    public interface IRecordSearchHelperServices
    {
        // Query Processing Methods
        string CleanAndNormalizeQuery(string query);
        List<string> ExtractSmartKeywords(string query);
        List<string> ExtractKeywordsAndNumbers(string query);
        string RemoveCommonQueryWords(string query);

        // Date/Time Extraction Methods
        (DateTime? startDate, DateTime? endDate) ExtractDateRangeFromQuery(string query);
        DateTime? ParseDateFromString(string dateString, DateTime referenceDate);
        (DateTime startDate, DateTime endDate)? ExtractAroundTimeFilter(string lowerQuery, DateTime referenceDate);
        List<string> ExtractFileTypeFilters(string query);
        (bool isEarliest, bool isLatest) ExtractSortingIntent(string query);

        // Search Optimization Methods
        int CalculateDynamicSearchLimit(int topK, bool isEarliest, bool isLatest, DateTime? startDate, DateTime? endDate, int fileTypeFiltersCount, int contentKeywordsCount);
        float CalculateDynamicMinimumScore(float originalMinScore, string query, List<string> contentKeywords);

        // Result Processing Methods
        List<(string id, float similarity, Dictionary<string, object> metadata)> ApplyDateRangeFilter(
            List<(string id, float similarity, Dictionary<string, object> metadata)> results,
            DateTime? startDate,
            DateTime? endDate);
        List<(string id, float similarity, Dictionary<string, object> metadata)> ApplyMetadataFilters(
            List<(string id, float similarity, Dictionary<string, object> metadata)> results,
            Dictionary<string, object> filters);
        List<(string id, float similarity, Dictionary<string, object> metadata)> ApplyFileTypeFilter(
            List<(string id, float similarity, Dictionary<string, object> metadata)> results,
            List<string> fileTypes);
        List<(string id, float similarity, Dictionary<string, object> metadata)> ApplyDateSorting(
            List<(string id, float similarity, Dictionary<string, object> metadata)> results,
            bool earliest);
        string BuildContentPreview(Dictionary<string, object> metadata);

        // Metadata Utility Methods
        T? GetMetadataValue<T>(Dictionary<string, object> metadata, string key);
        string MakeSafeKey(string key);
    }
}
