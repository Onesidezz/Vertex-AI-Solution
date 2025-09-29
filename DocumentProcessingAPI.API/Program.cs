using DocumentProcessingAPI.Core.Interfaces;
using DocumentProcessingAPI.Infrastructure.Data;
using DocumentProcessingAPI.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.IO.Abstractions;
using System.Reflection;
using AspNetCoreRateLimit;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Console()
    .WriteTo.File("logs/documentprocessing-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container
builder.Services.AddControllers();
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
});

// Database
builder.Services.AddDbContext<DocumentProcessingDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Google Gemini API configuration
// Requires valid Gemini API key for embedding generation
// Google Gemini API configuration
builder.Services.AddHttpClient("Gemini", client =>
{
    client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/");
    client.DefaultRequestHeaders.Add("x-goog-api-key", builder.Configuration["Gemini:ApiKey"]);
});
// Required for GeminiEmbeddingService

// File System Abstraction
builder.Services.AddSingleton<IFileSystem, FileSystem>();

// Application Services
builder.Services.AddScoped<IDocumentProcessor, DocumentProcessor>();
builder.Services.AddScoped<ITextChunkingService, TextChunkingService>();
builder.Services.AddScoped<IEmbeddingService, GeminiEmbeddingService>(); // Google Gemini embedding service
builder.Services.AddScoped<ILocalEmbeddingStorageService, LocalEmbeddingStorageService>(); // Local embedding storage
builder.Services.AddScoped<IFileStorageService, LocalFileStorageService>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<ISearchService, SearchService>();
builder.Services.AddScoped<IRagService, RagService>(); // RAG service with Gemini embeddings

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

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Document Processing API V1");
        c.RoutePrefix = string.Empty; // Set Swagger UI at the app's root
    });
}

// Global Error Handler
app.UseExceptionHandler("/error");

app.UseHttpsRedirection();
app.UseCors();

// Rate Limiting
app.UseIpRateLimiting();

app.UseRouting();
app.UseAuthorization();

// Health Checks
app.MapHealthChecks("/health");

// Controllers
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
        logger.LogInformation("Database initialized successfully");

        // Ensure embeddings directory exists
        var embeddingsPath = builder.Configuration["Embeddings:StoragePath"] ?? "C:\\Users\\ukhan2\\source\\repos\\DocumentProcessingAPI\\Embeddings";
        Directory.CreateDirectory(embeddingsPath);
        logger.LogInformation("Embeddings directory initialized: {Path}", embeddingsPath);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to initialize database systems");
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
