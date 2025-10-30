using Microsoft.AspNetCore.Mvc;
using Npgsql;
using DocumentProcessingAPI.Infrastructure.Services;

namespace DocumentProcessingAPI.API.Controllers;

/// <summary>
/// Test controller for verifying PostgreSQL connection and pgvector extension
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class TestController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<TestController> _logger;
    private readonly PgVectorService _pgVectorService;

    public TestController(IConfiguration configuration, ILogger<TestController> logger, PgVectorService pgVectorService)
    {
        _configuration = configuration;
        _logger = logger;
        _pgVectorService = pgVectorService;
    }

    /// <summary>
    /// Test PostgreSQL connection
    /// GET /api/test/postgres-connection
    /// </summary>
    [HttpGet("postgres-connection")]
    public async Task<IActionResult> TestPostgresConnection()
    {
        try
        {
            var connectionString = _configuration.GetConnectionString("PostgresConnection");

            if (string.IsNullOrEmpty(connectionString))
            {
                return BadRequest(new { error = "PostgreSQL connection string not found in appsettings.json" });
            }

            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            _logger.LogInformation("✅ Successfully connected to PostgreSQL");

            // Get PostgreSQL version
            using var cmd = new NpgsqlCommand("SELECT version()", connection);
            var version = await cmd.ExecuteScalarAsync();

            return Ok(new
            {
                success = true,
                message = "Successfully connected to PostgreSQL",
                connectionString = MaskPassword(connectionString),
                postgresVersion = version?.ToString(),
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to connect to PostgreSQL");
            return StatusCode(500, new
            {
                success = false,
                error = "Failed to connect to PostgreSQL",
                message = ex.Message,
                stackTrace = ex.StackTrace
            });
        }
    }

    /// <summary>
    /// Check if pgvector extension is installed
    /// GET /api/test/pgvector-status
    /// </summary>
    [HttpGet("pgvector-status")]
    public async Task<IActionResult> TestPgvectorStatus()
    {
        try
        {
            var connectionString = _configuration.GetConnectionString("PostgresConnection");

            if (string.IsNullOrEmpty(connectionString))
            {
                return BadRequest(new { error = "PostgreSQL connection string not found in appsettings.json" });
            }

            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            // Check if pgvector extension exists
            using var cmd = new NpgsqlCommand(
                "SELECT EXISTS(SELECT 1 FROM pg_available_extensions WHERE name = 'vector')",
                connection);
            var extensionAvailable = (bool?)await cmd.ExecuteScalarAsync() ?? false;

            // Check if pgvector extension is installed
            using var cmd2 = new NpgsqlCommand(
                "SELECT EXISTS(SELECT 1 FROM pg_extension WHERE extname = 'vector')",
                connection);
            var extensionInstalled = (bool?)await cmd2.ExecuteScalarAsync() ?? false;

            return Ok(new
            {
                success = true,
                pgvectorAvailable = extensionAvailable,
                pgvectorInstalled = extensionInstalled,
                message = extensionInstalled
                    ? "✅ pgvector extension is installed and ready"
                    : extensionAvailable
                        ? "⚠️ pgvector extension is available but not installed. Run: CREATE EXTENSION vector;"
                        : "❌ pgvector extension is NOT available. Please install pgvector on PostgreSQL first.",
                installationInstructions = !extensionAvailable ? GetInstallationInstructions() : null,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to check pgvector status");
            return StatusCode(500, new
            {
                success = false,
                error = "Failed to check pgvector status",
                message = ex.Message
            });
        }
    }

    /// <summary>
    /// Install pgvector extension (requires superuser permissions)
    /// POST /api/test/install-pgvector
    /// </summary>
    [HttpPost("install-pgvector")]
    public async Task<IActionResult> InstallPgvector()
    {
        try
        {
            var connectionString = _configuration.GetConnectionString("PostgresConnection");

            if (string.IsNullOrEmpty(connectionString))
            {
                return BadRequest(new { error = "PostgreSQL connection string not found in appsettings.json" });
            }

            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            // Try to create pgvector extension
            using var cmd = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS vector", connection);
            await cmd.ExecuteNonQueryAsync();

            _logger.LogInformation("✅ Successfully installed pgvector extension");

            return Ok(new
            {
                success = true,
                message = "✅ pgvector extension installed successfully",
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to install pgvector extension");
            return StatusCode(500, new
            {
                success = false,
                error = "Failed to install pgvector extension",
                message = ex.Message,
                hint = "Make sure the user has superuser permissions and pgvector is installed on the PostgreSQL server",
                installationInstructions = GetInstallationInstructions()
            });
        }
    }

    private string MaskPassword(string connectionString)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            if (!string.IsNullOrEmpty(builder.Password))
            {
                builder.Password = "****";
            }
            return builder.ConnectionString;
        }
        catch
        {
            return connectionString;
        }
    }

    private object GetInstallationInstructions()
    {
        return new
        {
            windows = new
            {
                step1 = "Download pgvector from: https://github.com/pgvector/pgvector/releases",
                step2 = "Extract the files to your PostgreSQL installation directory",
                step3 = "Copy vector.dll to PostgreSQL\\lib directory",
                step4 = "Copy vector.control and vector--*.sql files to PostgreSQL\\share\\extension directory",
                step5 = "Restart PostgreSQL service",
                alternative = "Or use pgAdmin Query Tool to run: CREATE EXTENSION vector; (requires extension files installed)"
            },
            docker = new
            {
                command = "Use ankane/pgvector Docker image: docker run -d -p 5432:5432 -e POSTGRES_PASSWORD=yourpassword ankane/pgvector"
            },
            linux = new
            {
                ubuntu = "sudo apt install postgresql-<version>-pgvector",
                fedora = "sudo dnf install pgvector"
            }
        };
    }

    /// <summary>
    /// Test saving dummy embeddings to PostgreSQL
    /// POST /api/test/save-dummy-embeddings
    /// </summary>
    [HttpPost("save-dummy-embeddings")]
    public async Task<IActionResult> SaveDummyEmbeddings([FromQuery] int count = 5)
    {
        try
        {
            _logger.LogInformation("Creating {Count} dummy embeddings for testing", count);

            var random = new Random();
            var vectorData = new List<DocumentProcessingAPI.Infrastructure.Services.VectorData>();

            for (int i = 0; i < count; i++)
            {
                // Generate random 3072-dimensional vector
                var vector = new float[3072];
                for (int j = 0; j < 3072; j++)
                {
                    vector[j] = (float)(random.NextDouble() * 2 - 1); // Random values between -1 and 1
                }

                var metadata = new Dictionary<string, object>
                {
                    ["record_uri"] = 1000 + i,
                    ["record_title"] = $"Test Record {i}",
                    ["date_created"] = DateTime.UtcNow.AddDays(-i).ToString("o"),
                    ["record_type"] = "Document",
                    ["container"] = "Test Container",
                    ["assignee"] = "Test User",
                    ["all_parts"] = "Part 1, Part 2",
                    ["acl"] = "Public",
                    ["chunk_index"] = 0,
                    ["chunk_sequence"] = 0,
                    ["total_chunks"] = 1,
                    ["token_count"] = 100,
                    ["start_position"] = 0,
                    ["end_position"] = 100,
                    ["page_number"] = 1,
                    ["chunk_content"] = $"This is test content for record {i}. It contains some dummy text to simulate real content.",
                    ["content_preview"] = $"This is test content for record {i}...",
                    ["file_extension"] = ".txt",
                    ["file_type"] = "txt",
                    ["document_category"] = "Text Document",
                    ["entity_type"] = "content_manager_record",
                    ["indexed_at"] = DateTime.UtcNow.ToString("o")
                };

                vectorData.Add(new DocumentProcessingAPI.Infrastructure.Services.VectorData
                {
                    Id = $"test_embedding_{i}_{Guid.NewGuid()}",
                    Vector = vector,
                    Metadata = metadata
                });
            }

            // Save using PgVectorService
            await _pgVectorService.SaveEmbeddingsBatchAsync(vectorData);

            _logger.LogInformation("✅ Successfully saved {Count} dummy embeddings", count);

            return Ok(new
            {
                success = true,
                message = $"Successfully saved {count} dummy embeddings",
                embeddingIds = vectorData.Select(v => v.Id).ToList(),
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to save dummy embeddings");
            return StatusCode(500, new
            {
                success = false,
                error = "Failed to save dummy embeddings",
                message = ex.Message,
                stackTrace = ex.StackTrace
            });
        }
    }
}
