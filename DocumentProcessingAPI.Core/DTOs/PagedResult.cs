namespace DocumentProcessingAPI.Core.DTOs
{
    /// <summary>
    /// Generic paged result container for pagination support
    /// </summary>
    /// <typeparam name="T">The type of items in the page</typeparam>
    public class PagedResult<T>
    {
        /// <summary>
        /// The items in the current page
        /// </summary>
        public List<T> Items { get; set; } = new List<T>();

        /// <summary>
        /// Total count of items across all pages
        /// </summary>
        public long TotalCount { get; set; }

        /// <summary>
        /// Current page number (0-based)
        /// </summary>
        public int PageNumber { get; set; }

        /// <summary>
        /// Page size (items per page)
        /// </summary>
        public int PageSize { get; set; }

        /// <summary>
        /// Total number of pages
        /// </summary>
        public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;

        /// <summary>
        /// Whether there are more pages after the current page
        /// </summary>
        public bool HasMore => PageNumber < TotalPages - 1;
    }
}
