using Microsoft.AspNetCore.Mvc;
using Npgsql;
using DocumentProcessingAPI.Infrastructure.Services;
using DocumentProcessingAPI.Core.DTOs;
using DocumentProcessingAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using TRIM.SDK;

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
    private readonly ContentManagerServices _contentManagerServices;
    private readonly DocumentProcessingDbContext _context;

    public TestController(
        IConfiguration configuration,
        ILogger<TestController> logger,
        PgVectorService pgVectorService,
        ContentManagerServices contentManagerServices,
        DocumentProcessingDbContext context)
    {
        _configuration = configuration;
        _logger = logger;
        _pgVectorService = pgVectorService;
        _contentManagerServices = contentManagerServices;
        _context = context;
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

    /// <summary>
    /// Backfill SourceDateModified field by fetching DateModified from Content Manager
    /// POST /api/test/backfill-source-date-modified
    /// </summary>
    [HttpPost("backfill-source-date-modified")]
    public async Task<IActionResult> BackfillSourceDateModified()
    {
        var startTime = DateTime.UtcNow;

        try
        {
            _logger.LogInformation("════════════════════════════════════════");
            _logger.LogInformation("🔄 Starting SourceDateModified Backfill");
            _logger.LogInformation("════════════════════════════════════════");

            // Get all distinct RecordUris that need updating
            var recordUrisToUpdate = await _context.Embeddings
                .Where(e => e.SourceDateModified == null)
                .Select(e => e.RecordUri)
                .Distinct()
                .ToListAsync();

            _logger.LogInformation("📊 Found {Count} unique records to backfill", recordUrisToUpdate.Count);

            if (recordUrisToUpdate.Count == 0)
            {
                return Ok(new
                {
                    success = true,
                    message = "✅ No records need backfilling - all records already have SourceDateModified",
                    recordsProcessed = 0,
                    embeddingsUpdated = 0,
                    duration = "0s"
                });
            }

            // Get TRIM settings from configuration
            var dataSetId = _configuration["TRIM:DataSetId"];
            var workgroupServerUrl = _configuration["TRIM:WorkgroupServerUrl"];

            if (string.IsNullOrEmpty(dataSetId) || string.IsNullOrEmpty(workgroupServerUrl))
            {
                return BadRequest(new
                {
                    success = false,
                    error = "TRIM settings not found in configuration",
                    message = "Please configure TRIM:DataSetId and TRIM:WorkgroupServerUrl in appsettings.json"
                });
            }

            _logger.LogInformation("🔌 Connecting to Content Manager...");
            _logger.LogInformation("  • DataSetId: {DataSetId}", dataSetId);
            _logger.LogInformation("  • WorkgroupServerURL: {Url}", workgroupServerUrl);

            // Create a NEW database connection (same as ContentManagerServices)
            var database = new TRIM.SDK.Database()
            {
                Id = dataSetId,
                WorkgroupServerURL = workgroupServerUrl
            };

            database.Connect();

            if (!database.IsConnected)
            {
                return StatusCode(500, new
                {
                    success = false,
                    error = "Failed to connect to Content Manager"
                });
            }

            var connectedUser = database.CurrentUser?.Name ?? "Unknown";
            _logger.LogInformation("✅ Connected to Content Manager as: {User}", connectedUser);

            // PHASE 1: Fetch DateModified from Content Manager (synchronous, no async!)
            _logger.LogInformation("📥 Phase 1: Fetching DateModified from Content Manager...");
            var recordTimestamps = new Dictionary<long, DateTime>();
            var failedRecords = new List<(long uri, string error)>();
            int fetchedCount = 0;
            int fetchFailedCount = 0;

            try
            {
                foreach (var recordUri in recordUrisToUpdate)
                {
                    try
                    {
                        // Fetch record from Content Manager (synchronous!)
                        var record = new Record(database, recordUri);

                        // Get DateModified (synchronous!)
                        var dateModified = record.DateModified.ToDateTime();
                        var dateModifiedUtc = DateTime.SpecifyKind(dateModified, DateTimeKind.Utc);

                        // Store in memory
                        recordTimestamps[recordUri] = dateModifiedUtc;
                        fetchedCount++;

                        // Log progress every 50 records
                        if (fetchedCount % 50 == 0)
                        {
                            _logger.LogInformation("  ⏳ Fetched: {Fetched}/{Total} records",
                                fetchedCount, recordUrisToUpdate.Count);
                        }
                    }
                    catch (Exception ex)
                    {
                        fetchFailedCount++;
                        failedRecords.Add((recordUri, ex.Message));
                        _logger.LogWarning(ex, "  ⚠️ Failed to fetch record URI {Uri}", recordUri);
                    }
                }
            }
            finally
            {
                // Dispose the database connection BEFORE async operations
                database.Dispose();
                _logger.LogInformation("🔌 Disconnected from Content Manager");
            }

            _logger.LogInformation("✅ Phase 1 Complete: Fetched {Success} records, {Failed} failed",
                fetchedCount, fetchFailedCount);

            // PHASE 2: Update PostgreSQL (async operations are safe now!)
            _logger.LogInformation("📤 Phase 2: Updating PostgreSQL...");
            int recordsProcessed = 0;
            int embeddingsUpdated = 0;

            foreach (var kvp in recordTimestamps)
            {
                try
                {
                    var recordUri = kvp.Key;
                    var dateModifiedUtc = kvp.Value;

                    // Update all embeddings for this RecordUri in PostgreSQL
                    var affectedRows = await _context.Embeddings
                        .Where(e => e.RecordUri == recordUri)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(e => e.SourceDateModified, dateModifiedUtc));

                    embeddingsUpdated += affectedRows;
                    recordsProcessed++;

                    // Log progress every 50 records
                    if (recordsProcessed % 50 == 0)
                    {
                        _logger.LogInformation("  ⏳ Updated: {Processed}/{Total} records, {Embeddings} embeddings",
                            recordsProcessed, recordTimestamps.Count, embeddingsUpdated);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "  ⚠️ Failed to update PostgreSQL for record URI {Uri}", kvp.Key);
                }
            }

            _logger.LogInformation("✅ Phase 2 Complete: Updated {Success} records, {Embeddings} embeddings",
                recordsProcessed, embeddingsUpdated);

            var duration = DateTime.UtcNow - startTime;

            _logger.LogInformation("════════════════════════════════════════");
            _logger.LogInformation("✅ Backfill Complete");
            _logger.LogInformation("════════════════════════════════════════");
            _logger.LogInformation("📊 Statistics:");
            _logger.LogInformation("  • Records Found: {Found}", recordUrisToUpdate.Count);
            _logger.LogInformation("  • Records Fetched from CM: {Fetched}", fetchedCount);
            _logger.LogInformation("  • Records Updated in PostgreSQL: {Updated}", recordsProcessed);
            _logger.LogInformation("  • Embeddings Updated: {Embeddings}", embeddingsUpdated);
            _logger.LogInformation("  • Records Failed: {Failed}", fetchFailedCount);
            _logger.LogInformation("  • Duration: {Duration:mm\\:ss}", duration);

            return Ok(new
            {
                success = true,
                message = "✅ Backfill completed successfully",
                statistics = new
                {
                    totalRecordsFound = recordUrisToUpdate.Count,
                    recordsFetchedFromCM = fetchedCount,
                    recordsUpdatedInPostgreSQL = recordsProcessed,
                    embeddingsUpdated,
                    recordsFailed = fetchFailedCount,
                    successRate = recordUrisToUpdate.Count > 0
                        ? $"{fetchedCount * 100.0 / recordUrisToUpdate.Count:F2}%"
                        : "N/A"
                },
                duration = $"{duration.TotalSeconds:F2}s",
                failedRecords = failedRecords.Take(10).Select(f => new
                {
                    uri = f.uri,
                    error = f.error
                }).ToList(),
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            var duration = DateTime.UtcNow - startTime;
            _logger.LogError(ex, "❌ Backfill failed after {Duration:mm\\:ss}", duration);

            return StatusCode(500, new
            {
                success = false,
                error = "Backfill operation failed",
                message = ex.Message,
                duration = $"{duration.TotalSeconds:F2}s",
                stackTrace = ex.StackTrace
            });
        }
    }
}
