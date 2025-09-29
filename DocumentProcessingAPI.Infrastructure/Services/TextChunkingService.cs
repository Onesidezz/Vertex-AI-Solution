using DocumentProcessingAPI.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using System.Text;

namespace DocumentProcessingAPI.Infrastructure.Services;

/// <summary>
/// Service for text chunking operations with token-based splitting
/// </summary>
public class TextChunkingService : ITextChunkingService
{
    private readonly ILogger<TextChunkingService> _logger;
    private static readonly Regex SentenceEndRegex = new(@"[.!?]+\s+", RegexOptions.Compiled);
    private static readonly Regex WordBoundaryRegex = new(@"\b", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex PageMarkerRegex = new(@"\[PAGE\s+(\d+)\]", RegexOptions.Compiled);

    // Approximate token-to-character ratio for English text (1 token ≈ 4 characters on average)
    private const double TokenToCharRatio = 4.0;
    private const int MinChunkSize = 100;
    private const int MaxChunkSize = 4000;
    private const int MinOverlap = 0;

    public TextChunkingService(ILogger<TextChunkingService> logger)
    {
        _logger = logger;
    }

    public async Task<List<TextChunk>> ChunkTextAsync(string text, int chunkSize = 1000, int overlap = 200)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogWarning("Attempted to chunk empty or null text");
            return new List<TextChunk>();
        }

        if (!ValidateChunkingParameters(chunkSize, overlap))
        {
            throw new ArgumentException("Invalid chunking parameters");
        }

        try
        {
            _logger.LogInformation("Starting text chunking with chunk size {ChunkSize} and overlap {Overlap}", chunkSize, overlap);

            // Clean the text first
            var cleanedText = CleanText(text);

            // Extract page information if available
            var pageInfo = ExtractPageInformation(cleanedText);

            // Remove page markers for chunking
            var textForChunking = RemovePageMarkers(cleanedText);

            var chunks = new List<TextChunk>();

            // Convert token sizes to approximate character counts
            int chunkSizeChars = (int)(chunkSize * TokenToCharRatio);
            int overlapChars = (int)(overlap * TokenToCharRatio);

            int position = 0;
            int sequence = 0;

            while (position < textForChunking.Length)
            {
                var chunkEndPosition = Math.Min(position + chunkSizeChars, textForChunking.Length);

                // Try to break at sentence boundary if possible
                if (chunkEndPosition < textForChunking.Length)
                {
                    var sentenceBreak = FindOptimalBreakPoint(textForChunking, position, chunkEndPosition);
                    if (sentenceBreak > position + (chunkSizeChars / 2)) // Only use if it's not too far back
                    {
                        chunkEndPosition = sentenceBreak;
                    }
                }

                var chunkText = textForChunking.Substring(position, chunkEndPosition - position).Trim();

                if (!string.IsNullOrWhiteSpace(chunkText))
                {
                    var tokenCount = await CountTokensAsync(chunkText);
                    var pageNumber = GetPageNumberForPosition(pageInfo, position);

                    var chunk = new TextChunk
                    {
                        Content = chunkText,
                        TokenCount = tokenCount,
                        StartPosition = position,
                        EndPosition = chunkEndPosition,
                        Sequence = sequence++,
                        PageNumber = pageNumber,
                        Metadata = new Dictionary<string, object>
                        {
                            ["character_count"] = chunkText.Length,
                            ["word_count"] = CountWords(chunkText),
                            ["has_structured_content"] = HasStructuredContent(chunkText)
                        }
                    };

                    chunks.Add(chunk);
                    _logger.LogDebug("Created chunk {Sequence} with {TokenCount} tokens", chunk.Sequence, chunk.TokenCount);
                }

                // Move position forward, accounting for overlap
                var nextPosition = chunkEndPosition - overlapChars;
                if (nextPosition <= position)
                {
                    nextPosition = position + 1; // Ensure we make progress
                }
                position = nextPosition;

                // Safety break to prevent infinite loops
                if (position >= textForChunking.Length - overlapChars)
                {
                    break;
                }
            }

            _logger.LogInformation("Text chunking completed. Created {ChunkCount} chunks", chunks.Count);
            return chunks;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during text chunking");
            throw;
        }
    }

    public async Task<int> CountTokensAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        // Simple approximation: split by whitespace and punctuation
        // This is a rough estimation - for production, consider using tiktoken or similar
        var words = WhitespaceRegex.Split(text.Trim())
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .ToList();

        // Account for punctuation and special characters as separate tokens
        var tokenCount = 0;
        foreach (var word in words)
        {
            // Simple heuristic: longer words might be split into sub-tokens
            if (word.Length > 10)
            {
                tokenCount += (int)Math.Ceiling(word.Length / 4.0);
            }
            else
            {
                tokenCount += 1;
            }

            // Add tokens for punctuation
            tokenCount += Regex.Matches(word, @"[^\w\s]").Count;
        }

        return tokenCount;
    }

    public bool ValidateChunkingParameters(int chunkSize, int overlap)
    {
        if (chunkSize < MinChunkSize || chunkSize > MaxChunkSize)
        {
            _logger.LogWarning("Invalid chunk size: {ChunkSize}. Must be between {MinSize} and {MaxSize}",
                chunkSize, MinChunkSize, MaxChunkSize);
            return false;
        }

        if (overlap < MinOverlap || overlap >= chunkSize)
        {
            _logger.LogWarning("Invalid overlap: {Overlap}. Must be between {MinOverlap} and less than chunk size {ChunkSize}",
                overlap, MinOverlap, chunkSize);
            return false;
        }

        return true;
    }

    private string CleanText(string text)
    {
        // Enhanced text cleaning for OCR noise and document artifacts
        var lines = text.Split('\n');
        var cleanedLines = new List<string>();

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            // Preserve structure markers
            if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
            {
                cleanedLines.Add(line);
                continue;
            }

            // Skip very short or noisy lines that are likely OCR artifacts
            if (trimmedLine.Length < 3)
                continue;

            // Skip lines with excessive special characters (likely OCR noise)
            var specialCharCount = trimmedLine.Count(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c) && !".,!?;:()-\"'".Contains(c));
            if (specialCharCount > trimmedLine.Length / 3)
                continue;

            // Clean common OCR artifacts
            var cleanedLine = CleanOcrArtifacts(trimmedLine);

            // Normalize whitespace for content lines
            cleanedLine = WhitespaceRegex.Replace(cleanedLine, " ");

            if (!string.IsNullOrWhiteSpace(cleanedLine))
            {
                cleanedLines.Add(cleanedLine);
            }
        }

        return string.Join("\n", cleanedLines);
    }

    /// <summary>
    /// Clean OCR artifacts and noise from individual lines
    /// </summary>
    private string CleanOcrArtifacts(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return line;

        var cleaned = line;

        // Remove standalone single characters that are likely OCR noise
        cleaned = Regex.Replace(cleaned, @"\b[a-zA-Z]\s+(?=[A-Z])", " ");

        // Fix common OCR spacing issues around punctuation
        cleaned = Regex.Replace(cleaned, @"\s+([,.!?;:])", "$1");
        cleaned = Regex.Replace(cleaned, @"([,.!?;:])\s*([A-Z])", "$1 $2");

        // Remove excessive punctuation
        cleaned = Regex.Replace(cleaned, @"[.]{3,}", "...");
        cleaned = Regex.Replace(cleaned, @"[-]{3,}", "---");

        // Fix broken words (common in OCR)
        cleaned = Regex.Replace(cleaned, @"(\w)\s+(\w)\s+(?=\w)", "$1$2 ");

        // Clean up multiple spaces
        cleaned = Regex.Replace(cleaned, @"\s{2,}", " ");

        return cleaned.Trim();
    }

    private Dictionary<int, int> ExtractPageInformation(string text)
    {
        var pageInfo = new Dictionary<int, int>(); // position -> page number

        var matches = PageMarkerRegex.Matches(text);
        foreach (Match match in matches)
        {
            if (int.TryParse(match.Groups[1].Value, out int pageNumber))
            {
                pageInfo[match.Index] = pageNumber;
            }
        }

        return pageInfo;
    }

    private string RemovePageMarkers(string text)
    {
        // Remove page markers but keep the content structure
        return PageMarkerRegex.Replace(text, "");
    }

    private int GetPageNumberForPosition(Dictionary<int, int> pageInfo, int position)
    {
        if (!pageInfo.Any()) return 1;

        // Find the last page marker before or at this position
        var relevantPages = pageInfo.Where(kv => kv.Key <= position).OrderBy(kv => kv.Key);
        return relevantPages.LastOrDefault().Value;
    }

    private int FindOptimalBreakPoint(string text, int start, int maxEnd)
    {
        // Look for sentence endings near the max end position
        var searchStart = Math.Max(start, maxEnd - (int)(MaxChunkSize * TokenToCharRatio * 0.3));
        var searchText = text.Substring(searchStart, maxEnd - searchStart);

        var sentenceMatches = SentenceEndRegex.Matches(searchText);

        if (sentenceMatches.Count > 0)
        {
            // Use the last sentence ending
            var lastMatch = sentenceMatches[sentenceMatches.Count - 1];
            return searchStart + lastMatch.Index + lastMatch.Length;
        }

        // Fall back to word boundary
        var wordBoundaries = WordBoundaryRegex.Matches(text.Substring(maxEnd - 50, Math.Min(50, text.Length - maxEnd + 50)));
        if (wordBoundaries.Count > 1)
        {
            return maxEnd - 50 + wordBoundaries[wordBoundaries.Count / 2].Index;
        }

        return maxEnd;
    }

    private int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        return WhitespaceRegex.Split(text.Trim())
            .Count(w => !string.IsNullOrWhiteSpace(w));
    }

    private bool HasStructuredContent(string text)
    {
        // Check for common structured content markers
        var structureMarkers = new[]
        {
            "[HEADING]", "[TABLE", "[ROW", "[SHEET", "[SLIDE", "[COMMENTS]", "[FOOTNOTES]"
        };

        return structureMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}