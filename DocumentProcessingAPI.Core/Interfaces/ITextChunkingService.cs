namespace DocumentProcessingAPI.Core.Interfaces;

/// <summary>
/// Service interface for text chunking operations
/// </summary>
public interface ITextChunkingService
{
    /// <summary>
    /// Split text into chunks with specified parameters
    /// </summary>
    /// <param name="text">Text to be chunked</param>
    /// <param name="chunkSize">Maximum tokens per chunk</param>
    /// <param name="overlap">Number of overlapping tokens between chunks</param>
    /// <returns>List of text chunks with metadata</returns>
    Task<List<TextChunk>> ChunkTextAsync(string text, int chunkSize = 1000, int overlap = 200);

    /// <summary>
    /// Calculate the number of tokens in a text
    /// </summary>
    /// <param name="text">Text to count tokens for</param>
    /// <returns>Number of tokens</returns>
    Task<int> CountTokensAsync(string text);

    /// <summary>
    /// Validate chunking parameters
    /// </summary>
    /// <param name="chunkSize">Chunk size in tokens</param>
    /// <param name="overlap">Overlap size in tokens</param>
    /// <returns>True if parameters are valid</returns>
    bool ValidateChunkingParameters(int chunkSize, int overlap);
}

/// <summary>
/// Represents a text chunk with metadata
/// </summary>
public class TextChunk
{
    public string Content { get; set; } = string.Empty;
    public int TokenCount { get; set; }
    public int StartPosition { get; set; }
    public int EndPosition { get; set; }
    public int Sequence { get; set; }
    public int PageNumber { get; set; } = 1;
    public Dictionary<string, object> Metadata { get; set; } = new();
}