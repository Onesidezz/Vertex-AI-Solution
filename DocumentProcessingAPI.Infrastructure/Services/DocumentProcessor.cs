using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using DocumentProcessingAPI.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net.Http;

namespace DocumentProcessingAPI.Infrastructure.Services;

/// <summary>
/// Document processor service based on the provided FileTextExtractor
/// </summary>
public class DocumentProcessor : IDocumentProcessor
{
    private readonly ILogger<DocumentProcessor> _logger;
    private readonly IConfiguration _configuration;
    private static readonly HashSet<string> SupportedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text/plain",
        "application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document", // .docx
        "application/msword", // .doc
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", // .xlsx
        "application/vnd.openxmlformats-officedocument.presentationml.presentation", // .pptx
        "text/csv",
        "image/png"

    };

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".pdf", ".docx", ".xlsx", ".pptx", ".csv",".doc"
    };

    private const long MaxFileSize = 500L * 1024 * 1024; // 500MB
    private const long WarningFileSize = 50L * 1024 * 1024; // 50MB

    public DocumentProcessor(ILogger<DocumentProcessor> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<string> ExtractTextAsync(string filePath, string contentType)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            var fileSizeMB = fileInfo.Length / 1024 / 1024;

            if (fileInfo.Length > MaxFileSize)
            {
                return $"[FILE TOO LARGE] File size ({fileSizeMB}MB) exceeds maximum limit ({MaxFileSize / 1024 / 1024}MB). Please contact administrator for processing large files.";
            }

            string extension = Path.GetExtension(filePath).ToLower();
            string content;

            content = extension switch
            {
                ".txt" or ".csv" => await ExtractTextFromTxtStreamingAsync(filePath),
                ".pdf" => await ExtractTextFromPdfAsync(filePath),
                ".docx" => await ExtractTextFromDocxAsync(filePath),
                ".doc" => await ExtractTextFromDocAsync(filePath),
                ".xlsx" => await ExtractTextFromXlsxAsync(filePath),
                ".pptx" => await ExtractTextFromPptxAsync(filePath),
                _ => throw new NotSupportedException($"File type '{extension}' is not supported."),
            };

            if (fileInfo.Length > WarningFileSize)
            {
                content = $"[LARGE FILE WARNING] File size: {fileSizeMB}MB. Processing may take longer.\n\n" + content;
            }

            return content;
        }
        catch (OutOfMemoryException)
        {
            _logger.LogError("Out of memory error processing file: {FilePath}", filePath);
            var fileSizeMB = new FileInfo(filePath).Length / 1024 / 1024;
            return $"[MEMORY ERROR] File is too large to process in memory ({fileSizeMB}MB). Please use a smaller file or contact administrator.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract text from file: {FilePath}", filePath);
            return $"[PROCESSING ERROR] Failed to extract text from file: {ex.Message}";
        }
    }

    public bool IsFileTypeSupported(string contentType)
    {
        return SupportedMimeTypes.Contains(contentType);
    }

    public IEnumerable<string> GetSupportedExtensions()
    {
        return SupportedExtensions;
    }

    public async Task<(bool IsValid, string? ErrorMessage)> ValidateFileAsync(string filePath, string contentType, long fileSize)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return (false, "File does not exist");
            }

            if (fileSize > MaxFileSize)
            {
                return (false, $"File size ({fileSize / 1024 / 1024}MB) exceeds maximum limit ({MaxFileSize / 1024 / 1024}MB)");
            }

            if (fileSize == 0)
            {
                return (false, "File is empty");
            }

            var extension = Path.GetExtension(filePath);
            if (!SupportedExtensions.Contains(extension))
            {
                return (false, $"File type '{extension}' is not supported");
            }

            if (!IsFileTypeSupported(contentType))
            {
                return (false, $"Content type '{contentType}' is not supported");
            }

            // Additional file-specific validation
            return extension.ToLower() switch
            {
                ".pdf" => await ValidatePdfFileAsync(filePath),
                ".docx" => await ValidateDocxFileAsync(filePath),
                ".xlsx" => await ValidateXlsxFileAsync(filePath),
                ".pptx" => await ValidatePptxFileAsync(filePath),
                _ => (true, null)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating file: {FilePath}", filePath);
            return (false, $"Validation error: {ex.Message}");
        }
    }

    private async Task<string> ExtractTextFromTxtStreamingAsync(string filePath)
    {
        const int bufferSize = 8192;
        var content = new StringBuilder();

        try
        {
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize);
            using var reader = new StreamReader(fileStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize);

            var buffer = new char[bufferSize];
            int charsRead;

            while ((charsRead = await reader.ReadAsync(buffer, 0, bufferSize)) > 0)
            {
                content.Append(buffer, 0, charsRead);

                if (content.Length % (bufferSize * 100) == 0)
                {
                    GC.Collect(0, GCCollectionMode.Optimized);
                }
            }

            var result = content.ToString();

            // Apply universal text cleaning for better quality
            result = await ApplyUniversalTextCleaning(result);

            var linesProcessed = result.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            _logger.LogInformation("Text file extraction completed. Extracted {TextLength} characters from {LinesProcessed} lines",
                result.Length, linesProcessed);

            return result;
        }
        catch (UnauthorizedAccessException)
        {
            return "[ACCESS ERROR] Unable to read file. Check file permissions.";
        }
        catch (FileNotFoundException)
        {
            return "[FILE ERROR] File not found.";
        }
        catch (IOException ex)
        {
            return $"[IO ERROR] Error reading file: {ex.Message}";
        }
    }

    private async Task<string> ExtractTextFromPdfAsync(string filePath)
    {
        return await Task.Run(async () =>
        {
            try
            {
                using var pdfReader = new PdfReader(filePath);
                using var pdfDoc = new PdfDocument(pdfReader);

                var text = new StringBuilder();
                int totalPages = pdfDoc.GetNumberOfPages();

                const int maxPages = 1000;
                int pagesToProcess = Math.Min(totalPages, maxPages);

                if (totalPages > maxPages)
                {
                    text.AppendLine($"[LARGE PDF WARNING] PDF has {totalPages} pages. Processing first {maxPages} pages only.\n");
                }

                _logger.LogInformation("Processing PDF with {TotalPages} pages, processing {PagesToProcess}", totalPages, pagesToProcess);

                for (int pageNum = 1; pageNum <= pagesToProcess; pageNum++)
                {
                    try
                    {
                        var page = pdfDoc.GetPage(pageNum);

                        // Try multiple extraction strategies for better OCR handling
                        var pageText = ExtractTextWithMultipleStrategies(page, pageNum);

                        // Apply universal text cleaning for better quality
                        pageText = await ApplyUniversalTextCleaning(pageText);

                        if (!string.IsNullOrWhiteSpace(pageText))
                        {
                            text.AppendLine($"[PAGE {pageNum}]");
                            text.AppendLine(pageText);
                            text.AppendLine($"[END PAGE {pageNum}]");
                            text.AppendLine();
                        }
                        else
                        {
                            _logger.LogWarning("Page {PageNum} produced no readable text", pageNum);
                        }

                        if (pageNum % 50 == 0)
                        {
                            GC.Collect(0, GCCollectionMode.Optimized);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to extract text from page {PageNum}", pageNum);
                        text.AppendLine($"[PAGE ERROR {pageNum}] Failed to extract page: {ex.Message}");
                    }
                }

                if (totalPages > maxPages)
                {
                    text.AppendLine($"\n[TRUNCATED] Remaining {totalPages - maxPages} pages not processed due to size limits.");
                }

                var finalText = text.ToString();
                _logger.LogInformation("PDF extraction completed. Extracted {TextLength} characters from {PagesProcessed} pages",
                    finalText.Length, pagesToProcess);

                return finalText;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process PDF file: {FilePath}", filePath);
                return $"[PDF ERROR] Failed to process PDF: {ex.Message}";
            }
        });
    }

    private async Task<string> ExtractTextFromDocxAsync(string filePath)
    {
        return await Task.Run(async () =>
        {
            try
            {
                using var doc = WordprocessingDocument.Open(filePath, false);
                var content = new StringBuilder();

                if (doc.MainDocumentPart?.Document?.Body == null)
                {
                    return "[DOCX ERROR] Invalid Word document structure.";
                }

                var body = doc.MainDocumentPart.Document.Body;
                var paragraphs = body.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>().ToList();

                const int maxParagraphs = 5000;
                int totalParagraphs = paragraphs.Count;
                int paragraphsToProcess = Math.Min(totalParagraphs, maxParagraphs);

                if (totalParagraphs > maxParagraphs)
                {
                    content.AppendLine($"[LARGE DOCX WARNING] Word document has {totalParagraphs} paragraphs. Processing first {maxParagraphs} paragraphs only.\n");
                }

                int processedParagraphs = 0;
                foreach (var paragraph in paragraphs.Take(paragraphsToProcess))
                {
                    try
                    {
                        var paragraphText = paragraph.InnerText?.Trim();
                        if (!string.IsNullOrWhiteSpace(paragraphText))
                        {
                            var pPr = paragraph.Elements<DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties>().FirstOrDefault();
                            var pStyle = pPr?.Elements<DocumentFormat.OpenXml.Wordprocessing.ParagraphStyleId>().FirstOrDefault();

                            if (pStyle != null && pStyle.Val?.Value?.Contains("Heading") == true)
                            {
                                content.AppendLine($"[HEADING] {paragraphText}");
                            }
                            else
                            {
                                content.AppendLine(paragraphText);
                            }
                        }

                        processedParagraphs++;

                        if (processedParagraphs % 500 == 0)
                        {
                            GC.Collect(0, GCCollectionMode.Optimized);
                        }
                    }
                    catch (Exception ex)
                    {
                        content.AppendLine($"[PARAGRAPH ERROR] Failed to extract paragraph: {ex.Message}");
                    }
                }

                // Extract tables
                var tables = body.Elements<DocumentFormat.OpenXml.Wordprocessing.Table>().Take(100);
                if (tables.Any())
                {
                    content.AppendLine("\n[TABLES]");
                    int tableCount = 0;

                    foreach (var table in tables)
                    {
                        try
                        {
                            content.AppendLine($"[TABLE {++tableCount}]");
                            var rows = table.Elements<DocumentFormat.OpenXml.Wordprocessing.TableRow>().Take(50);

                            foreach (var row in rows)
                            {
                                var cells = row.Elements<DocumentFormat.OpenXml.Wordprocessing.TableCell>().Take(20);
                                var cellTexts = cells.Select(cell => cell.InnerText?.Trim() ?? "").Where(text => !string.IsNullOrWhiteSpace(text));

                                if (cellTexts.Any())
                                {
                                    content.AppendLine(string.Join(" | ", cellTexts));
                                }
                            }
                            content.AppendLine($"[END TABLE {tableCount}]");
                        }
                        catch (Exception ex)
                        {
                            content.AppendLine($"[TABLE ERROR {tableCount}] Failed to extract table: {ex.Message}");
                        }
                    }
                    content.AppendLine("[END TABLES]");
                }

                if (totalParagraphs > maxParagraphs)
                {
                    content.AppendLine($"\n[TRUNCATED] Remaining {totalParagraphs - maxParagraphs} paragraphs not processed due to size limits.");
                }

                var result = content.ToString();

                // Apply universal text cleaning for better quality
                result = await ApplyUniversalTextCleaning(result);

                _logger.LogInformation("DOCX extraction completed. Extracted {TextLength} characters from {ParagraphsProcessed} paragraphs",
                    result.Length, Math.Min(totalParagraphs, maxParagraphs));

                return result;
            }
            catch (Exception ex)
            {
                return $"[DOCX ERROR] Failed to process Word document: {ex.Message}";
            }
        });
    }

    private async Task<string> ExtractTextFromDocAsync(string filePath)
    {
        return await Task.Run(async () =>
        {
            try
            {
                // Read the file as bytes
                var bytes = File.ReadAllBytes(filePath);

                // Try multiple encodings to extract text
                var extractedTexts = new List<string>();

                // Try UTF-8
                try
                {
                    var utf8Text = System.Text.Encoding.UTF8.GetString(bytes);
                    extractedTexts.Add(ExtractReadableText(utf8Text));
                }
                catch { }

                // Try Windows-1252 (common for older Word docs)
                try
                {
                    var windows1252 = System.Text.Encoding.GetEncoding(1252);
                    var winText = windows1252.GetString(bytes);
                    extractedTexts.Add(ExtractReadableText(winText));
                }
                catch { }

                // Try ASCII
                try
                {
                    var asciiText = System.Text.Encoding.ASCII.GetString(bytes);
                    extractedTexts.Add(ExtractReadableText(asciiText));
                }
                catch { }

                // Find the best extraction (longest meaningful text)
                var bestText = extractedTexts
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .OrderByDescending(t => GetMeaningfulTextScore(t))
                    .FirstOrDefault();

                if (string.IsNullOrWhiteSpace(bestText) || bestText.Length < 50)
                {
                    return "[DOC WARNING] Could not extract readable text from legacy .doc format. Please convert to .docx for better results.";
                }

                // Apply universal text cleaning for better quality
                bestText = await ApplyUniversalTextCleaning(bestText);

                _logger.LogInformation("DOC extraction completed. Extracted {TextLength} characters from legacy Word document",
                    bestText.Length);

                return $"[DOC FORMAT] Extracted from legacy Word document:\n\n{bestText}";
            }
            catch (Exception ex)
            {
                return $"[DOC ERROR] Failed to process .doc file: {ex.Message}. Please convert to .docx format for better results.";
            }
        });
    }

    private string ExtractReadableText(string rawText)
    {
        var cleanText = new StringBuilder();
        var words = new List<string>();

        // Extract sequences of readable characters
        var currentWord = new StringBuilder();

        for (int i = 0; i < rawText.Length; i++)
        {
            char c = rawText[i];

            // Keep readable characters
            if (char.IsLetter(c) || char.IsDigit(c) ||
                ".,!?;:()\"-'".Contains(c))
            {
                currentWord.Append(c);
            }
            else if (char.IsWhiteSpace(c))
            {
                if (currentWord.Length > 0)
                {
                    var word = currentWord.ToString();
                    if (IsValidWord(word))
                    {
                        words.Add(word);
                    }
                    currentWord.Clear();
                }
            }
            else
            {
                // End current word on binary/control characters
                if (currentWord.Length > 0)
                {
                    var word = currentWord.ToString();
                    if (IsValidWord(word))
                    {
                        words.Add(word);
                    }
                    currentWord.Clear();
                }
            }
        }

        // Add the last word if any
        if (currentWord.Length > 0)
        {
            var word = currentWord.ToString();
            if (IsValidWord(word))
            {
                words.Add(word);
            }
        }

        // Join valid words with spaces
        var result = string.Join(" ", words);

        // Clean up excessive whitespace
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\s{2,}", " ");

        return result.Trim();
    }

    private bool IsValidWord(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return false;

        // Allow single characters if they're meaningful (like "A", "I", numbers)
        if (word.Length == 1)
        {
            return char.IsLetterOrDigit(word[0]) || "AI".Contains(word[0]);
        }

        // Allow pure numbers (dates, quantities, etc.)
        if (word.All(char.IsDigit))
        {
            return word.Length <= 10; // Reasonable number length
        }

        // Allow mixed numbers and letters (like "2008", "56", "18,098,912")
        if (word.Any(char.IsDigit) && word.Any(c => char.IsLetterOrDigit(c) || ",.-".Contains(c)))
        {
            // Skip obvious artifacts like "000000000100HK"
            if (word.Length > 15 && word.Count(char.IsDigit) > word.Count(char.IsLetter) * 3)
                return false;

            return true;
        }

        // Must contain at least one letter for text words
        if (!word.Any(char.IsLetter))
            return false;

        // Skip obvious binary artifacts
        if (word.Length > 50) // Very long strings are usually binary
            return false;

        // Skip strings that are mostly punctuation
        int punctCount = word.Count(c => ".,!?;:()\"'-".Contains(c));
        int letterCount = word.Count(char.IsLetter);

        if (punctCount > letterCount && letterCount == 0)
            return false;

        return true;
    }

    private int GetMeaningfulTextScore(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var meaningfulWords = words.Where(IsValidWord).Count();

        // Score based on meaningful word count and average word length
        var avgWordLength = words.Where(IsValidWord).DefaultIfEmpty("").Average(w => w.Length);

        return (int)(meaningfulWords * avgWordLength);
    }

    private async Task<string> ExtractTextFromXlsxAsync(string filePath)
    {
        return await Task.Run(async () =>
        {
            try
            {
                using var spreadsheet = SpreadsheetDocument.Open(filePath, false);
                var content = new StringBuilder();

                var workbookPart = spreadsheet.WorkbookPart;
                if (workbookPart == null) return "[EXCEL ERROR] Invalid Excel file structure.";

                var sheets = workbookPart.Workbook.Descendants<Sheet>().ToList();
                const int maxSheets = 50;
                const int maxRows = 10000;
                const int maxCols = 500;

                int sheetsToProcess = Math.Min(sheets.Count, maxSheets);
                if (sheets.Count > maxSheets)
                {
                    content.AppendLine($"[LARGE EXCEL WARNING] Excel has {sheets.Count} sheets. Processing first {maxSheets} sheets only.\n");
                }

                int sheetCount = 0;
                foreach (var sheet in sheets.Take(sheetsToProcess))
                {
                    try
                    {
                        var sheetName = sheet.Name?.Value ?? $"Sheet{sheetCount + 1}";
                        var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id);
                        var worksheet = worksheetPart.Worksheet;

                        content.AppendLine($"[SHEET {sheetName}]");

                        var allCells = worksheet.Descendants<Cell>().ToList();
                        var cellDict = new Dictionary<string, Cell>();

                        foreach (var cell in allCells)
                        {
                            if (cell.CellReference != null)
                            {
                                cellDict[cell.CellReference.Value] = cell;
                            }
                        }

                        if (cellDict.Any())
                        {
                            var minRow = cellDict.Keys.Min(cellRef => GetRowIndex(cellRef));
                            var maxRow = Math.Min(cellDict.Keys.Max(cellRef => GetRowIndex(cellRef)), minRow + maxRows);
                            var minCol = cellDict.Keys.Min(cellRef => GetColumnIndex(cellRef));
                            var maxCol = Math.Min(cellDict.Keys.Max(cellRef => GetColumnIndex(cellRef)), minCol + maxCols);

                            bool rowLimitReached = cellDict.Keys.Max(cellRef => GetRowIndex(cellRef)) > maxRow;
                            bool colLimitReached = cellDict.Keys.Max(cellRef => GetColumnIndex(cellRef)) > maxCol;

                            if (rowLimitReached || colLimitReached)
                            {
                                content.AppendLine($"[LARGE SHEET WARNING] Sheet truncated - processing {maxRow - minRow + 1} rows and {maxCol - minCol + 1} columns max.\n");
                            }

                            for (int row = minRow; row <= maxRow; row++)
                            {
                                content.AppendLine($"[ROW {row}]");
                                var rowCells = new List<string>();

                                for (int col = minCol; col <= maxCol; col++)
                                {
                                    var cellRef = GetCellReference(row, col);
                                    if (cellDict.ContainsKey(cellRef))
                                    {
                                        var cellValue = GetCellValue(cellDict[cellRef], workbookPart);
                                        rowCells.Add(cellValue);
                                    }
                                    else
                                    {
                                        rowCells.Add("");
                                    }
                                }

                                if (rowCells.Any(c => !string.IsNullOrWhiteSpace(c)))
                                {
                                    content.AppendLine(string.Join(" | ", rowCells));
                                }

                                content.AppendLine($"[END ROW {row}]");

                                if ((row - minRow) % 1000 == 0)
                                {
                                    GC.Collect(0, GCCollectionMode.Optimized);
                                }
                            }
                        }

                        content.AppendLine($"[END SHEET {sheetName}]");
                        sheetCount++;
                    }
                    catch (Exception ex)
                    {
                        content.AppendLine($"[SHEET ERROR] Failed to process sheet: {ex.Message}");
                    }
                }

                var result = content.ToString();

                // Apply universal text cleaning for better quality
                result = await ApplyUniversalTextCleaning(result);

                _logger.LogInformation("Excel extraction completed. Extracted {TextLength} characters from {SheetCount} sheets",
                    result.Length, sheetCount);

                return result;
            }
            catch (Exception ex)
            {
                return $"[EXCEL ERROR] Failed to process Excel file: {ex.Message}";
            }
        });
    }

    private async Task<string> ExtractTextFromPptxAsync(string filePath)
    {
        return await Task.Run(async () =>
        {
            try
            {
                using var presentation = PresentationDocument.Open(filePath, false);
                var content = new StringBuilder();

                var slideParts = presentation.PresentationPart?.SlideParts?.ToList() ?? new List<SlidePart>();

                const int maxSlides = 500;
                int totalSlides = slideParts.Count;
                int slidesToProcess = Math.Min(totalSlides, maxSlides);

                if (totalSlides > maxSlides)
                {
                    content.AppendLine($"[LARGE PPTX WARNING] PowerPoint has {totalSlides} slides. Processing first {maxSlides} slides only.\n");
                }

                for (int slideIndex = 0; slideIndex < slidesToProcess; slideIndex++)
                {
                    try
                    {
                        var slidePart = slideParts[slideIndex];
                        content.AppendLine($"[SLIDE {slideIndex + 1}]");

                        var slide = slidePart.Slide;
                        if (slide?.CommonSlideData?.ShapeTree != null)
                        {
                            foreach (var shape in slide.CommonSlideData.ShapeTree.Elements())
                            {
                                try
                                {
                                    var textBodies = shape.Descendants<DocumentFormat.OpenXml.Drawing.Text>();
                                    foreach (var textElement in textBodies)
                                    {
                                        if (!string.IsNullOrWhiteSpace(textElement.Text))
                                        {
                                            content.AppendLine($"  {textElement.Text.Trim()}");
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    content.AppendLine($"  [SHAPE ERROR] Failed to extract shape content: {ex.Message}");
                                }
                            }
                        }

                        content.AppendLine($"[END SLIDE {slideIndex + 1}]");
                        content.AppendLine();

                        if ((slideIndex + 1) % 25 == 0)
                        {
                            GC.Collect(0, GCCollectionMode.Optimized);
                        }
                    }
                    catch (Exception ex)
                    {
                        content.AppendLine($"[SLIDE ERROR {slideIndex + 1}] Failed to process slide: {ex.Message}");
                    }
                }

                if (totalSlides > maxSlides)
                {
                    content.AppendLine($"\n[TRUNCATED] Remaining {totalSlides - maxSlides} slides not processed due to size limits.");
                }

                var result = content.ToString();

                // Apply universal text cleaning for better quality
                result = await ApplyUniversalTextCleaning(result);

                _logger.LogInformation("PowerPoint extraction completed. Extracted {TextLength} characters from {SlidesProcessed} slides",
                    result.Length, Math.Min(totalSlides, maxSlides));

                return result;
            }
            catch (Exception ex)
            {
                return $"[PPTX ERROR] Failed to process PowerPoint file: {ex.Message}";
            }
        });
    }

    // Helper methods for Excel processing
    private static int GetRowIndex(string cellReference)
    {
        var match = Regex.Match(cellReference, @"\d+");
        return match.Success ? int.Parse(match.Value) : 1;
    }

    private static int GetColumnIndex(string cellReference)
    {
        var match = Regex.Match(cellReference, @"[A-Z]+");
        if (!match.Success) return 1;

        var columnName = match.Value;
        int result = 0;
        for (int i = 0; i < columnName.Length; i++)
        {
            result = result * 26 + (columnName[i] - 'A' + 1);
        }
        return result;
    }

    private static string GetCellReference(int row, int col)
    {
        string columnName = "";
        while (col > 0)
        {
            col--;
            columnName = (char)('A' + col % 26) + columnName;
            col /= 26;
        }
        return columnName + row;
    }

    private static string GetCellValue(Cell cell, WorkbookPart workbookPart)
    {
        if (cell?.CellValue == null) return "";

        var value = cell.CellValue.Text;

        if (cell.DataType != null && cell.DataType == CellValues.SharedString)
        {
            var sharedStringTable = workbookPart.SharedStringTablePart?.SharedStringTable;
            if (sharedStringTable != null && int.TryParse(value, out int index))
            {
                var sharedString = sharedStringTable.Elements<SharedStringItem>().ElementAtOrDefault(index);
                return sharedString?.InnerText ?? value;
            }
        }
        else if (cell.DataType != null && cell.DataType == CellValues.Boolean)
        {
            return value == "1" ? "TRUE" : "FALSE";
        }
        else if (cell.DataType == null || cell.DataType == CellValues.Number)
        {
            if (double.TryParse(value, out double numValue))
            {
                if (numValue > 1 && numValue < 2958466)
                {
                    try
                    {
                        var date = DateTime.FromOADate(numValue);
                        if (date.Date != DateTime.MinValue.Date)
                        {
                            return date.ToString("yyyy-MM-dd HH:mm:ss");
                        }
                    }
                    catch
                    {
                        // Return as number if date conversion fails
                    }
                }
            }
            return value;
        }

        return value ?? "";
    }

    // Validation methods
    private async Task<(bool IsValid, string? ErrorMessage)> ValidatePdfFileAsync(string filePath)
    {
        try
        {
            using var pdfReader = new PdfReader(filePath);
            using var pdfDoc = new PdfDocument(pdfReader);

            if (pdfDoc.GetNumberOfPages() == 0)
            {
                return (false, "PDF file contains no pages");
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Invalid PDF file: {ex.Message}");
        }
    }

    private async Task<(bool IsValid, string? ErrorMessage)> ValidateDocxFileAsync(string filePath)
    {
        try
        {
            using var doc = WordprocessingDocument.Open(filePath, false);
            if (doc.MainDocumentPart?.Document?.Body == null)
            {
                return (false, "Invalid Word document structure");
            }
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Invalid DOCX file: {ex.Message}");
        }
    }

    private async Task<(bool IsValid, string? ErrorMessage)> ValidateXlsxFileAsync(string filePath)
    {
        try
        {
            using var spreadsheet = SpreadsheetDocument.Open(filePath, false);
            if (spreadsheet.WorkbookPart == null)
            {
                return (false, "Invalid Excel file structure");
            }
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Invalid XLSX file: {ex.Message}");
        }
    }

    private async Task<(bool IsValid, string? ErrorMessage)> ValidatePptxFileAsync(string filePath)
    {
        try
        {
            using var presentation = PresentationDocument.Open(filePath, false);
            if (presentation.PresentationPart == null)
            {
                return (false, "Invalid PowerPoint file structure");
            }
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Invalid PPTX file: {ex.Message}");
        }
    }

    private string FixTextSpacing(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var result = new StringBuilder();
        bool lastWasLetter  = false;
        bool lastWasDigit   = false;
        bool lastWasUpper   = false;

        for (int i = 0; i < text.Length; i++)
        {
            char current = text[i];

            // Add space before uppercase letters that follow lowercase letters or digits.
            // Do NOT add a space if we are inside an acronym — detected by checking BOTH
            // the previous character (was it uppercase?) AND the next character (is it uppercase?).
            // This correctly handles: "OpenText" (split), "IBM" (no split), "CEO" (no split).
            if (char.IsUpper(current) && (lastWasLetter || lastWasDigit) &&
                result.Length > 0 && result[result.Length - 1] != ' ' && result[result.Length - 1] != '\n')
            {
                bool prevWasUpper = lastWasUpper;
                bool nextIsUpper  = i + 1 < text.Length && char.IsUpper(text[i + 1]);

                // Skip space insertion when we are continuing or ending an acronym
                bool isAcronym = prevWasUpper || nextIsUpper;
                if (!isAcronym)
                {
                    result.Append(' ');
                }
            }

            result.Append(current);

            lastWasUpper  = char.IsUpper(current);
            lastWasLetter = char.IsLetter(current);
            lastWasDigit  = char.IsDigit(current);
        }

        return result.ToString();
    }

    /// <summary>
    /// Targeted post-processing corrections for PDF extraction artefacts that survive
    /// the CleanOcrNoise → ReconstructText → FixTextSpacing pipeline.
    ///
    /// Three classes of issue are addressed:
    ///   1. Uppercase acronym fragments joined by a stray space after glyph-level
    ///      line-splitting  (e.g. "CE O" → "CEO", "IB M" → "IBM").
    ///      These arise when individual glyphs have baseline Y coordinates that
    ///      differ by more than the FontAware merge threshold (now 4pt, was 2pt).
    ///      The enumerated patterns here provide an extra safety net for any that
    ///      still slip through.
    ///
    ///   2. CamelCase brand / product names that FixTextSpacing incorrectly splits
    ///      (e.g. "Open Text" → "OpenText", "Mc Gourlay" → "McGourlay").
    ///      FixTextSpacing must insert spaces at CamelCase transitions for normal
    ///      prose (e.g. "ThisMethod" → "This Method"), so it cannot distinguish
    ///      brand names.  We restore them here.  Add to the list as new names
    ///      appear in the document corpus.
    ///
    ///   3. Missing inter-word spaces where the PDF content stream omits the gap
    ///      between adjacent words ("anda" = "and" + "a").  We only correct tokens
    ///      that appear as standalone words (full \b…\b match) so that real words
    ///      containing the same substring (e.g. "miranda") are never touched.
    /// </summary>
    private string NormalizeExtractedText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        // ── 1. Known uppercase acronym fragments ──────────────────────────────
        // Enumerated explicitly (rather than a broad regex) to avoid accidentally
        // merging legitimate two-word phrases that happen to be all-caps.
        var acronymFixes = new (string Pattern, string Replacement)[]
        {
            (@"\bCE\s+O\b",  "CEO"),   // CEO most common split in C-suite titles
            (@"\bCF\s+O\b",  "CFO"),
            (@"\bCT\s+O\b",  "CTO"),
            (@"\bCO\s+O\b",  "COO"),
            (@"\bCH\s+O\b",  "CHO"),
            (@"\bCS\s+O\b",  "CSO"),
            (@"\bIB\s+M\b",  "IBM"),
            (@"\bER\s+P\b",  "ERP"),
            (@"\bCR\s+M\b",  "CRM"),
            (@"\bHR\s+M\b",  "HRM"),
            (@"\bSA\s+P\b",  "SAP"),
        };
        foreach (var (pattern, replacement) in acronymFixes)
            text = Regex.Replace(text, pattern, replacement, RegexOptions.IgnoreCase);

        // Financial year / quarter shorthands split by a stray space:
        //   "FY 26" → "FY26",  "Q 3" → "Q3",  "H 1" → "H1"
        text = Regex.Replace(text, @"\b(FY|FQ|Q[1-4]?|H[12])\s+(\d{1,4})\b", "$1$2");

        // ── 2. Brand / organisation name corrections ──────────────────────────
        var brandFixes = new (string Wrong, string Right)[]
        {
            ("Open Text",    "OpenText"),
            ("Mc Gourlay",   "McGourlay"),
            ("Mc Kinsey",    "McKinsey"),
            ("Mc Donald",    "McDonald"),
            ("De Loitte",    "Deloitte"),
            ("Ac centure",   "Accenture"),
            // Add further brand names encountered in the document corpus here.
        };
        foreach (var (wrong, right) in brandFixes)
            text = Regex.Replace(text, @"\b" + Regex.Escape(wrong) + @"\b", right,
                RegexOptions.IgnoreCase);

        // ── 3. Missing inter-word spaces (standalone fused tokens only) ───────
        // Using \b…\b guarantees we only split the token when it stands alone as
        // a whole word — "miranda" is NOT matched because "anda" is not at a \b.
        // Not using IgnoreCase so that capitalised proper nouns (e.g. "Anda") are
        // left untouched.
        var wordFusions = new (string Pattern, string Replacement)[]
        {
            (@"\banda\b",  "and a"),    // "and a" fused
            (@"\boran\b",  "or an"),    // "or an" fused
            (@"\bwitha\b", "with a"),   // "with a" fused
            (@"\bofa\b",   "of a"),     // "of a" fused
            (@"\bina\b",   "in a"),     // "in a" fused — rare but observed
        };
        foreach (var (pattern, replacement) in wordFusions)
            text = Regex.Replace(text, pattern, replacement);

        return text;
    }

    /// <summary>
    /// Extract text using multiple strategies for better OCR handling
    /// </summary>
    private string ExtractTextWithMultipleStrategies(iText.Kernel.Pdf.PdfPage page, int pageNum)
    {
        var bestText = "";
        var maxScore = 0;

        try
        {
            // Strategy 1: Simple text extraction
            var simpleStrategy = new SimpleTextExtractionStrategy();
            var simpleText = PdfTextExtractor.GetTextFromPage(page, simpleStrategy);
            var simpleScore = CalculateTextQuality(simpleText);

            if (simpleScore > maxScore)
            {
                maxScore = simpleScore;
                bestText = simpleText;
            }

            // Strategy 2: Location-based text extraction (preserves positioning)
            var locationStrategy = new LocationTextExtractionStrategy();
            var locationText = PdfTextExtractor.GetTextFromPage(page, locationStrategy);
            var locationScore = CalculateTextQuality(locationText);

            if (locationScore > maxScore)
            {
                maxScore = locationScore;
                bestText = locationText;
            }

            // Strategy 3: Font-aware extraction — detects bold/large text and tags it as [HEADING].
            // This catches headings that Strategy 1/2 skip because they use a separately-named
            // embedded font (e.g. Arial-BoldMT, Calibri-Bold) or live in a header XObject.
            var fontAwareStrategy = new FontAwareTextExtractionStrategy();
            var fontAwareText = PdfTextExtractor.GetTextFromPage(page, fontAwareStrategy);
            var fontAwareScore = CalculateTextQuality(fontAwareText);

            // Prefer font-aware result when it scores at least as well, because it preserves
            // heading markers that improve RAG chunking quality.
            if (fontAwareScore >= maxScore && !string.IsNullOrWhiteSpace(fontAwareText))
            {
                maxScore = fontAwareScore;
                bestText = fontAwareText;
            }
            else if (!string.IsNullOrWhiteSpace(fontAwareText))
            {
                // Even if font-aware didn't win overall, merge any [HEADING] lines it found
                // that are missing from the best result so headings are never silently dropped.
                bestText = MergeHeadingsIntoText(bestText, fontAwareText);
            }

            _logger.LogDebug("Page {PageNum}: Simple={SimpleScore}, Location={LocationScore}, FontAware={FontAwareScore}, Selected={MaxScore}",
                pageNum, simpleScore, locationScore, fontAwareScore, maxScore);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract text from page {PageNum} with advanced strategies, falling back to simple", pageNum);
            bestText = PdfTextExtractor.GetTextFromPage(page, new SimpleTextExtractionStrategy());
        }

        return bestText ?? "";
    }

    /// <summary>
    /// Merges [HEADING] lines found by the font-aware strategy into the base text.
    /// Inserts any heading that is not already present so bold keywords are never lost.
    /// </summary>
    private string MergeHeadingsIntoText(string baseText, string fontAwareText)
    {
        var headingLines = fontAwareText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.TrimStart().StartsWith("[HEADING]"))
            .Select(l => l.Trim())
            .ToList();

        if (headingLines.Count == 0)
            return baseText;

        var merged = new StringBuilder(baseText);
        foreach (var heading in headingLines)
        {
            // Extract the plain text part after [HEADING]
            var headingContent = heading.Replace("[HEADING]", "").Trim();
            // Only inject if neither the tagged nor untagged version exists already
            if (!baseText.Contains(headingContent, StringComparison.OrdinalIgnoreCase) &&
                !baseText.Contains(heading, StringComparison.OrdinalIgnoreCase))
            {
                merged.Insert(0, heading + Environment.NewLine);
            }
        }

        return merged.ToString();
    }

    /// <summary>
    /// Calculate text quality score to determine best extraction strategy
    /// </summary>
    private int CalculateTextQuality(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var score = 0;
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Score based on number of readable words
        score += words.Length;

        // Bonus for words with proper capitalization
        foreach (var word in words)
        {
            if (word.Length > 1 && char.IsUpper(word[0]) && word.Skip(1).All(char.IsLower))
                score += 2;

            // Bonus for common English words
            if (IsCommonWord(word.ToLowerInvariant()))
                score += 3;

            // Penalty for excessive special characters
            var specialCharCount = word.Count(c => !char.IsLetterOrDigit(c) && c != '.' && c != '-');
            if (specialCharCount > word.Length / 2)
                score -= 5;
        }

        return Math.Max(0, score);
    }

    /// <summary>
    /// Check if word is a common English word
    /// </summary>
    private bool IsCommonWord(string word)
    {
        var commonWords = new HashSet<string>
        {
            "the", "and", "or", "but", "in", "on", "at", "to", "for", "of", "with", "by", "from",
            "is", "are", "was", "were", "be", "been", "have", "has", "had", "do", "does", "did",
            "will", "would", "could", "should", "may", "might", "can", "this", "that", "these", "those",
            "a", "an", "one", "two", "three", "first", "second", "third", "new", "old", "good", "bad",
            "service", "api", "system", "data", "user", "file", "document", "process", "method", "function"
        };

        return commonWords.Contains(word);
    }

    /// <summary>
    /// Advanced OCR noise cleaning for all document types
    /// </summary>
    private async Task<string> CleanOcrNoise(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var cleaned = text;

        // For severely corrupted OCR text, use AI-powered correction
        if (IsTextSeverelyCorrupted(cleaned))
        {
            _logger.LogInformation("Detected severely corrupted OCR text, applying AI-powered correction");
            cleaned = await ApplyAiTextCorrection(cleaned);
        }
        else
        {
            // Apply standard character-level OCR corrections
            cleaned = ApplyCharacterLevelOcrCorrection(cleaned);
        }

        // Fix common OCR word-level substitutions
        var ocrFixes = new Dictionary<string, string>
        {
            // File and path related terms
            { "fi1e", "file" }, { "fi1es", "files" }, { "confi9", "config" }, { "config1", "config" },
            { "Prograrn", "Program" }, { "Fi1es", "Files" }, { "Micro", "Micro" },
            { "C0nfig", "Config" }, { "configurationfi1e", "configuration file" },
            { "insta11ed", "installed" }, { "insta11ation", "installation" },
            { "Managerll", "Manager" }, { "5ervice", "Service" }, { "5erviceAP1", "ServiceAPI" },
            { "AP1", "API" }, { "webconfigurationfi1es", "web configuration files" },
            { "workpathfo1der", "work path folder" }, { "bydefau1t", "by default" },
            { "fo11owing", "following" }, { "1ocation", "location" },

            // Common technical terms
            { "ser vice", "service" }, { "A PI", "API" }, { "sy stem", "system" },
            { "pro cess", "process" }, { "doc ument", "document" }, { "re quest", "request" },
            { "res ponse", "response" }, { "meth od", "method" }, { "func tion", "function" },
            { "data base", "database" }, { "work flow", "workflow" }, { "user narne", "username" },
            { "pass word", "password" }, { "cornputer", "computer" }, { "environrnent", "environment" },

            // Directory and system terms
            { "fo1der", "folder" }, { "directo1y", "directory" }, { "w0rkgroup", "workgroup" },
            { "Workgroup5erver", "Workgroup Server" }, { "5erver", "Server" },
            { "1ocal", "local" }, { "1ocalhost", "localhost" },
            { "hostnarne", "hostname" }, { "1P", "IP" }, { "Paddress", "IP address" },

            // HTTP and web terms
            { "http", "http" }, { "https", "https" }, { "we6site", "website" },
            { "6rowser", "browser" }, { "HT ML", "HTML" }, { "HT TP", "HTTP" },
            { "HT TPS", "HTTPS" }, { "JS ON", "JSON" }, { "XM L", "XML" },

            // Common English words with OCR issues
            { "exarnp1e", "example" }, { "exarnp1es", "examples" }, { "forexarnp1e", "for example" },
            { "detai1s", "details" }, { "detai1", "detail" }, { "rnore", "more" },
            { "rnessage", "message" }, { "rneaning", "meaning" }, { "tirne", "time" },
            { "sorne", "some" }, { "frorn", "from" }, { "thern", "them" }, { "forrn", "form" },
            { "narne", "name" }, { "narnes", "names" }, { "filenarne", "filename" },
            { "userrarne", "username" },

            // Space-related issues
            { "  ", " " }, // Multiple spaces
            { " ,", "," }, { " .", "." }, { " !", "!" }, { " ?", "?" },
            { "( ", "(" }, { " )", ")" }, { "[ ", "[" }, { " ]", "]" },

            // Remove artifacts
            { "�", "" }, { "¤", "" }, { "□", "" }, { "■", "" }, { "▪", "" }, { "○", "" }
        };

        // Apply OCR fixes
        foreach (var fix in ocrFixes)
        {
            cleaned = cleaned.Replace(fix.Key, fix.Value);
        }

        // Fix broken words at line endings
        cleaned = Regex.Replace(cleaned, @"(\w+)-\s*\r?\n\s*(\w+)", "$1$2");

        // Remove excessive whitespace but preserve paragraph breaks
        cleaned = Regex.Replace(cleaned, @"[ \t]+", " ");
        cleaned = Regex.Replace(cleaned, @"\r?\n[ \t]*\r?\n", "\n\n");
        cleaned = Regex.Replace(cleaned, @"\r?\n", " ");
        cleaned = Regex.Replace(cleaned, @"\s{3,}", "\n\n");

        return cleaned.Trim();
    }

    /// <summary>
    /// Intelligent text reconstruction for all document types
    /// </summary>
    private string ReconstructText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var reconstructed = new StringBuilder();
        var currentParagraph = new StringBuilder();

        foreach (var line in lines)
        {
            var cleanLine = line.Trim();
            if (string.IsNullOrWhiteSpace(cleanLine))
                continue;

            // Detect if this should start a new paragraph
            if (ShouldStartNewParagraph(cleanLine, currentParagraph.ToString()))
            {
                if (currentParagraph.Length > 0)
                {
                    reconstructed.AppendLine(currentParagraph.ToString().Trim());
                    reconstructed.AppendLine();
                    currentParagraph.Clear();
                }
            }

            // Add space if continuing a paragraph
            if (currentParagraph.Length > 0)
                currentParagraph.Append(" ");

            currentParagraph.Append(cleanLine);
        }

        // Add final paragraph
        if (currentParagraph.Length > 0)
        {
            reconstructed.AppendLine(currentParagraph.ToString().Trim());
        }

        return reconstructed.ToString();
    }

    /// <summary>
    /// Determine if a line should start a new paragraph
    /// </summary>
    private bool ShouldStartNewParagraph(string line, string currentParagraph)
    {
        if (string.IsNullOrWhiteSpace(currentParagraph))
            return true;

        // Headers and titles (often all caps or title case)
        if (line.All(c => char.IsUpper(c) || char.IsWhiteSpace(c) || char.IsDigit(c) || char.IsPunctuation(c)))
            return true;

        // Numbered lists
        if (Regex.IsMatch(line, @"^\d+[\.\)]\s"))
            return true;

        // Bullet points
        if (Regex.IsMatch(line, @"^[\-\*\•]\s"))
            return true;

        // Indented content
        if (line.StartsWith("  ") || line.StartsWith("\t"))
            return true;

        // API endpoints or technical identifiers
        if (Regex.IsMatch(line, @"^(GET|POST|PUT|DELETE|PATCH)\s|^https?://|^www\.|^\w+\.\w+\("))
            return true;

        // Code-like content
        if (line.Contains("{") || line.Contains("}") || line.Contains("//") || line.Contains("/*"))
            return true;

        return false;
    }

    /// <summary>
    /// Detect if text is severely corrupted by OCR and needs AI correction
    /// </summary>
    private bool IsTextSeverelyCorrupted(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 100)
            return false;

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 10)
            return false;

        var corruptedCount = 0;
        var totalWords = Math.Min(words.Length, 50); // Check first 50 words

        foreach (var word in words.Take(totalWords))
        {
            // Count words with obvious OCR corruption patterns
            if (word.Length > 2 && (
                Regex.IsMatch(word, @"[0-9][a-zA-Z]") || // Numbers mixed with letters
                Regex.IsMatch(word, @"[a-zA-Z][0-9]") ||
                word.Contains("1l") || word.Contains("l1") ||
                word.Contains("0O") || word.Contains("O0") ||
                word.Contains("5S") || word.Contains("S5") ||
                word.Contains("rn") ||
                Regex.IsMatch(word, @"[a-zA-Z]{2,}[0-9]+[a-zA-Z]+") || // Pattern like "confi9uration"
                word.Count(c => char.IsDigit(c)) > word.Length / 3)) // More than 1/3 digits
            {
                corruptedCount++;
            }
        }

        // If more than 40% of words appear corrupted, consider it severely corrupted
        var corruptionRatio = (double)corruptedCount / totalWords;
        _logger.LogDebug("OCR corruption analysis: {CorruptedWords}/{TotalWords} = {CorruptionRatio:P1}",
            corruptedCount, totalWords, corruptionRatio);

        return corruptionRatio > 0.4;
    }

    /// <summary>
    /// Use AI to correct severely corrupted OCR text
    /// </summary>
    private async Task<string> ApplyAiTextCorrection(string corruptedText)
    {
        try
        {
            // Split text into manageable chunks (Gemini has token limits)
            var chunks = SplitTextIntoChunks(corruptedText, 2000);
            var correctedChunks = new List<string>();

            foreach (var chunk in chunks)
            {
                var prompt = $@"The following text has been corrupted by OCR (Optical Character Recognition) errors. Please correct it by:

                                1. Fixing obvious character substitutions (1→l, 0→O, 5→S, rn→m, etc.)
                                2. Reconstructing broken words
                                3. Adding proper spacing between words
                                4. Maintaining the original meaning and structure
                                5. Keeping technical terms, file paths, and configuration details accurate

                                Only return the corrected text, no explanations:

                                {chunk}";

                var correctedChunk = await CallGeminiForTextCorrection(prompt);
                if (!string.IsNullOrWhiteSpace(correctedChunk))
                {
                    correctedChunks.Add(correctedChunk);
                }
                else
                {
                    // Fallback to original chunk if AI correction fails
                    correctedChunks.Add(chunk);
                }

                // Add small delay to avoid rate limiting
                await Task.Delay(100);
            }

            var result = string.Join("\n\n", correctedChunks);
            _logger.LogInformation("AI text correction completed. Original: {OriginalLength} chars, Corrected: {CorrectedLength} chars",
                corruptedText.Length, result.Length);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply AI text correction, falling back to original text");
            return corruptedText;
        }
    }

    /// <summary>
    /// Split text into chunks for AI processing
    /// </summary>
    private List<string> SplitTextIntoChunks(string text, int maxChunkSize)
    {
        var chunks = new List<string>();
        var lines = text.Split('\n');
        var currentChunk = new StringBuilder();

        foreach (var line in lines)
        {
            if (currentChunk.Length + line.Length + 1 > maxChunkSize && currentChunk.Length > 0)
            {
                chunks.Add(currentChunk.ToString());
                currentChunk.Clear();
            }

            currentChunk.AppendLine(line);
        }

        if (currentChunk.Length > 0)
        {
            chunks.Add(currentChunk.ToString());
        }

        return chunks;
    }

    /// <summary>
    /// Call Vertex AI Gemini for text correction
    /// </summary>
    private async Task<string> CallGeminiForTextCorrection(string prompt)
    {
        try
        {
            var projectId = _configuration["VertexAI:ProjectId"];
            var location = _configuration["VertexAI:Location"] ?? "us-central1";
            var model = _configuration["VertexAI:GenerativeModel"] ?? "gemini-2.5-flash";

            if (string.IsNullOrEmpty(projectId))
            {
                _logger.LogWarning("VertexAI ProjectId not configured");
                return "";
            }

            // Build the endpoint URL for Vertex AI
            var endpoint = $"https://{location}-aiplatform.googleapis.com/v1/projects/{projectId}/locations/{location}/publishers/google/models/{model}:generateContent";

            using var httpClient = new HttpClient() { Timeout = TimeSpan.FromMinutes(2) };

            // Get access token from gcloud CLI
            var accessToken = await GetGoogleCloudAccessTokenAsync();
            if (string.IsNullOrEmpty(accessToken))
            {
                _logger.LogError("Failed to get Google Cloud access token");
                return "";
            }

            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.1, // Low temperature for consistent corrections
                    maxOutputTokens = 2048,
                    topP = 0.95,
                    topK = 40
                }
            };

            var jsonContent = JsonSerializer.Serialize(requestBody);
            var httpContent = new StringContent(jsonContent, Encoding.UTF8, System.Net.Mime.MediaTypeNames.Application.Json);
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");

            var response = await httpClient.PostAsync(endpoint, httpContent);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var jsonResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);

                if (jsonResponse.TryGetProperty("candidates", out var candidates) &&
                    candidates.GetArrayLength() > 0)
                {
                    var firstCandidate = candidates[0];
                    if (firstCandidate.TryGetProperty("content", out var content) &&
                        content.TryGetProperty("parts", out var parts) &&
                        parts.GetArrayLength() > 0)
                    {
                        var firstPart = parts[0];
                        if (firstPart.TryGetProperty("text", out var textElement))
                        {
                            return textElement.GetString() ?? "";
                        }
                    }
                }
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Vertex AI API error: {StatusCode} - {Error}", response.StatusCode, errorContent);
            }

            return "";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to call Vertex AI for text correction");
            return "";
        }
    }

    /// <summary>
    /// Get Google Cloud access token using gcloud CLI
    /// </summary>
    private async Task<string> GetGoogleCloudAccessTokenAsync()
    {
        try
        {
            // Get gcloud path from configuration or use default Windows installation path
            var gcloudPath = _configuration["VertexAI:GcloudPath"]
                ?? @"C:\Users\ukhan2\AppData\Local\Google\Cloud SDK\google-cloud-sdk\bin\gcloud.cmd";

            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = gcloudPath,
                    Arguments = "auth print-access-token",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var token = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(token))
            {
                _logger.LogInformation("✅ Successfully obtained Google Cloud access token");
                return token.Trim();
            }
            else
            {
                _logger.LogError("Failed to get gcloud access token. Error: {Error}", error);
                return "";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception getting gcloud access token");
            return "";
        }
    }

    /// <summary>
    /// Apply character-level OCR corrections for common misrecognitions
    /// </summary>
    private string ApplyCharacterLevelOcrCorrection(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var result = new StringBuilder(text.Length);
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var word in words)
        {
            var correctedWord = CorrectWordCharacters(word);
            result.Append(correctedWord).Append(' ');
        }

        return result.ToString().TrimEnd();
    }

    /// <summary>
    /// Correct OCR character misrecognitions within individual words
    /// </summary>
    private string CorrectWordCharacters(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return word;

        var corrected = word;

        // Context-aware character corrections
        // Fix '1' to 'l' in the middle of words that look like they should contain 'l'
        corrected = Regex.Replace(corrected, @"([a-zA-Z])1([a-zA-Z])", "$1l$2");
        corrected = Regex.Replace(corrected, @"^1([a-zA-Z]{2,})", "l$1"); // Beginning of word
        corrected = Regex.Replace(corrected, @"([a-zA-Z]{2,})1$", "$1l"); // End of word

        // Fix '0' to 'O' in words that look like they should contain 'O'
        corrected = Regex.Replace(corrected, @"([a-zA-Z])0([a-zA-Z])", "$1O$2");
        corrected = Regex.Replace(corrected, @"^0([a-zA-Z]{2,})", "O$1");

        // Fix '5' to 'S' at the beginning of words
        corrected = Regex.Replace(corrected, @"^5([a-zA-Z]{2,})", "S$1");

        // Fix 'rn' to 'm' in common contexts
        if (corrected.Contains("rn") && !IsIntentionalRn(corrected))
        {
            corrected = corrected.Replace("rn", "m");
        }

        // Fix 'll' that should be 'II' (two capital i's) or '11' (numbers)
        if (Regex.IsMatch(corrected, @"^[A-Z].*ll.*[A-Z]$"))
        {
            corrected = corrected.Replace("ll", "II");
        }

        // Fix '6' to 'G' in appropriate contexts
        corrected = Regex.Replace(corrected, @"^6([a-zA-Z]{2,})", "G$1");

        // Fix '8' to 'B' in appropriate contexts
        corrected = Regex.Replace(corrected, @"^8([a-zA-Z]{2,})", "B$1");

        // Fix common word patterns
        if (corrected.EndsWith("1es"))
            corrected = corrected.Substring(0, corrected.Length - 3) + "les";

        if (corrected.EndsWith("1ed"))
            corrected = corrected.Substring(0, corrected.Length - 3) + "led";

        if (corrected.EndsWith("1ing"))
            corrected = corrected.Substring(0, corrected.Length - 4) + "ling";

        return corrected;
    }

    /// <summary>
    /// Check if 'rn' is intentional (like in words ending with 'ern', 'orn', 'arn')
    /// </summary>
    private bool IsIntentionalRn(string word)
    {
        var intentionalRnPatterns = new[]
        {
            "ern", "orn", "arn", "urn", "irn", "barn", "torn", "worn", "born", "corn", "horn", "learn", "stern", "modern"
        };

        return intentionalRnPatterns.Any(pattern => word.ToLowerInvariant().Contains(pattern));
    }

    /// <summary>
    /// Universal text cleaning method for all document types
    /// </summary>
    private async Task<string> ApplyUniversalTextCleaning(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // Apply OCR noise cleaning (now async for AI-powered correction)
        text = await CleanOcrNoise(text);

        // Apply intelligent text reconstruction
        text = ReconstructText(text);

        // Fix CamelCase / acronym spacing
        text = FixTextSpacing(text);

        // Correct known artefacts that survive the above steps:
        // acronym fragments with stray spaces, brand names split by FixTextSpacing,
        // and missing inter-word gaps in the original PDF stream.
        text = NormalizeExtractedText(text);

        return text;
    }
}