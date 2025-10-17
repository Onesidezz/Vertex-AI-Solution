namespace DocumentProcessingAPI.Core.DTOs
{
    public class ContainerRecordsInfo
    {
        public long ContainerId { get; set; }
        public string ContainerName { get; set; }
        public Dictionary<string, long> RecordCounts { get; set; } = new Dictionary<string, long>();
    }
}