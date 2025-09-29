using DocumentProcessingAPI.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace DocumentProcessingAPI.Tests.Services;

public class TextChunkingServiceTests
{
    private readonly Mock<ILogger<TextChunkingService>> _mockLogger;
    private readonly TextChunkingService _service;

    public TextChunkingServiceTests()
    {
        _mockLogger = new Mock<ILogger<TextChunkingService>>();
        _service = new TextChunkingService(_mockLogger.Object);
    }

    [Fact]
    public async Task ChunkTextAsync_WithValidText_ReturnsChunks()
    {
        // Arrange
        var text = "This is a test document. It contains multiple sentences. " +
                  "Each sentence provides some context. The text should be chunked properly.";

        // Act
        var chunks = await _service.ChunkTextAsync(text, chunkSize: 10, overlap: 2);

        // Assert
        Assert.NotEmpty(chunks);
        Assert.All(chunks, chunk =>
        {
            Assert.NotNull(chunk.Content);
            Assert.True(chunk.TokenCount > 0);
            Assert.True(chunk.Sequence >= 0);
        });
    }

    [Fact]
    public async Task ChunkTextAsync_WithEmptyText_ReturnsEmptyList()
    {
        // Arrange
        var text = "";

        // Act
        var chunks = await _service.ChunkTextAsync(text);

        // Assert
        Assert.Empty(chunks);
    }

    [Fact]
    public async Task CountTokensAsync_WithValidText_ReturnsPositiveCount()
    {
        // Arrange
        var text = "Hello world, this is a test.";

        // Act
        var tokenCount = await _service.CountTokensAsync(text);

        // Assert
        Assert.True(tokenCount > 0);
    }

    [Fact]
    public void ValidateChunkingParameters_WithValidParameters_ReturnsTrue()
    {
        // Act
        var isValid = _service.ValidateChunkingParameters(1000, 200);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void ValidateChunkingParameters_WithInvalidChunkSize_ReturnsFalse()
    {
        // Act
        var isValid = _service.ValidateChunkingParameters(50, 200); // chunk size too small

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void ValidateChunkingParameters_WithOverlapTooLarge_ReturnsFalse()
    {
        // Act
        var isValid = _service.ValidateChunkingParameters(1000, 1000); // overlap equals chunk size

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public async Task ChunkTextAsync_PreservesPageInformation_WhenPageMarkersPresent()
    {
        // Arrange
        var text = "[PAGE 1]\nThis is page one content.\n[PAGE 2]\nThis is page two content.";

        // Act
        var chunks = await _service.ChunkTextAsync(text, chunkSize: 50, overlap: 10);

        // Assert
        Assert.NotEmpty(chunks);
        Assert.Contains(chunks, c => c.PageNumber == 1);
        Assert.Contains(chunks, c => c.PageNumber == 2);
    }
}