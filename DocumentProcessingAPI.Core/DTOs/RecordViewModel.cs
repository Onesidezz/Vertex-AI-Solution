namespace DocumentProcessingAPI.Core.DTOs
{
    public class RecordViewModel
    {
        public long URI { get; set; }
        public string Title { get; set; }
        public string Container { get; set; }
        public string AllParts { get; set; }
        public string Assignee { get; set; }
        public string DateCreated { get; set; }
        public string IsContainer { get; set; }
        public Dictionary<string, long> ContainerCount { get; set; }
        public string ACL { get; set; }
        public Dictionary<string, object> DefaultProperties { get; set; } // New property for default properties
    }
}