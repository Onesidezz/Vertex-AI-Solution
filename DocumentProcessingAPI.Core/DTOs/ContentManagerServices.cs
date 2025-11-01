using DocumentProcessingAPI.Core.Configuration;
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
        private readonly AsyncLocal<Database> _currentDatabase;
        private Dictionary<string, PropertyOrFieldDef> recordProperties = new Dictionary<string, PropertyOrFieldDef>();
        private Dictionary<string, string> propertyInternalNames = new Dictionary<string, string>();
        private string _storedDatasetId;
        private string _storedWorkgroupUrl;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public ContentManagerServices(IOptions<TrimSettings> trimSettings, ILogger<ContentManagerServices> logger)
        {
            _trimSettings = trimSettings.Value ?? throw new ArgumentNullException(nameof(trimSettings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

                var database = new Database()
                {
                    Id = _trimSettings.DataSetId,
                    WorkgroupServerURL = _trimSettings.WorkgroupServerUrl
                };
                database.Connect();
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
                long totalRecords = 0;
                bool useEstimatedCount = (searchString == "*" || string.IsNullOrWhiteSpace(searchString));

                // Ensure properties are loaded
                await LoadPropertiesAsync();

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

                    var viewModel = new RecordViewModel
                    {
                        URI = record.Uri.Value,
                        Title = record.Title,
                        Container = record.Container?.Name ?? "",
                        AllParts = record.AllParts ?? "",
                        Assignee = record.Assignee?.Name ?? "",
                        DateCreated = dateCreatedStr,
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

                return listOfRecords;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetRecordsAsync");
                throw new Exception($"Failed to execute search: {ex.Message}", ex);
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
