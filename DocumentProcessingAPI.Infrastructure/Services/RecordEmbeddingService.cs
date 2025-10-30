using DocumentProcessingAPI.Core.DTOs;
using DocumentProcessingAPI.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace DocumentProcessingAPI.Infrastructure.Services
{
    /// <summary>
    /// Service for processing Content Manager records and generating embeddings
    /// Combines record data with all metadata fields into a single embedding
    /// </summary>
    public class RecordEmbeddingService : IRecordEmbeddingService
    {
        private readonly ContentManagerServices _contentManagerServices;
        private readonly IEmbeddingService _embeddingService;
        private readonly PgVectorService _pgVectorService;
        private readonly IDocumentProcessor _documentProcessor;
        private readonly ITextChunkingService _textChunkingService;
        private readonly ILogger<RecordEmbeddingService> _logger;
        private const string RECORD_COLLECTION_PREFIX = "cm_record_";
        private const int CHUNK_SIZE = 1500; // tokens
        private const int CHUNK_OVERLAP = 150; // tokens

        public RecordEmbeddingService(
            ContentManagerServices contentManagerServices,
            IEmbeddingService embeddingService,
            PgVectorService pgVectorService,
            IDocumentProcessor documentProcessor,
            ITextChunkingService textChunkingService,
            ILogger<RecordEmbeddingService> logger)
        {
            _contentManagerServices = contentManagerServices;
            _embeddingService = embeddingService;
            _pgVectorService = pgVectorService;
            _documentProcessor = documentProcessor;
            _textChunkingService = textChunkingService;
            _logger = logger;
        }

        /// <summary>
        /// Process all records from Content Manager based on search criteria
        /// Skips records that are already embedded (checks if embedding exists in Qdrant)
        /// </summary>
        public async Task<int> ProcessAllRecordsAsync(string searchString = "*")
        {
            try
            {
                _logger.LogInformation("========================================");
                _logger.LogInformation("🚀 STARTING BATCH EMBEDDING PROCESS");
                _logger.LogInformation("========================================");
                _logger.LogInformation("Search Criteria: {SearchString}", searchString);
                _logger.LogInformation("Start Time: {StartTime}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                var startTime = DateTime.Now;

                // Ensure Content Manager is connected
                await _contentManagerServices.ConnectDatabaseAsync();

                // Get all records
                var records = await _contentManagerServices.GetRecordsAsync(searchString);
                _logger.LogInformation("📊 Retrieved {Count} records from Content Manager", records.Count);

                if (!records.Any())
                {
                    _logger.LogWarning("⚠️ No records found matching search criteria");
                    return 0;
                }

                // Process each record and generate embeddings
                var vectorDataList = new List<VectorData>();
                int processedCount = 0;
                int skippedCount = 0;
                int totalChunks = 0;
                int documentsWithContent = 0;
                int containersProcessed = 0;
                int recordsWithoutContent = 0;
                int currentRecordIndex = 0;
                int failedCount = 0;
                var failedRecords = new List<(long uri, string title, string error)>();

                _logger.LogInformation("");
                _logger.LogInformation("📝 Starting record processing loop...");
                _logger.LogInformation("");

                foreach (var record in records)
                {
                    currentRecordIndex++;
                    var percentComplete = (double)currentRecordIndex / records.Count * 100;

                    try
                    {
                        // Only log every 10th record progress for successful records to reduce noise
                        if (currentRecordIndex % 10 == 0 || currentRecordIndex == 1)
                        {
                            _logger.LogInformation("📊 Progress: [{Current}/{Total}] - {Percentage:F1}% Complete",
                                currentRecordIndex, records.Count, percentComplete);
                        }

                        // Check if this record already has embeddings in PostgreSQL
                        // Format: cm_record_{URI}_chunk_0
                        var firstChunkId = $"{RECORD_COLLECTION_PREFIX}{record.URI}_chunk_0";
                        var existing = await _pgVectorService.GetEmbeddingAsync(firstChunkId);

                        if (existing != null)
                        {
                            skippedCount++;
                            _logger.LogWarning("⏭️ SKIPPING - Record already embedded in PostgreSQL");
                            _logger.LogInformation("");
                            continue; // Skip this record - already processed
                        }

                        // Build comprehensive text representation (includes document content if electronic)
                        // Process ALL records: containers, records with content, records without content
                        var (metadataText, documentContent) = await BuildRecordTextComponentsAsync(record);

                        // Track record types
                        string recordCategory = "";
                        if (record.IsContainer == "Container")
                        {
                            containersProcessed++;
                            recordCategory = "Container (Metadata Only)";
                        }
                        else if (!string.IsNullOrWhiteSpace(documentContent))
                        {
                            documentsWithContent++;
                            recordCategory = "Document with Content";
                        }
                        else
                        {
                            recordsWithoutContent++;
                            recordCategory = "Record without Content (Metadata Only)";
                        }

                        // Combine metadata + document content
                        var fullRecordText = metadataText;
                        if (!string.IsNullOrWhiteSpace(documentContent))
                        {
                            fullRecordText += "\n\n--- Document Content ---\n" + documentContent;
                        }

                        _logger.LogInformation("📊 Processing Results:");
                        _logger.LogInformation("   • Category: {Category}", recordCategory);
                        _logger.LogInformation("   • Metadata Size: {MetadataSize} chars", metadataText.Length);
                        if (!string.IsNullOrWhiteSpace(documentContent))
                        {
                            _logger.LogInformation("   • Document Content Size: {ContentSize} chars", documentContent.Length);
                        }
                        _logger.LogInformation("   • Total Text Size: {TotalSize} chars", fullRecordText.Length);

                        // Chunk the text
                        _logger.LogInformation("✂️ Chunking text for embedding...");
                        var textChunks = await _textChunkingService.ChunkTextAsync(fullRecordText, CHUNK_SIZE, CHUNK_OVERLAP);

                        _logger.LogInformation("✅ Created {ChunkCount} chunk(s) for this record", textChunks.Count);

                        totalChunks += textChunks.Count;

                        // Build a concise metadata header to prepend to each chunk
                        // This ensures ALL chunks can match date-based queries
                        var metadataHeader = BuildChunkMetadataHeader(record);

                        // Generate embeddings for each chunk
                        _logger.LogInformation("🔢 Generating embeddings for {ChunkCount} chunk(s)...", textChunks.Count);
                        int chunkIndex = 0;
                        foreach (var textChunk in textChunks)
                        {
                            // Prepend metadata header to each chunk so date queries work on all chunks
                            var enrichedChunkContent = metadataHeader + "\n\n" + textChunk.Content;

                            // Generate embedding for the enriched chunk (with metadata header)
                            var embedding = await _embeddingService.GenerateEmbeddingAsync(enrichedChunkContent);

                            // Prepare metadata for this chunk
                            var metadata = BuildRecordMetadata(record);

                            // Add chunk-specific metadata
                            metadata["chunk_index"] = chunkIndex;
                            metadata["chunk_sequence"] = textChunk.Sequence;
                            metadata["total_chunks"] = textChunks.Count;
                            metadata["token_count"] = textChunk.TokenCount;
                            metadata["start_position"] = textChunk.StartPosition;
                            metadata["end_position"] = textChunk.EndPosition;
                            metadata["page_number"] = textChunk.PageNumber;
                            metadata["chunk_content"] = textChunk.Content; // Store full chunk content for synthesis
                            metadata["content_preview"] = textChunk.Content.Length > 100 ?
                                textChunk.Content.Substring(0, 100) + "..." : textChunk.Content;

                            // Create unique embedding ID based on URI (unique identifier)
                            // Format: cm_record_{URI}_chunk_{chunkIndex}
                            var embeddingId = $"{RECORD_COLLECTION_PREFIX}{record.URI}_chunk_{chunkIndex}";

                            // Add to batch
                            vectorDataList.Add(new VectorData
                            {
                                Id = embeddingId,
                                Vector = embedding,
                                Metadata = metadata
                            });

                            chunkIndex++;
                        }

                        processedCount++;

                        _logger.LogInformation("✅ RECORD PROCESSED SUCCESSFULLY");
                        _logger.LogInformation("   • Total Embeddings Generated: {EmbeddingCount}", textChunks.Count);
                        _logger.LogInformation("   • Embeddings in Batch Queue: {QueueSize}", vectorDataList.Count);
                        _logger.LogInformation("");

                        // Save in batches of 50 to avoid memory issues
                        if (vectorDataList.Count >= 50)
                        {
                            _logger.LogInformation("💾 Batch size reached (50+ embeddings). Saving to PostgreSQL...");
                            await _pgVectorService.SaveEmbeddingsBatchAsync(vectorDataList);
                            _logger.LogInformation("✅ Batch saved successfully. Queue cleared.");
                            _logger.LogInformation("");
                            vectorDataList.Clear();
                        }
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        failedRecords.Add((record.URI, record.Title ?? "Unknown", ex.Message));

                        // Log to separate failed records file using enriched properties
                        _logger.LogError(ex, "❌ FAILED to process record {RecordUri} - {RecordTitle}. Error: {ErrorMessage}. {FailedRecord}",
                            record.URI, record.Title ?? "Unknown", ex.Message, true);
                    }
                }

                // Save remaining embeddings
                if (vectorDataList.Any())
                {
                    _logger.LogInformation("💾 Saving final batch of {Count} embeddings to PostgreSQL", vectorDataList.Count);
                    await _pgVectorService.SaveEmbeddingsBatchAsync(vectorDataList);
                }

                var endTime = DateTime.Now;
                var duration = endTime - startTime;

                _logger.LogInformation("========================================");
                _logger.LogInformation("✅ BATCH EMBEDDING PROCESS COMPLETE");
                _logger.LogInformation("========================================");
                _logger.LogInformation("📊 SUMMARY STATISTICS:");
                _logger.LogInformation("  • Total Records Retrieved: {Total}", records.Count);
                _logger.LogInformation("  • Records Already Embedded (Skipped): {Skipped}", skippedCount);
                _logger.LogInformation("  • New Records Processed: {Processed}", processedCount);
                _logger.LogInformation("  • Failed Records: {Failed}", failedCount);
                _logger.LogInformation("    ├─ Documents with Content: {DocsWithContent}", documentsWithContent);
                _logger.LogInformation("    ├─ Records without Content: {RecordsWithoutContent}", recordsWithoutContent);
                _logger.LogInformation("    └─ Containers (Metadata Only): {Containers}", containersProcessed);
                _logger.LogInformation("  • Total Chunks Created: {TotalChunks}", totalChunks);
                _logger.LogInformation("  • Total Embeddings Generated: {TotalEmbeddings}", totalChunks);
                _logger.LogInformation("  • Average Chunks per Record: {AvgChunks:F2}", processedCount > 0 ? (double)totalChunks / processedCount : 0);
                _logger.LogInformation("⏱️ TIME TAKEN:");
                _logger.LogInformation("  • Start: {StartTime}", startTime.ToString("yyyy-MM-dd HH:mm:ss"));
                _logger.LogInformation("  • End: {EndTime}", endTime.ToString("yyyy-MM-dd HH:mm:ss"));
                _logger.LogInformation("  • Duration: {Duration}", duration.ToString(@"hh\:mm\:ss"));
                _logger.LogInformation("  • Avg Time per Record: {AvgTime:F2}s", processedCount > 0 ? duration.TotalSeconds / processedCount : 0);

                if (failedCount > 0)
                {
                    _logger.LogWarning("========================================");
                    _logger.LogWarning("⚠️ FAILED RECORDS SUMMARY");
                    _logger.LogWarning("========================================");
                    _logger.LogWarning("Total Failed: {FailedCount}", failedCount);
                    _logger.LogWarning("Check logs/failed-records-.txt for detailed failure information");
                    _logger.LogWarning("Failed Records:");
                    foreach (var failed in failedRecords)
                    {
                        _logger.LogWarning("  • URI: {Uri} | Title: {Title} | Error: {Error}",
                            failed.uri, failed.title, failed.error.Split('\n')[0]); // First line of error only
                    }
                }

                _logger.LogInformation("========================================");

                return processedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process all records");
                throw;
            }
        }

        /// <summary>
        /// Build comprehensive text representation of record including all metadata fields
        /// Downloads and extracts content from electronic documents
        /// Returns metadata text and document content separately for chunking
        /// </summary>
        private async Task<(string metadataText, string? documentContent)> BuildRecordTextComponentsAsync(RecordViewModel record)
        {
            var textBuilder = new StringBuilder();

            // Add core record information
            textBuilder.AppendLine($"Record Title: {record.Title}");
            textBuilder.AppendLine($"Record URI: {record.URI}");
            textBuilder.AppendLine($"Date Created: {record.DateCreated}");

            // Add multiple date format representations for better search matching
            // This helps match queries like "13/10/2025" (DD/MM/YYYY) and "10/13/2025" (MM/DD/YYYY)
            if (!string.IsNullOrEmpty(record.DateCreated))
            {
                var alternativeDateFormats = GetAlternativeDateFormats(record.DateCreated);
                if (!string.IsNullOrEmpty(alternativeDateFormats))
                {
                    textBuilder.AppendLine(alternativeDateFormats);
                }
            }

            // Add comprehensive temporal context for better date range queries
            var temporalContext = GetTemporalContext(record.DateCreated);
            if (!string.IsNullOrEmpty(temporalContext))
            {
                textBuilder.AppendLine(temporalContext);
            }

            textBuilder.AppendLine($"Record Type: {record.IsContainer}");

            if (!string.IsNullOrEmpty(record.Container))
                textBuilder.AppendLine($"Container: {record.Container}");

            if (!string.IsNullOrEmpty(record.Assignee))
                textBuilder.AppendLine($"Assignee: {record.Assignee}");

            if (!string.IsNullOrEmpty(record.AllParts))
                textBuilder.AppendLine($"All Parts: {record.AllParts}");

            if (!string.IsNullOrEmpty(record.ACL))
                textBuilder.AppendLine($"Access Control: {record.ACL}");

            var metadataText = textBuilder.ToString();
            string? documentContent = null;

            // Try to download and extract content for non-container records
            // This includes: Document Files (with content), Records without content (JPEG, etc.)
            // Containers will be embedded with metadata only
            if (record.IsContainer != "Container")
            {
                try
                {
                    _logger.LogInformation("📥 Attempting to download file content...");

                    // Try to download the document from Content Manager
                    var fileHandler = await _contentManagerServices.DownloadAsync((int)record.URI);

                    if (fileHandler != null && fileHandler.File != null && fileHandler.File.Length > 0)
                    {
                        _logger.LogInformation("✅ File downloaded successfully");
                        _logger.LogInformation("   • File Name: {FileName}", fileHandler.FileName);
                        _logger.LogInformation("   • File Size: {Size:N0} bytes ({SizeMB:F2} MB)",
                            fileHandler.File.Length, fileHandler.File.Length / 1024.0 / 1024.0);

                        // Save temporarily to extract content
                        var tempPath = Path.Combine(Path.GetTempPath(), fileHandler.FileName);
                        await File.WriteAllBytesAsync(tempPath, fileHandler.File);

                        try
                        {
                            // Determine content type from file extension
                            var extension = Path.GetExtension(fileHandler.FileName).ToLowerInvariant();
                            var contentType = extension switch
                            {
                                ".pdf" => "application/pdf",
                                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                                ".doc" => "application/msword",
                                ".txt" => "text/plain",
                                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                                ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                                _ => "application/octet-stream"
                            };

                            _logger.LogInformation("📖 Extracting text from file...");
                            _logger.LogInformation("   • File Extension: {Extension}", extension);
                            _logger.LogInformation("   • Content Type: {ContentType}", contentType);

                            // Extract text from the document
                            var extractedText = await _documentProcessor.ExtractTextAsync(tempPath, contentType);

                            if (!string.IsNullOrWhiteSpace(extractedText))
                            {
                                documentContent = extractedText;
                                _logger.LogInformation("✅ Text extraction successful");
                                _logger.LogInformation("   • Extracted Text Size: {Length:N0} characters", extractedText.Length);
                            }
                            else
                            {
                                _logger.LogWarning("⚠️ No text content extracted from file");
                                _logger.LogInformation("   • Will embed metadata only");
                            }
                        }
                        finally
                        {
                            // Clean up temporary file
                            if (File.Exists(tempPath))
                            {
                                File.Delete(tempPath);
                                _logger.LogDebug("🗑️ Deleted temporary file: {TempPath}", tempPath);
                            }
                        }
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ No downloadable content found");
                        _logger.LogInformation("   • Will embed metadata only");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("⚠️ Could not download/extract content: {Message}", ex.Message);
                    _logger.LogInformation("   • Continuing with metadata only");
                    // Continue processing with just metadata - this is expected for records without content
                }
            }
            else
            {
                _logger.LogInformation("📦 Container detected - Will embed metadata only");
            }

            return (metadataText, documentContent);
        }

        /// <summary>
        /// Build concise metadata header to prepend to each chunk
        /// This ensures ALL chunks (not just chunk 0) can match date-based queries
        /// </summary>
        private string BuildChunkMetadataHeader(RecordViewModel record)
        {
            var header = new StringBuilder();
            header.AppendLine($"[Record: {record.Title} | URI: {record.URI}]");
            header.AppendLine($"[Created: {record.DateCreated}]");

            // Add alternative date formats for better matching
            if (!string.IsNullOrEmpty(record.DateCreated))
            {
                var altFormats = GetAlternativeDateFormats(record.DateCreated);
                if (!string.IsNullOrEmpty(altFormats))
                {
                    header.AppendLine($"[{altFormats}]");
                }
            }

            return header.ToString().TrimEnd();
        }

        /// <summary>
        /// Build metadata dictionary for Qdrant storage
        /// Only includes core fields - no DefaultProperties (200+ fields)
        /// This enables filtering on core fields only
        /// </summary>
        private Dictionary<string, object> BuildRecordMetadata(RecordViewModel record)
        {
            var metadata = new Dictionary<string, object>
            {
                ["record_uri"] = record.URI,
                ["record_title"] = record.Title ?? "",
                ["date_created"] = record.DateCreated ?? "",
                ["record_type"] = record.IsContainer ?? "",
                ["container"] = record.Container ?? "",
                ["assignee"] = record.Assignee ?? "",
                ["all_parts"] = record.AllParts ?? "",
                ["acl"] = record.ACL ?? "",
                ["entity_type"] = "content_manager_record",
                ["indexed_at"] = DateTime.UtcNow.ToString("o")
            };

            // Extract and add file extension information for better filtering
            if (!string.IsNullOrEmpty(record.Title))
            {
                var extension = Path.GetExtension(record.Title).ToLowerInvariant();
                if (!string.IsNullOrEmpty(extension))
                {
                    metadata["file_extension"] = extension;
                    
                    // Add normalized file type for easier filtering
                    var fileType = extension.TrimStart('.');
                    metadata["file_type"] = fileType;

                    // Add document category for semantic grouping
                    var category = GetDocumentCategory(extension);
                    if (!string.IsNullOrEmpty(category))
                    {
                        metadata["document_category"] = category;
                    }
                }
            }

            return metadata;
        }

        /// <summary>
        /// Get document category based on file extension
        /// </summary>
        private string GetDocumentCategory(string extension)
        {
            return extension.ToLowerInvariant() switch
            {
                ".pdf" => "PDF Document",
                ".doc" or ".docx" => "Word Document",
                ".xls" or ".xlsx" => "Excel Document",
                ".ppt" or ".pptx" => "PowerPoint Document",
                ".txt" => "Text Document",
                ".rtf" => "Rich Text Document",
                ".csv" => "CSV Data",
                ".xml" => "XML Data",
                ".json" => "JSON Data",
                ".html" or ".htm" => "Web Document",
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".tiff" => "Image",
                ".mp3" or ".wav" or ".wma" or ".aac" => "Audio",
                ".mp4" or ".avi" or ".wmv" or ".mov" => "Video",
                ".zip" or ".rar" or ".7z" => "Archive",
                _ => "Other Document"
            };
        }

        /// <summary>
        /// Get alternative date format representations to support both US and European date formats
        /// Converts MM/DD/YYYY to DD/MM/YYYY and vice versa
        /// Example: "10/13/2025" becomes "Alternative Date Formats: 13/10/2025, 2025-10-13"
        /// </summary>
        private string GetAlternativeDateFormats(string dateCreatedString)
        {
            if (string.IsNullOrEmpty(dateCreatedString))
                return "";

            try
            {
                // Parse the date (assuming MM/DD/YYYY HH:mm:ss format from Content Manager)
                var dateFormats = new[] { "MM/dd/yyyy HH:mm:ss", "M/d/yyyy HH:mm:ss", "MM/dd/yyyy H:mm:ss" };
                DateTime parsedDate = DateTime.MinValue;
                bool parsed = false;

                foreach (var format in dateFormats)
                {
                    if (DateTime.TryParseExact(dateCreatedString, format, null, System.Globalization.DateTimeStyles.None, out parsedDate))
                    {
                        parsed = true;
                        break;
                    }
                }

                if (!parsed)
                {
                    // Try general parse as fallback
                    if (!DateTime.TryParse(dateCreatedString, out parsedDate))
                        return "";
                }

                // Generate multiple date format representations
                var formats = new List<string>();

                // DD/MM/YYYY format (European)
                formats.Add(parsedDate.ToString("dd/MM/yyyy"));

                // YYYY-MM-DD format (ISO)
                formats.Add(parsedDate.ToString("yyyy-MM-dd"));

                // With full month name
                formats.Add(parsedDate.ToString("MMMM dd, yyyy")); // e.g., "October 13, 2025"

                // With abbreviated month
                formats.Add(parsedDate.ToString("MMM dd, yyyy")); // e.g., "Oct 13, 2025"

                // DD Month YYYY format
                formats.Add(parsedDate.ToString("dd MMMM yyyy")); // e.g., "13 October 2025"

                return $"Alternative Date Formats: {string.Join(", ", formats)}";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// Get comprehensive temporal context for better date range queries
        /// Includes: time of day, month name, year, quarter, day of week
        /// This helps embeddings match queries like "between January 2025 and March 2025"
        /// </summary>
        private string GetTemporalContext(string dateCreatedString)
        {
            if (string.IsNullOrEmpty(dateCreatedString))
                return "";

            // Try to parse the date with time
            var dateFormats = new[] { "MM/dd/yyyy HH:mm:ss", "M/d/yyyy HH:mm:ss", "MM/dd/yyyy H:mm:ss",
                                     "yyyy-MM-dd HH:mm:ss", "dd/MM/yyyy HH:mm:ss" };

            DateTime parsedDate = DateTime.MinValue;
            bool parsed = false;

            foreach (var format in dateFormats)
            {
                if (DateTime.TryParseExact(dateCreatedString, format, null, System.Globalization.DateTimeStyles.None, out parsedDate))
                {
                    parsed = true;
                    break;
                }
            }

            // Fallback to general parse
            if (!parsed)
            {
                if (!DateTime.TryParse(dateCreatedString, out parsedDate))
                    return ""; // Cannot parse, skip temporal context
            }

            var contextBuilder = new StringBuilder();

            // 1. Time of day context
            var timeOfDay = GetTimeOfDayLabels(parsedDate);
            if (!string.IsNullOrEmpty(timeOfDay))
            {
                contextBuilder.AppendLine($"Time of Day: {timeOfDay}");
            }

            // 2. Month and year context
            var monthName = parsedDate.ToString("MMMM"); // Full month name (e.g., "January")
            var year = parsedDate.Year;
            contextBuilder.AppendLine($"Month: {monthName} {year}");

            // 3. Quarter context
            var quarter = (parsedDate.Month - 1) / 3 + 1;
            contextBuilder.AppendLine($"Quarter: Q{quarter} {year}");

            // 4. Day of week context
            var dayOfWeek = parsedDate.ToString("dddd"); // Full day name (e.g., "Monday")
            contextBuilder.AppendLine($"Day of Week: {dayOfWeek}");

            // 5. Year context (helps with "between year X and year Y" queries)
            contextBuilder.AppendLine($"Year: {year}");

            // 6. Week of year context
            var weekOfYear = System.Globalization.CultureInfo.CurrentCulture.Calendar
                .GetWeekOfYear(parsedDate, System.Globalization.CalendarWeekRule.FirstDay, DayOfWeek.Sunday);
            contextBuilder.AppendLine($"Week of Year: Week {weekOfYear} of {year}");

            return contextBuilder.ToString().TrimEnd();
        }

        /// <summary>
        /// Extract time-of-day labels from DateTime for semantic search
        /// Returns descriptive time periods like "morning", "noon", "afternoon", "evening", "night", "midnight"
        /// </summary>
        private string GetTimeOfDayLabels(DateTime parsedDate)
        {
            var hour = parsedDate.Hour;
            var minute = parsedDate.Minute;

            // Define time-of-day categories with semantic labels
            var timeContext = new List<string>();

            // Check for special times (within 15 minutes)
            if (hour == 12 && minute >= 0 && minute <= 15)
            {
                timeContext.Add("noon");
                timeContext.Add("midday");
            }
            else if (hour == 0 && minute >= 0 && minute <= 15)
            {
                timeContext.Add("midnight");
            }
            else if (hour == 23 && minute >= 45)
            {
                timeContext.Add("near midnight");
            }

            // Add general time period
            if (hour >= 5 && hour < 12)
            {
                timeContext.Add("morning");
                if (hour >= 5 && hour < 8)
                    timeContext.Add("early morning");
                else if (hour >= 10)
                    timeContext.Add("late morning");
            }
            else if (hour >= 12 && hour < 17)
            {
                timeContext.Add("afternoon");
                if (hour >= 12 && hour < 14)
                    timeContext.Add("early afternoon");
                else if (hour >= 15)
                    timeContext.Add("late afternoon");
            }
            else if (hour >= 17 && hour < 21)
            {
                timeContext.Add("evening");
                if (hour >= 17 && hour < 19)
                    timeContext.Add("early evening");
            }
            else // 21:00 - 04:59
            {
                timeContext.Add("night");
                if (hour >= 21 && hour < 24)
                    timeContext.Add("late night");
                else if (hour >= 0 && hour < 5)
                    timeContext.Add("early night");
            }

            return string.Join(", ", timeContext);
        }

        /// <summary>
        /// Delete all embeddings (chunks) for a specific record URI from PostgreSQL
        /// This removes both metadata and document content embeddings for the record
        /// OPTIMIZED: Uses efficient WHERE clause deletion in PostgreSQL
        /// </summary>
        /// <param name="recordUri">The Content Manager record URI to delete</param>
        /// <returns>Number of chunks deleted</returns>
        public async Task<int> DeleteRecordEmbeddingsAsync(long recordUri)
        {
            try
            {
                _logger.LogInformation("🗑️ Starting deletion of all embeddings for record URI: {RecordUri}", recordUri);

                var startTime = DateTime.Now;
                int deletedCount = 0;

                // Get count of embeddings to delete
                var embeddingIds = await _pgVectorService.GetPointIdsByRecordUriAsync(recordUri);

                if (!embeddingIds.Any())
                {
                    _logger.LogWarning("⚠️ No embeddings found for record URI: {RecordUri}", recordUri);
                    return 0;
                }

                _logger.LogInformation("📋 Found {Count} embeddings to delete for record URI: {RecordUri}",
                    embeddingIds.Count, recordUri);

                // Use efficient WHERE clause deletion
                var success = await _pgVectorService.DeleteEmbeddingsByRecordUriAsync(recordUri);

                if (success)
                {
                    deletedCount = embeddingIds.Count;
                    _logger.LogInformation("✅ Successfully deleted all embeddings using efficient WHERE clause deletion");
                }
                else
                {
                    _logger.LogWarning("⚠️ Deletion failed");
                }

                var endTime = DateTime.Now;
                var duration = endTime - startTime;

                _logger.LogInformation("========================================");
                _logger.LogInformation("✅ RECORD DELETION COMPLETE");
                _logger.LogInformation("========================================");
                _logger.LogInformation("📊 DELETION STATISTICS:");
                _logger.LogInformation("  • Record URI: {RecordUri}", recordUri);
                _logger.LogInformation("  • Embeddings Found: {Found}", embeddingIds.Count);
                _logger.LogInformation("  • Embeddings Deleted: {Deleted}", deletedCount);
                _logger.LogInformation("  • Failed Deletions: {Failed}", embeddingIds.Count - deletedCount);
                _logger.LogInformation("⏱️ TIME TAKEN:");
                _logger.LogInformation("  • Start: {StartTime}", startTime.ToString("yyyy-MM-dd HH:mm:ss"));
                _logger.LogInformation("  • End: {EndTime}", endTime.ToString("yyyy-MM-dd HH:mm:ss"));
                _logger.LogInformation("  • Duration: {Duration}ms", duration.TotalMilliseconds);
                _logger.LogInformation("========================================");

                return deletedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to delete embeddings for record URI: {RecordUri}", recordUri);
                throw;
            }
        }

        /// <summary>
        /// Delete all embeddings (chunks) for multiple record URIs from Vector DB (batch deletion)
        /// This removes both metadata and document content embeddings for all specified records
        /// </summary>
        /// <param name="recordUris">List of Content Manager record URIs to delete</param>
        /// <returns>Dictionary mapping each URI to the number of chunks deleted</returns>
        public async Task<Dictionary<long, int>> DeleteMultipleRecordEmbeddingsAsync(List<long> recordUris)
        {
            try
            {
                _logger.LogInformation("========================================");
                _logger.LogInformation("🗑️ STARTING BATCH DELETION PROCESS");
                _logger.LogInformation("========================================");
                _logger.LogInformation("Total Record URIs to delete: {Count}", recordUris?.Count ?? 0);
                _logger.LogInformation("Start Time: {StartTime}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                var startTime = DateTime.Now;
                var results = new Dictionary<long, int>();

                if (recordUris == null || !recordUris.Any())
                {
                    _logger.LogWarning("⚠️ No record URIs provided for batch deletion");
                    return results;
                }

                int totalDeleted = 0;
                int successCount = 0;
                int failureCount = 0;
                int notFoundCount = 0;

                _logger.LogInformation("");
                _logger.LogInformation("📝 Starting batch deletion loop...");
                _logger.LogInformation("");

                for (int i = 0; i < recordUris.Count; i++)
                {
                    var recordUri = recordUris[i];
                    var percentComplete = (double)(i + 1) / recordUris.Count * 100;

                    try
                    {
                        _logger.LogInformation("════════════════════════════════════════════════════════════════");
                        _logger.LogInformation("🗑️ DELETING RECORD [{Current}/{Total}] - {Percentage:F1}% Complete",
                            i + 1, recordUris.Count, percentComplete);
                        _logger.LogInformation("════════════════════════════════════════════════════════════════");
                        _logger.LogInformation("📋 Record URI: {RecordUri}", recordUri);

                        // Delete embeddings for this record
                        var deletedCount = await DeleteRecordEmbeddingsAsync(recordUri);

                        // Store the result
                        results[recordUri] = deletedCount;

                        if (deletedCount > 0)
                        {
                            successCount++;
                            totalDeleted += deletedCount;
                            _logger.LogInformation("✅ Successfully deleted {Count} chunks for record URI {RecordUri}",
                                deletedCount, recordUri);
                        }
                        else
                        {
                            notFoundCount++;
                            _logger.LogWarning("⚠️ No embeddings found for record URI {RecordUri}", recordUri);
                        }

                        _logger.LogInformation("");
                    }
                    catch (Exception ex)
                    {
                        failureCount++;
                        results[recordUri] = 0;
                        _logger.LogError(ex, "❌ FAILED to delete embeddings for record URI {RecordUri}", recordUri);
                        _logger.LogError("Error: {ErrorMessage}", ex.Message);
                        _logger.LogInformation("");
                    }
                }

                var endTime = DateTime.Now;
                var duration = endTime - startTime;

                _logger.LogInformation("========================================");
                _logger.LogInformation("✅ BATCH DELETION PROCESS COMPLETE");
                _logger.LogInformation("========================================");
                _logger.LogInformation("📊 SUMMARY STATISTICS:");
                _logger.LogInformation("  • Total Record URIs Requested: {Total}", recordUris.Count);
                _logger.LogInformation("  • Successfully Deleted: {Success}", successCount);
                _logger.LogInformation("  • Not Found (No Embeddings): {NotFound}", notFoundCount);
                _logger.LogInformation("  • Failed: {Failed}", failureCount);
                _logger.LogInformation("  • Total Chunks Deleted: {TotalDeleted}", totalDeleted);
                _logger.LogInformation("  • Average Chunks per Record: {AvgChunks:F2}",
                    successCount > 0 ? (double)totalDeleted / successCount : 0);
                _logger.LogInformation("⏱️ TIME TAKEN:");
                _logger.LogInformation("  • Start: {StartTime}", startTime.ToString("yyyy-MM-dd HH:mm:ss"));
                _logger.LogInformation("  • End: {EndTime}", endTime.ToString("yyyy-MM-dd HH:mm:ss"));
                _logger.LogInformation("  • Duration: {Duration}", duration.ToString(@"hh\:mm\:ss"));
                _logger.LogInformation("  • Avg Time per Record: {AvgTime:F2}s",
                    recordUris.Count > 0 ? duration.TotalSeconds / recordUris.Count : 0);
                _logger.LogInformation("========================================");

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to process batch deletion");
                throw;
            }
        }
    }
}
