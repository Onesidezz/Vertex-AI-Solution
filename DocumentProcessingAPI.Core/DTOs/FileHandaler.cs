namespace DocumentProcessingAPI.Core.DTOs
{
    public class FileHandaler
    {
        public string FileName { get; set; }
        public byte[] File { get; set; }
        public string? LocalDownloadPath { get; set; }
    }
}
