using AspNetCoreRateLimit;
using DocumentProcessingAPI.Core.Configuration;
using DocumentProcessingAPI.Core.DTOs;
using DocumentProcessingAPI.Core.Interfaces;
using DocumentProcessingAPI.Infrastructure.Data;
using DocumentProcessingAPI.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Serilog;
using System.IO.Abstractions;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Console()
    .WriteTo.File("logs/documentprocessing-.txt", rollingInterval: RollingInterval.Day)
    .WriteTo.Logger(lc => lc
        .Filter.ByIncludingOnly(evt => evt.Level == Serilog.Events.LogEventLevel.Error
            && evt.Properties.ContainsKey("FailedRecord"))
        .WriteTo.File("logs/failed-records-.txt",
            rollingInterval: RollingInterval.Day,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} | URI: {RecordUri} | Title: {RecordTitle} | Error: {Message}{NewLine}{Exception}"))
    .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container
builder.Services.AddControllersWithViews() // Changed from AddControllers() to support MVC
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.MaxDepth = 64;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Document Processing API",
        Version = "v1",
        Description = "API for document processing and semantic search",
        Contact = new Microsoft.OpenApi.Models.OpenApiContact
        {
            Name = "Document Processing API",
            Email = "support@documentprocessing.com"
        }
    });

    // Include XML comments
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }

    // Exclude MVC controllers from Swagger (only show API controllers)
    options.DocInclusionPredicate((docName, apiDesc) =>
    {
        // Only include API controllers (those in /api/ routes)
        return apiDesc.RelativePath?.StartsWith("api/") == true;
    });
});

// Database - SQL Server for Documents, PostgreSQL for Embeddings
builder.Services.AddDbContext<DocumentProcessingDbContext>(options =>
{
    // Use PostgreSQL for embeddings with pgvector support
    options.UseNpgsql(builder.Configuration.GetConnectionString("PostgresConnection"),
        npgsqlOptions => npgsqlOptions.UseVector());

    // Optionally also configure SQL Server if you want to keep document management
    // options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
});

// Vertex AI configuration
// Uses Google Cloud authentication (gcloud auth application-default login)
// No HTTP client needed - Vertex AI SDK handles authentication
// Configuration is in appsettings.json under "VertexAI" section

// File System Abstraction
builder.Services.AddSingleton<IFileSystem, FileSystem>();
// Add TRIM settings configuration
builder.Services.Configure<TrimSettings>(builder.Configuration.GetSection("TRIM"));

// Add Content Manager Services
builder.Services.AddScoped<ContentManagerServices>();

// Application Services
builder.Services.AddScoped<IDocumentProcessor, DocumentProcessor>();
builder.Services.AddScoped<ITextChunkingService, TextChunkingService>();
builder.Services.AddScoped<IEmbeddingService, GeminiEmbeddingService>(); // Google Gemini embedding service

// Vector Database - PostgreSQL with pgvector
builder.Services.AddScoped<PgVectorService>(); // PostgreSQL vector database service

builder.Services.AddScoped<IFileStorageService, LocalFileStorageService>();
// DocumentService, SearchService, and RagService removed - only using Content Manager record services
// builder.Services.AddScoped<IRagService, RagService>(); // RAG service with Gemini embeddings

// Content Manager Record Embedding Services
builder.Services.AddScoped<IRecordEmbeddingService, RecordEmbeddingService>();
builder.Services.AddScoped<IRecordSearchService, RecordSearchService>();

// Record Search Helper Services (segregated from RecordSearchService)
builder.Services.AddScoped<IRecordSearchHelperServices, RecordSearchHelperServices>();
builder.Services.AddScoped<IRecordSearchGoogleServices, RecordSearchGoogleServices>();

// AI Record Services (Summary and Q&A using Gemini)
builder.Services.AddScoped<IAIRecordService, AIRecordService>();

// Quartz Scheduler for Content Manager Record Sync----------------------------------------------------------
//builder.Services.AddQuartz(q =>
//{
//    // Use a scoped job factory to support DI in jobs
//    q.UseMicrosoftDependencyInjectionJobFactory();

//    // Get configuration
//    var cronSchedule = builder.Configuration["RecordSync:CronSchedule"] ?? "0 0 * * * ?"; // Default: Every hour
//    var searchString = builder.Configuration["RecordSync:SearchString"] ?? "*";
//    var enableSync = bool.Parse(builder.Configuration["RecordSync:Enabled"] ?? "true");

//    // Create job
//    var jobKey = new Quartz.JobKey("record-sync-job", "content-manager-sync");
//    q.AddJob<DocumentProcessingAPI.Infrastructure.Jobs.RecordSyncJob>(opts => opts
//        .WithIdentity(jobKey)
//        .WithDescription("Syncs Content Manager records and generates embeddings")
//        .UsingJobData("SearchString", searchString)
//        .UsingJobData("EnableSync", enableSync)
//        .StoreDurably());

//    // Create trigger with cron schedule
//    q.AddTrigger(opts => opts
//        .ForJob(jobKey)
//        .WithIdentity("record-sync-trigger", "content-manager-sync")
//        .WithCronSchedule(cronSchedule)
//        .WithDescription($"Sync Content Manager records: {cronSchedule}"));
//});

//// Add Quartz hosted service
//builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

// Add scheduler management service
//builder.Services.AddSingleton<RecordSyncSchedulerService>();
//------------------------------------------------------------------------------------------------------------



// Health Checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<DocumentProcessingDbContext>();

// Rate Limiting
builder.Services.AddOptions();
builder.Services.AddMemoryCache();
builder.Services.Configure<IpRateLimitOptions>(builder.Configuration.GetSection("IpRateLimiting"));
builder.Services.AddSingleton<IIpPolicyStore, MemoryCacheIpPolicyStore>();
builder.Services.AddSingleton<IRateLimitCounterStore, MemoryCacheRateLimitCounterStore>();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
builder.Services.AddSingleton<IProcessingStrategy, AsyncKeyLockProcessingStrategy>();

// CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Configure JSON options for better API responses
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});

// Add TRIM settings configuration
builder.Services.Configure<TrimSettings>(builder.Configuration.GetSection("TRIM"));

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Document Processing API V1");
        c.RoutePrefix = "swagger"; // Changed from empty to "swagger" so MVC can use root
    });
}

// Global Error Handler - differentiate between API and MVC
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.ContentType = "application/json";

        var exceptionHandlerPathFeature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();
        var exception = exceptionHandlerPathFeature?.Error;

        // Check if this is an API request
        var path = context.Request.Path.Value ?? "";
        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            // Return JSON error for API requests
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;

            var problemDetails = new
            {
                type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                title = "Internal Server Error",
                status = StatusCodes.Status500InternalServerError,
                detail = exception?.Message ?? "An unexpected error occurred while processing your request.",
                traceId = context.TraceIdentifier
            };

            await context.Response.WriteAsJsonAsync(problemDetails);
        }
        else
        {
            // Redirect to MVC error page for web requests
            context.Response.Redirect("/Home/Error");
        }
    });
});

app.UseHttpsRedirection();

// Static files for MVC (CSS, JS, images)
app.UseStaticFiles();

app.UseCors();

// Rate Limiting
app.UseIpRateLimiting();

app.UseRouting();
app.UseAuthorization();

// Health Checks
app.MapHealthChecks("/health");

// MVC Routes - Default route for MVC pages (Search is the landing page)
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Search}/{id?}");

// API Controllers - Keep API endpoints working
app.MapControllers();

// Initialize database
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<DocumentProcessingDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        // Ensure database is created
        await context.Database.EnsureCreatedAsync();
        logger.LogInformation("✅ PostgreSQL database initialized successfully");

        // Initialize pgvector extension
        try
        {
            var pgVectorService = scope.ServiceProvider.GetRequiredService<PgVectorService>();
            await pgVectorService.InitializeAsync();
            logger.LogInformation("✅ pgvector extension initialized successfully");

            // Get collection stats
            var (count, lastIndexed) = await pgVectorService.GetCollectionStatsAsync();
            logger.LogInformation("📊 Embeddings stats - Total: {Count}, Last Indexed: {LastIndexed}",
                count, lastIndexed?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Never");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Failed to initialize pgvector extension");
            logger.LogWarning("⚠️ API will start but vector search may not work properly.");
            logger.LogWarning("⚠️ Please ensure PostgreSQL has pgvector extension installed.");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "❌ Failed to initialize database systems");
        throw;
    }
}

try
{
    Log.Information("Starting Document Processing API");
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// Make Program class available for testing
public partial class Program { }
