using DocumentProcessingAPI.Core.Configuration;
using DocumentProcessingAPI.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.IO.Compression;
using TRIM.SDK;

namespace DocumentProcessingAPI.Core.DTOs
{
    public class ContentManagerServices : IDisposable
    {
        private readonly TrimSettings _trimSettings;
        private readonly ILogger<ContentManagerServices> _logger;
        private readonly IWindowsAuthenticationService _authService;
        private readonly AsyncLocal<Database> _currentDatabase;
        private Dictionary<string, PropertyOrFieldDef> recordProperties = new Dictionary<string, PropertyOrFieldDef>();
        private Dictionary<string, string> propertyInternalNames = new Dictionary<string, string>();
        private string _storedDatasetId;
        private string _storedWorkgroupUrl;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public ContentManagerServices(
            IOptions<TrimSettings> trimSettings,
            ILogger<ContentManagerServices> logger,
            IWindowsAuthenticationService authService)
        {
            _trimSettings = trimSettings.Value ?? throw new ArgumentNullException(nameof(trimSettings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _currentDatabase = new AsyncLocal<Database>();
        }

        public async Task<Database> GetDatabaseAsync()
        {
            if (_currentDatabase.Value != null)
            {
                return _currentDatabase.Value;
            }

            await _semaphore.WaitAsync();
            try
            {
                // Double-check pattern
                if (_currentDatabase.Value != null)
                {
                    return _currentDatabase.Value;
                }

                // Get current Windows user
                var currentUsername = _authService.GetCurrentUsername();

                _logger.LogInformation("🔐 [TRIM] Connecting to Content Manager as user: {Username}", currentUsername ?? "Anonymous");

                var database = new Database()
                {
                    Id = _trimSettings.DataSetId,
                    WorkgroupServerURL = _trimSettings.WorkgroupServerUrl
                };


                database.TrustedUser = currentUsername; 
                database.Connect();

                // Verify connection
                if (database.IsConnected)
                {
                    var connectedUser = database.CurrentUser?.Name ?? "Unknown";
                    _logger.LogInformation("✅ [TRIM] Connected successfully. Current TRIM user: {TrimUser}", connectedUser);

                    if (currentUsername != null && !connectedUser.Equals(currentUsername, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning("⚠️ [TRIM] Windows user ({WindowsUser}) differs from TRIM user ({TrimUser})",
                            currentUsername, connectedUser);
                    }
                }
                else
                {
                    _logger.LogError("❌ [TRIM] Database connection failed");
                    throw new Exception("Failed to connect to TRIM Content Manager");
                }

                _currentDatabase.Value = database;
                return database;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task ConnectDatabaseAsync()
        {
            _storedDatasetId = _trimSettings.DataSetId;
            _storedWorkgroupUrl = _trimSettings.WorkgroupServerUrl;
            await GetDatabaseAsync();
        }

        private async Task LoadPropertiesAsync()
        {
            var database = await GetDatabaseAsync();

            await _semaphore.WaitAsync();
            try
            {
                if (recordProperties.Count > 0)
                {
                    return; // Properties already loaded
                }

                var propList = PropertyOrFieldDef.GetAllPropertiesOrFields(database, BaseObjectTypes.Record);
                foreach (PropertyOrFieldDef item in propList)
                {
                    recordProperties.Add(item.Caption, item);
                    if (item.IsAProperty)
                    {
                        propertyInternalNames.Add(item.Property.Id.ToString(), item.Caption);
                    }
                    else if (item.IsAField)
                    {
                        propertyInternalNames.Add(item.Field.Name, item.Caption);
                    }
                }
                _logger.LogInformation("Loading Record Property Internal Names is completed");
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<List<RecordViewModel>> GetRecordsAsync(string searchString)
        {
            try
            {
                var database = await GetDatabaseAsync();
                var trimUser = database.CurrentUser?.Name ?? "Unknown";

                _logger.LogInformation("📋 [TRIM] Fetching records for user: {WindowsUser} (TRIM: {TrimUser}) with search: {SearchString}",
                     trimUser, searchString);

                long totalRecords = 0;
                bool useEstimatedCount = (searchString == "*" || string.IsNullOrWhiteSpace(searchString));

                // Ensure properties are loaded
                await LoadPropertiesAsync();

                // TRIM SDK will automatically filter results based on the current user's ACL permissions
                TrimMainObjectSearch search = new TrimMainObjectSearch(database, BaseObjectTypes.Record);

                if (useEstimatedCount)
                {
                    search.SetSearchString($"typedTitle:{searchString}");
                }
                else
                {
                    // For specific search queries (e.g., date filters), use the search string directly
                    search.SetSearchString(searchString);
                }

                search.SetSortString("DateCreated");

                var listOfRecords = new List<RecordViewModel>();

                totalRecords = search.Count;
                var containerRecordsInfoList = new List<ContainerRecordsInfo>();
                var processedContainerIds = new HashSet<long>();

                foreach (Record record in search)
                {
                    bool isContainer = !record.IsElectronic;

                    // Gather default properties for this record
                    var defaultProperties = new Dictionary<string, object>();
                    foreach (var prop in recordProperties)
                    {
                        try
                        {
                            object value = null;
                            if (prop.Value.IsAProperty)
                            {
                                value = record.GetProperty((PropertyIds)prop.Value.Property.Id);
                            }
                            else if (prop.Value.IsAField)
                            {
                                value = record.GetFieldValue(prop.Value.Field);
                            }

                            // Convert complex TRIM objects to serializable types
                            if (value == null)
                            {
                                defaultProperties[prop.Key] = null;
                            }
                            else if (value is bool || value is int || value is long || value is double || value is decimal)
                            {
                                // Keep primitive types as-is
                                defaultProperties[prop.Key] = value;
                            }
                            else if (value is DateTime dt)
                            {
                                // Convert DateTime to ISO string
                                defaultProperties[prop.Key] = dt.ToString("o");
                            }
                            else if (value is string str)
                            {
                                defaultProperties[prop.Key] = str;
                            }
                            else
                            {
                                // For complex TRIM objects, convert to string to avoid cycles
                                defaultProperties[prop.Key] = value.ToString();
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to get property {PropertyName} for record {RecordUri}", prop.Key, record.Uri.Value);
                            defaultProperties[prop.Key] = null;
                        }
                    }

                    // Format DateCreated with both date AND time
                    // Use ToDateTime() to convert TrimDateTime to standard DateTime, then format with time
                    var dateCreatedStr = record.DateCreated.ToDateTime().ToString("MM/dd/yyyy HH:mm:ss");

                    // Capture DateModified for change detection
                    var dateModifiedStr = record.DateModified.ToDateTime().ToString("MM/dd/yyyy HH:mm:ss");

                    var viewModel = new RecordViewModel
                    {
                        URI = record.Uri.Value,
                        Title = record.Title,
                        Container = record.Container?.Name ?? "",
                        AllParts = record.AllParts ?? "",
                        Assignee = record.Assignee?.Name ?? "",
                        DateCreated = dateCreatedStr,
                        DateModified = dateModifiedStr,
                        IsContainer = isContainer ? "Container" : "Document File",
                        ContainerCount = isContainer ? new Dictionary<string, long>() : null,
                        ACL = record.AccessControlList?.ToString() ?? "",
                        DefaultProperties = defaultProperties
                    };

                    listOfRecords.Add(viewModel);

                    if (isContainer && !processedContainerIds.Contains(record.Uri.Value))
                    {
                        processedContainerIds.Add(record.Uri.Value);
                    }
                }

                _logger.LogInformation("✅ [TRIM] Returned {RecordCount} records accessible to user {Username} (ACL-filtered by TRIM SDK)",
                    listOfRecords.Count);

                return listOfRecords;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [TRIM] Error in GetRecordsAsync for user {Username}", _authService.GetCurrentUsername());
                throw new Exception($"Failed to execute search: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Get records with pagination support and optional change detection
        /// Supports efficient incremental sync by filtering on DateModified
        /// </summary>
        /// <param name="searchString">Search string for TRIM query</param>
        /// <param name="pageNumber">Page number (0-based)</param>
        /// <param name="pageSize">Number of records per page</param>
        /// <param name="lastSyncDate">Optional: Only return records modified after this date</param>
        /// <returns>Paged result containing records and pagination metadata</returns>
        public async Task<PagedResult<RecordViewModel>> GetRecordsPaginatedAsync(
            string searchString,
            int pageNumber,
            int pageSize,
            DateTime? lastSyncDate = null)
        {
            try
            {
                var database = await GetDatabaseAsync();
                var currentUser = _authService.GetCurrentUsername();
                var trimUser = database.CurrentUser?.Name ?? "Unknown";

                _logger.LogInformation("📋 [TRIM] Fetching paginated records - Page: {PageNumber}, Size: {PageSize}, LastSync: {LastSyncDate}",
                    pageNumber, pageSize, lastSyncDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "None");

                // Ensure properties are loaded
                await LoadPropertiesAsync();

                // TRIM SDK will automatically filter results based on the current user's ACL permissions
                TrimMainObjectSearch search = new TrimMainObjectSearch(database, BaseObjectTypes.Record);

                // Build search string with change detection if provided
                string finalSearchString;
                if (lastSyncDate.HasValue)
                {
                    // Add DateModified filter for incremental sync
                    // TRIM uses modifiedOn for last modified date
                    var dateFilter = $"updated>={lastSyncDate.Value:MM/dd/yyyy}";

                    if (string.IsNullOrWhiteSpace(searchString) || searchString == "*")
                    {
                        finalSearchString = dateFilter;
                    }
                    else
                    {
                        finalSearchString = $"{searchString} and {dateFilter}";
                    }

                    _logger.LogInformation("   🔄 Incremental sync enabled - filtering records modified since {LastSyncDate}",
                        lastSyncDate.Value.ToString("yyyy-MM-dd HH:mm:ss"));
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(searchString) || searchString == "*")
                    {
                        finalSearchString = "typedTitle:*";
                    }
                    else
                    {
                        finalSearchString = $"registeredOn:{searchString}";
                    }
                }

                search.SetSearchString(finalSearchString);
                search.SetSortString("DateCreated");

                // Get total count
                long totalCount = search.Count;
                _logger.LogInformation("   📊 Total matching records: {TotalCount}", totalCount);

                // Apply pagination using LINQ Skip and Take
                int skipCount = pageNumber * pageSize;

                if (skipCount >= totalCount)
                {
                    // Page is beyond available records
                    _logger.LogInformation("   ℹ️ Page {PageNumber} is beyond available records", pageNumber);
                    return new PagedResult<RecordViewModel>
                    {
                        Items = new List<RecordViewModel>(),
                        TotalCount = totalCount,
                        PageNumber = pageNumber,
                        PageSize = pageSize
                    };
                }

                _logger.LogInformation("   📄 Fetching page {PageNumber}: skipping {Skip} records, taking {Take}",
                    pageNumber + 1, skipCount, pageSize);

                var listOfRecords = new List<RecordViewModel>();

                // Use LINQ to skip and take records
                var pagedRecords = search.Cast<Record>().Skip(skipCount).Take(pageSize);

                foreach (Record record in pagedRecords)
                {
                    bool isContainer = !record.IsElectronic;

                    // Gather default properties for this record
                    var defaultProperties = new Dictionary<string, object>();
                    foreach (var prop in recordProperties)
                    {
                        try
                        {
                            object value = null;
                            if (prop.Value.IsAProperty)
                            {
                                value = record.GetProperty((PropertyIds)prop.Value.Property.Id);
                            }
                            else if (prop.Value.IsAField)
                            {
                                value = record.GetFieldValue(prop.Value.Field);
                            }

                            // Convert complex TRIM objects to serializable types
                            if (value == null)
                            {
                                defaultProperties[prop.Key] = null;
                            }
                            else if (value is bool || value is int || value is long || value is double || value is decimal)
                            {
                                defaultProperties[prop.Key] = value;
                            }
                            else if (value is DateTime dt)
                            {
                                defaultProperties[prop.Key] = dt.ToString("o");
                            }
                            else if (value is string str)
                            {
                                defaultProperties[prop.Key] = str;
                            }
                            else
                            {
                                defaultProperties[prop.Key] = value.ToString();
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to get property {PropertyName} for record {RecordUri}", prop.Key, record.Uri.Value);
                            defaultProperties[prop.Key] = null;
                        }
                    }

                    // Format DateCreated with both date AND time
                    var dateCreatedStr = record.DateCreated.ToDateTime().ToString("MM/dd/yyyy HH:mm:ss");

                    // Capture DateModified for change detection
                    var dateModifiedStr = record.DateModified.ToDateTime().ToString("MM/dd/yyyy HH:mm:ss");

                    var viewModel = new RecordViewModel
                    {
                        URI = record.Uri.Value,
                        Title = record.Title,
                        Container = record.Container?.Name ?? "",
                        AllParts = record.AllParts ?? "",
                        Assignee = record.Assignee?.Name ?? "",
                        DateCreated = dateCreatedStr,
                        DateModified = dateModifiedStr,
                        IsContainer = isContainer ? "Container" : "Document File",
                        ContainerCount = isContainer ? new Dictionary<string, long>() : null,
                        ACL = record.AccessControlList?.ToString() ?? "",
                        DefaultProperties = defaultProperties
                    };

                    listOfRecords.Add(viewModel);
                }

                _logger.LogInformation("✅ [TRIM] Returned page {PageNumber} with {RecordCount} records (Total: {TotalCount})",
                    pageNumber, listOfRecords.Count, totalCount);

                return new PagedResult<RecordViewModel>
                {
                    Items = listOfRecords,
                    TotalCount = totalCount,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [TRIM] Error in GetRecordsPaginatedAsync");
                throw new Exception($"Failed to execute paginated search: {ex.Message}", ex);
            }
        }
        public async Task<FileHandaler> DownloadAsync(long id)
        {
            try
            {
                var filehandeler = new FileHandaler();

                // Create a fresh database connection for this operation
                var database = await GetDatabaseAsync();

                Record record = new Record(database, id);
                if (!record.IsElectronic)
                {
                    Console.WriteLine($"Record with ID {id} does not have an electronic document.");
                    return null;
                }
                string extention = record.Extension;
                string outputPath = @"C:\Temp\Download\" + record.Title + "." + record.Extension;

                string fileName = record.Title + "." + extention;

                string savedPath = record.GetDocument(
                    outputPath,
                    false,
                    "Downloaded via app",
                    outputPath
                );

                var bytesdata = System.IO.File.ReadAllBytes(savedPath);
                filehandeler.File = bytesdata;
                filehandeler.FileName = fileName;
                filehandeler.LocalDownloadPath = outputPath;

                Console.WriteLine($"Successfully downloaded file: {fileName} ({bytesdata.Length} bytes)");
                return filehandeler;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in Download method for ID {id}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Download multiple records and package them as a ZIP file
        /// </summary>
        public async Task<FileHandaler> DownloadMultipleAsZipAsync(List<long> recordUris)
        {
            try
            {
                _logger.LogInformation("Starting bulk download for {Count} records", recordUris.Count);

                var database = await GetDatabaseAsync();
                var tempFolder = Path.Combine(@"C:\Temp\Download", $"Bulk_{DateTime.Now:yyyyMMddHHmmss}");
                Directory.CreateDirectory(tempFolder);

                var downloadedFiles = new List<string>();
                int successCount = 0;
                int failCount = 0;

                foreach (var uri in recordUris)
                {
                    try
                    {
                        Record record = new Record(database, uri);

                        if (!record.IsElectronic)
                        {
                            _logger.LogWarning("Record {Uri} is not electronic, skipping", uri);
                            failCount++;
                            continue;
                        }

                        string fileName = $"{record.Title}.{record.Extension}";
                        // Sanitize filename
                        fileName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));

                        string outputPath = Path.Combine(tempFolder, fileName);

                        // Handle duplicate filenames
                        int counter = 1;
                        while (File.Exists(outputPath))
                        {
                            fileName = $"{record.Title}_{counter}.{record.Extension}";
                            fileName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));
                            outputPath = Path.Combine(tempFolder, fileName);
                            counter++;
                        }

                        string savedPath = record.GetDocument(
                            outputPath,
                            false,
                            "Downloaded via bulk download",
                            outputPath
                        );

                        downloadedFiles.Add(savedPath);
                        successCount++;
                        _logger.LogInformation("Downloaded record {Uri}: {FileName}", uri, fileName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to download record {Uri}", uri);
                        failCount++;
                    }
                }

                if (downloadedFiles.Count == 0)
                {
                    _logger.LogWarning("No files were downloaded successfully");
                    Directory.Delete(tempFolder, true);
                    return null;
                }

                // Create ZIP file
                string zipFileName = $"Records_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
                string zipPath = Path.Combine(@"C:\Temp\Download", zipFileName);

                using (var zipArchive = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
                {
                    foreach (var filePath in downloadedFiles)
                    {
                        zipArchive.CreateEntryFromFile(filePath, Path.GetFileName(filePath));
                    }
                }

                // Clean up temp folder
                Directory.Delete(tempFolder, true);

                // Read ZIP file bytes
                var zipBytes = File.ReadAllBytes(zipPath);

                var fileHandler = new FileHandaler
                {
                    File = zipBytes,
                    FileName = zipFileName,
                    LocalDownloadPath = zipPath
                };

                _logger.LogInformation("Bulk download completed: {SuccessCount} succeeded, {FailCount} failed. ZIP size: {Size} bytes",
                    successCount, failCount, zipBytes.Length);

                return fileHandler;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in DownloadMultipleAsZipAsync");
                throw;
            }
        }

        #region Record Search Methods

        /// <summary>
        /// Execute Content Manager IDOL index search with separate parameters
        /// Returns candidate record URIs and record details
        /// </summary>
        public async Task<ContentManagerSearchResultDto> ExecuteContentManagerSearchAsync(
            string contentQuery,
            DateTime? startDate,
            DateTime? endDate,
            List<string> fileTypeFilters,
            List<string> keywordPhrases = null,
            string? uriFilter = null)
        {
            try
            {
                _logger.LogInformation("   🔍 Executing CM IDOL Index Search");
                _logger.LogInformation("      Content: {Content}", contentQuery ?? "none");
                _logger.LogInformation("      Date Range: {Start} to {End}",
                    startDate?.ToString("MM/dd/yyyy") ?? "none",
                    endDate?.ToString("MM/dd/yyyy") ?? "none");

                // Get database connection
                var database = await GetDatabaseAsync();

                if (database == null)
                {
                    _logger.LogError("   ❌ Database connection is not available");
                    throw new Exception("Database connection is not available");
                }

                var candidateUris = new HashSet<long>();
                var recordDetails = new List<string>();

                // Create TrimMainObjectSearch for IDOL index search
                var search = new TrimMainObjectSearch(database, BaseObjectTypes.Record);

                // IMPORTANT: SetSearchString can only accept ONE field at a time
                // Priority: URI > Date > Content > FileType

                // Step 0: If we have a URI filter, use ONLY URI filter (highest priority)
                if (!string.IsNullOrWhiteSpace(uriFilter))
                {
                    var uriStr = $"uri:{uriFilter}";
                    search.SetSearchString(uriStr);
                    _logger.LogInformation("   📋 Using URI filter: {UriFilter}", uriStr);
                }
                // Step 1: If we have a date, use ONLY date filter
                else if (startDate.HasValue || endDate.HasValue)
                {
                    if (startDate.HasValue && endDate.HasValue && startDate.Value.Date == endDate.Value.Date)
                    {
                        // Single date (same start and end)
                        var dateStr = $"createdOn:{startDate.Value:MM/dd/yyyy}";
                        search.SetSearchString(dateStr);
                        _logger.LogInformation("   📋 Using single date filter: {DateFilter}", dateStr);
                    }
                    else if (startDate.HasValue && endDate.HasValue)
                    {
                        // Date range (different start and end)
                        var dateStr = $"createdOn:{startDate.Value:MM/dd/yyyy} to {endDate.Value:MM/dd/yyyy}";
                        search.SetSearchString(dateStr);
                        _logger.LogInformation("   📋 Using date range filter: {DateFilter}", dateStr);
                    }
                    else if (startDate.HasValue)
                    {
                        // Only start date
                        var dateStr = $"createdOn:{startDate.Value:MM/dd/yyyy}";
                        search.SetSearchString(dateStr);
                        _logger.LogInformation("   📋 Using start date filter: {DateFilter}", dateStr);
                    }
                    else if (endDate.HasValue)
                    {
                        // Only end date
                        var dateStr = $"createdOn:{endDate.Value:MM/dd/yyyy}";
                        search.SetSearchString(dateStr);
                        _logger.LogInformation("   📋 Using end date filter: {DateFilter}", dateStr);
                    }
                }
                // Step 2: If no date but we have content, search for each keyword/phrase separately
                else if (!string.IsNullOrWhiteSpace(contentQuery))
                {
                    // Use keyword phrases from Gemini if available (preserves multi-word phrases like "17 years")
                    // Otherwise split content query into individual keywords
                    var keywords = (keywordPhrases != null && keywordPhrases.Any())
                        ? keywordPhrases.Where(k => !string.IsNullOrWhiteSpace(k)).ToList()
                        : contentQuery.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                                      .Where(k => k.Length > 2) // Skip very short words
                                      .ToList();

                    if (keywords.Count == 1)
                    {
                        // Single keyword/phrase - simple search
                        var contentStr = $"content:\"{keywords[0]}\"";
                        search.SetSearchString(contentStr);
                        _logger.LogInformation("   📋 Using content filter: {ContentFilter}", contentStr);
                    }
                    else if (keywords.Count > 1)
                    {
                        // Multiple keywords/phrases - combine with OR to find documents containing ANY keyword
                        var orConditions = keywords.Select(k => $"content:\"{k}\"").ToList();
                        var combinedQuery = string.Join(" or ", orConditions);
                        search.SetSearchString(combinedQuery);
                        _logger.LogInformation("   📋 Using multi-keyword OR filter: {ContentFilter}", combinedQuery);
                        _logger.LogInformation("   📋 Searching for documents containing ANY of: {Keywords}", string.Join(", ", keywords));
                    }
                    else
                    {
                        // No valid keywords after filtering
                        _logger.LogInformation("   ℹ️ No valid keywords found in content query");
                        search.SetSearchString("number:*");
                    }
                }
                // Step 3: If no date or content but have file type, use file type filter
                else if (fileTypeFilters != null && fileTypeFilters.Any())
                {
                    var extensionStr = $"extension:{fileTypeFilters.First()}";
                    search.SetSearchString(extensionStr);
                    _logger.LogInformation("   📋 Using extension filter: {ExtensionFilter}", extensionStr);
                }
                else
                {
                    // No filters, use wildcard
                    search.SetSearchString("number:*");
                    _logger.LogInformation("   📋 Using wildcard filter: number:*");
                }

                _logger.LogInformation("   📊 CM Index Search initiated. Estimated count: {Count}", search.Count);

                if (search.Count == 0)
                {
                    _logger.LogInformation("   ℹ️ CM IDOL Index Search returned 0 results");
                    return new ContentManagerSearchResultDto
                    {
                        CandidateRecordUris = candidateUris,
                        RecordDetails = recordDetails
                    };
                }

                // Iterate through search results and collect records with details
                var recordCount = 0;
                foreach (Record record in search)
                {
                    try
                    {
                        var uri = record.Uri.Value;
                        candidateUris.Add(uri);

                        // Add only URI for lightweight query enhancement
                        // During embedding, chunks have record_uri metadata, so this provides a hint
                        recordDetails.Add($"URI:{uri}");

                        recordCount++;

                        // Log progress for large result sets
                        if (recordCount % 100 == 0)
                        {
                            _logger.LogDebug("   📊 Processed {Count} records so far...", recordCount);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "   ⚠️ Failed to process record: {Error}", ex.Message);
                        // If fetch fails, just add the URI
                        if (candidateUris.Contains(record.Uri.Value))
                        {
                            recordDetails.Add($"URI:{record.Uri.Value}");
                        }
                    }
                }

                _logger.LogInformation("   ✅ CM IDOL Index Search completed: Found {Count} unique record URIs with details",
                    candidateUris.Count);

                return new ContentManagerSearchResultDto
                {
                    CandidateRecordUris = candidateUris,
                    RecordDetails = recordDetails
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "   ❌ Error executing CM IDOL Index search: {Message}", ex.Message);
                throw; // Re-throw to let calling code handle fallback
            }
        }

        /// <summary>
        /// Execute advanced filter search using only metadata filters
        /// Returns complete RecordSearchResponseDto with records
        /// </summary>
        public async Task<RecordSearchResponseDto> ExecuteContentManagerAdvanceFilterAsync(
            string? uri = null,
            string? clientId = null,
            string? title = null,
            DateTime? dateFrom = null,
            DateTime? dateTo = null,
            string? contentSearch = null,
            int topK = 20)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            _logger.LogInformation("========== ADVANCED FILTER SEARCH ==========");

            try
            {
                // Get database connection
                var database = await GetDatabaseAsync();
                if (database == null)
                {
                    _logger.LogError("Database connection is not available");
                    throw new Exception("Database connection is not available");
                }

                // Create TrimMainObjectSearch
                var search = new TrimMainObjectSearch(database, BaseObjectTypes.Record);

                // Priority-based filter selection using switch-case pattern
                // Determine which filter to use based on priority: URI > ClientId > Title > Date > Content
                if (!string.IsNullOrWhiteSpace(uri))
                {
                    // URI filter (highest priority)
                    var uriStr = $"uri:{uri}";
                    search.SetSearchString(uriStr);
                    _logger.LogInformation("   📋 Using URI filter: {UriFilter}", uriStr);
                }
                else if (!string.IsNullOrWhiteSpace(clientId))
                {
                    // Client ID filter
                    var clientIdStr = $"recContainer:{clientId}";
                    search.SetSearchString(clientIdStr);
                    _logger.LogInformation("   📋 Using Client ID filter: {ClientIdFilter}", clientIdStr);
                }
                else if (!string.IsNullOrWhiteSpace(title))
                {
                    // Title filter
                    var titleStr = $"typedTitle:{title}";
                    search.SetSearchString(titleStr);
                    _logger.LogInformation("   📋 Using Title filter: {TitleFilter}", titleStr);
                }
                else if (dateFrom.HasValue || dateTo.HasValue)
                {
                    // Date filter
                    string dateStr;
                    if (dateFrom.HasValue && dateTo.HasValue && dateFrom.Value.Date == dateTo.Value.Date)
                    {
                        // Single date
                        dateStr = $"createdOn:{dateFrom.Value:MM/dd/yyyy}";
                    }
                    else if (dateFrom.HasValue && dateTo.HasValue)
                    {
                        // Date range
                        dateStr = $"createdOn:{dateFrom.Value:MM/dd/yyyy} to {dateTo.Value:MM/dd/yyyy}";
                    }
                    else if (dateFrom.HasValue)
                    {
                        // Only start date
                        dateStr = $"createdOn:{dateFrom.Value:MM/dd/yyyy}";
                    }
                    else
                    {
                        // Only end date
                        dateStr = $"createdOn:{dateTo.Value:MM/dd/yyyy}";
                    }
                    search.SetSearchString(dateStr);
                    _logger.LogInformation("   📋 Using Date filter: {DateFilter}", dateStr);
                }
                else if (!string.IsNullOrWhiteSpace(contentSearch))
                {
                    // Content search filter
                    var contentStr = $"content:\"{contentSearch}\"";
                    search.SetSearchString(contentStr);
                    _logger.LogInformation("   📋 Using Content filter: {ContentFilter}", contentStr);
                }
                else
                {
                    // No filters provided - return empty result
                    _logger.LogWarning("No advanced filters provided");
                    return new RecordSearchResponseDto
                    {
                        Query = "[Advanced Filter Search]",
                        Results = new List<RecordSearchResultDto>(),
                        TotalResults = 0,
                        QueryTime = 0f,
                        SynthesizedAnswer = "No advanced filters provided. Please specify at least one filter."
                    };
                }

                _logger.LogInformation("   📊 Advanced Filter Search initiated. Estimated count: {Count}", search.Count);

                if (search.Count == 0)
                {
                    _logger.LogInformation("   ℹ️ Advanced Filter Search returned 0 results");
                    stopwatch.Stop();
                    return new RecordSearchResponseDto
                    {
                        Query = "[Advanced Filter Search]",
                        Results = new List<RecordSearchResultDto>(),
                        TotalResults = 0,
                        QueryTime = (float)stopwatch.Elapsed.TotalSeconds,
                        SynthesizedAnswer = "No records found matching the advanced filter criteria."
                    };
                }

                // Collect results
                var results = new List<RecordSearchResultDto>();
                var recordCount = 0;

                foreach (Record record in search)
                {
                    try
                    {
                        // Get ACL information
                        string? aclJson = null;
                        try
                        {
                            var acl = record.AccessControlList;
                            if (acl != null)
                            {
                                // Build ACL JSON with permissions for UI
                                // Function IDs: 1=ViewDocument, 2=ViewMetadata, 3=UpdateDocument, 4=UpdateMetadata,
                                //               5=ModifyAccess, 6=DestroyRecord, 7=ContributeContents
                                var aclData = new
                                {
                                    Permissions = new Dictionary<string, string>
                                    {
                                        { "ViewDocument", acl.get_AsString(1) ?? "Unknown" },
                                        { "ViewMetadata", acl.get_AsString(2) ?? "Unknown" },
                                        { "UpdateDocument", acl.get_AsString(3) ?? "Unknown" },
                                        { "UpdateMetadata", acl.get_AsString(4) ?? "Unknown" },
                                        { "ModifyAccess", acl.get_AsString(5) ?? "Unknown" },
                                        { "DestroyRecord", acl.get_AsString(6) ?? "Unknown" },
                                        { "ContributeContents", acl.get_AsString(7) ?? "Unknown" }
                                    }
                                };
                                aclJson = System.Text.Json.JsonSerializer.Serialize(aclData);
                            }
                        }
                        catch (Exception aclEx)
                        {
                            _logger.LogWarning(aclEx, "Failed to retrieve ACL for record {Uri}", record.Uri.Value);
                        }

                        var result = new RecordSearchResultDto
                        {
                            RecordUri = record.Uri.Value,
                            RecordTitle = record.Title ?? "Untitled",
                            RecordType = record.RecordType?.Name ?? "Unknown",
                            DateCreated = record.DateCreated.ToDateTime().ToString("yyyy-MM-dd HH:mm:ss"),
                            RelevanceScore = 1.0f, // Advanced filter matches are exact matches
                            ACL = aclJson
                        };

                        results.Add(result);
                        recordCount++;

                        // Limit results to topK
                        if (recordCount >= topK)
                        {
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "   ⚠️ Failed to process record: {Error}", ex.Message);
                    }
                }

                stopwatch.Stop();
                _logger.LogInformation("   ✅ Advanced Filter Search completed: Found {Count} records in {Time}s",
                    results.Count, stopwatch.Elapsed.TotalSeconds);

                return new RecordSearchResponseDto
                {
                    Query = "[Advanced Filter Search]",
                    Results = results,
                    TotalResults = results.Count,
                    QueryTime = (float)stopwatch.Elapsed.TotalSeconds,
                    SynthesizedAnswer = $"Found {results.Count} record(s) matching the advanced filter criteria."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "   ❌ Error executing Advanced Filter search: {Message}", ex.Message);
                stopwatch.Stop();
                return new RecordSearchResponseDto
                {
                    Query = "[Advanced Filter Search]",
                    Results = new List<RecordSearchResultDto>(),
                    TotalResults = 0,
                    QueryTime = (float)stopwatch.Elapsed.TotalSeconds,
                    SynthesizedAnswer = $"Error executing search: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Build Content Manager IDOL search string with proper syntax
        /// Supports content, date ranges, and file type filtering
        /// </summary>
        public string BuildContentManagerSearchString(
            string query,
            DateTime? startDate,
            DateTime? endDate,
            List<string> fileTypeFilters)
        {
            var searchParts = new List<string>();

            // 1. Add content search using the clean query directly
            // MUST use "content:" prefix (not "text:") as per CM requirements
            if (!string.IsNullOrWhiteSpace(query))
            {
                // Remove common query words for better CM index matching
                var cleanedQuery = RemoveCommonQueryWords(query);
                if (!string.IsNullOrWhiteSpace(cleanedQuery))
                {
                    // Search in content - IMPORTANT: use "content:" not "text:"
                    searchParts.Add($"content:{cleanedQuery}");
                }
            }

            // 2. Add date filter if provided
            // IMPORTANT: Content Manager only supports exact date matching with createdOn:MM/dd/yyyy
            // NO support for ranges (>=, <=) - must use exact dates with "or" operator
            if (startDate.HasValue && endDate.HasValue)
            {
                // If same date, use single filter
                if (startDate.Value.Date == endDate.Value.Date)
                {
                    searchParts.Add($"createdOn:{startDate.Value:MM/dd/yyyy}");
                }
                else
                {
                    // For date range, list all dates between start and end with "or"
                    // This is the only way CM supports date ranges
                    var dates = new List<string>();
                    var currentDate = startDate.Value.Date;

                    // Limit to reasonable range to avoid huge queries
                    var daysDiff = (endDate.Value.Date - startDate.Value.Date).Days;
                    if (daysDiff <= 31) // Only process if range is 31 days or less
                    {
                        while (currentDate <= endDate.Value.Date)
                        {
                            dates.Add($"createdOn:{currentDate:MM/dd/yyyy}");
                            currentDate = currentDate.AddDays(1);
                        }

                        if (dates.Count > 1)
                        {
                            searchParts.Add($"({string.Join(" or ", dates)})");
                        }
                        else if (dates.Count == 1)
                        {
                            searchParts.Add(dates[0]);
                        }
                    }
                    else
                    {
                        // For ranges > 31 days, just use start date with warning
                        _logger.LogWarning("Date range too large ({Days} days). Using start date only: {StartDate}",
                            daysDiff, startDate.Value.ToString("MM/dd/yyyy"));
                        searchParts.Add($"createdOn:{startDate.Value:MM/dd/yyyy}");
                    }
                }
            }
            else if (startDate.HasValue)
            {
                // Only start date - use exact date (no range support)
                searchParts.Add($"createdOn:{startDate.Value:MM/dd/yyyy}");
            }
            else if (endDate.HasValue)
            {
                // Only end date - use exact date (no range support)
                searchParts.Add($"createdOn:{endDate.Value:MM/dd/yyyy}");
            }

            // 3. Add file type filters if provided
            if (fileTypeFilters.Any())
            {
                var extensionTerms = fileTypeFilters.Select(ft => $"extension:{ft}").ToList();
                if (extensionTerms.Count > 1)
                {
                    searchParts.Add($"({string.Join(" or ", extensionTerms)})");
                }
                else
                {
                    searchParts.Add(extensionTerms.First());
                }
            }

            // If no search parts at all, return wildcard search
            if (!searchParts.Any())
            {
                return "number:*"; // Wildcard to get all records (as per reference pattern)
            }

            // Combine all parts with "and" operator (lowercase as per CM IDOL syntax)
            return string.Join(" and ", searchParts);
        }

        /// <summary>
        /// Remove common query words that don't add value to CM index search
        /// </summary>
        private string RemoveCommonQueryWords(string query)
        {
            var stopWords = new HashSet<string>
            {
                "show", "me", "get", "find", "search", "the", "a", "an", "from", "to", "between", "and", "or",
                "records", "record", "documents", "document", "files", "file", "created", "made", "added", "uploaded",
                "on", "at", "in", "which", "is", "that", "these", "those", "with", "for", "all", "any"
            };

            var words = query.ToLowerInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => !stopWords.Contains(w))
                .ToList();

            return string.Join(" ", words);
        }

        #endregion

        public void Dispose()
        {
            if (_currentDatabase.Value != null)
            {
                _currentDatabase.Value.Dispose();
                _currentDatabase.Value = null;
            }
            _semaphore.Dispose();
        }
    }
}
