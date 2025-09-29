using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using DocumentProcessingAPI.Infrastructure.Data;
using System.Text;
using System.Text.Json;
using DocumentProcessingAPI.Core.DTOs;

namespace DocumentProcessingAPI.Tests.Integration;

public class DocumentsControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public DocumentsControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Replace the database with in-memory database for testing
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<DocumentProcessingDbContext>));

                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<DocumentProcessingDbContext>(options =>
                {
                    options.UseInMemoryDatabase("TestDb");
                });
            });
        });

        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task GetDocuments_ReturnsSuccessStatusCode()
    {
        // Act
        var response = await _client.GetAsync("/api/documents");

        // Assert
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task GetDocuments_ReturnsJsonContent()
    {
        // Act
        var response = await _client.GetAsync("/api/documents");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Contains("documents", content.ToLower());
        Assert.Contains("totalCount", content.ToLower());
    }

    [Fact]
    public async Task GetHealth_ReturnsHealthy()
    {
        // Act
        var response = await _client.GetAsync("/health");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Healthy", content);
    }

    [Fact]
    public async Task Search_WithValidRequest_ReturnsSearchResults()
    {
        // Arrange
        var searchRequest = new SearchRequestDto
        {
            Query = "test query",
            TopK = 5,
            MinimumScore = 0.0f
        };

        var json = JsonSerializer.Serialize(searchRequest);
        var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/search", httpContent);

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("query", content.ToLower());
        Assert.Contains("results", content.ToLower());
    }
}