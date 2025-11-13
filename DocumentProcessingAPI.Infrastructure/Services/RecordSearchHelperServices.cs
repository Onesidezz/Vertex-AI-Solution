using DocumentProcessingAPI.Core.DTOs;
using DocumentProcessingAPI.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text;

namespace DocumentProcessingAPI.Infrastructure.Services
{
    /// <summary>
    /// Helper service containing utility methods for record search operations
    /// Includes query processing, date extraction, filtering, and metadata handling
    /// </summary>
    public class RecordSearchHelperServices : IRecordSearchHelperServices
    {
        private readonly ILogger<RecordSearchHelperServices> _logger;

        public RecordSearchHelperServices(ILogger<RecordSearchHelperServices> logger)
        {
            _logger = logger;
        }

        #region Query Processing Methods

        /// <summary>
        /// Clean and normalize the input query for better processing
        /// </summary>
        public string CleanAndNormalizeQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return "";

            // Remove extra whitespace and normalize
            query = System.Text.RegularExpressions.Regex.Replace(query.Trim(), @"\s+", " ");

            // Convert smart quotes to regular quotes
            query = query.Replace(""", "\"").Replace(""", "\"").Replace("'", "'").Replace("'", "'");

            // Normalize common variations
            query = query.Replace(" & ", " and ");
            query = query.Replace(" + ", " and ");

            return query;
        }

        /// <summary>
        /// Universal dynamic keyword extraction for 40TB+ of any data type
        /// Strategy: Extract ANY distinctive tokens that could match exact content
        /// Works for: numbers, IDs, codes, names, technical terms, ANY specific values
        /// </summary>
        public List<string> ExtractSmartKeywords(string query)
        {
            var keywords = new List<string>();

            // STRATEGY: Extract tokens that are SPECIFIC enough to match exact content
            // NOT generic question words - those are for semantic search only

            // 1. QUOTED PHRASES - Highest priority (explicit user intent)
            var quotedPattern = @"""([^""]+)""|'([^']+)'";
            var quoted = System.Text.RegularExpressions.Regex.Matches(query, quotedPattern);
            foreach (System.Text.RegularExpressions.Match match in quoted)
            {
                var phrase = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                if (!string.IsNullOrWhiteSpace(phrase) && phrase.Length >= 2)
                {
                    keywords.Add(phrase.Trim());
                }
            }

            // 2. ANY NUMBERS - All numbers are potentially important identifiers
            // Includes: 11895.77, 142, 2024, phone numbers, IDs, amounts, etc.
            var numberPattern = @"\b\d+(?:[.,]\d+)*\b";
            var numbers = System.Text.RegularExpressions.Regex.Matches(query, numberPattern);
            foreach (System.Text.RegularExpressions.Match match in numbers)
            {
                var num = match.Value;
                // Include numbers with 2+ digits OR decimals (skip single digits like "a", "1")
                if (num.Length >= 2 || num.Contains(".") || num.Contains(","))
                {
                    keywords.Add(num.Replace(",", "")); // Normalize: 11,895.77 → 11895.77
                }
            }

            // 3. ALPHANUMERIC TOKENS - Any mix of letters+numbers is likely a code/ID
            // Matches: INV-00020, UKHAN2, ABC123, PO_2024, REC-001, etc.
            var alphanumericPattern = @"\b[A-Za-z0-9]+[-_./\\][A-Za-z0-9]+(?:[-_./\\][A-Za-z0-9]+)*\b|\b[A-Za-z]+\d+[A-Za-z0-9]*\b|\b\d+[A-Za-z]+[A-Za-z0-9]*\b";
            var alphanumerics = System.Text.RegularExpressions.Regex.Matches(query, alphanumericPattern);
            foreach (System.Text.RegularExpressions.Match match in alphanumerics)
            {
                if (match.Value.Length >= 3) // Minimum length to avoid noise
                {
                    keywords.Add(match.Value);
                }
            }

            // 4. CAPITALIZED WORDS - Likely proper nouns (names, places, brands)
            // Matches: Umar, Khan, Microsoft, NewYork, etc.
            var capitalizedPattern = @"\b[A-Z][a-z]+\b";
            var capitalized = System.Text.RegularExpressions.Regex.Matches(query, capitalizedPattern);

            // Skip common English stop words and question words
            var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Which", "What", "Where", "When", "Why", "How", "Who", "The", "This", "That",
                "These", "Those", "Are", "Is", "Was", "Were", "Be", "Been", "Being",
                "Have", "Has", "Had", "Do", "Does", "Did", "Can", "Could", "Would", "Should",
                "May", "Might", "Must", "Will", "Shall", "In", "On", "At", "To", "For", "From",
                "By", "With", "About", "As", "Of", "Or", "And", "But", "If", "Then", "Than"
            };

            foreach (System.Text.RegularExpressions.Match match in capitalized)
            {
                if (!stopWords.Contains(match.Value) && match.Value.Length >= 3)
                {
                    keywords.Add(match.Value);
                }
            }

            // 5. MULTI-WORD CAPITALIZED PHRASES - Full names, compound proper nouns
            // Matches: "Umar Khan", "New York", "Microsoft Azure", etc.
            var multiWordPattern = @"\b[A-Z][a-z]+(?:\s+[A-Z][a-z]+)+\b";
            var multiWords = System.Text.RegularExpressions.Regex.Matches(query, multiWordPattern);
            foreach (System.Text.RegularExpressions.Match match in multiWords)
            {
                // Split and check each word
                var words = match.Value.Split(' ');
                if (words.All(w => !stopWords.Contains(w)))
                {
                    keywords.Add(match.Value);
                }
            }

            // 6. UNCOMMON/TECHNICAL WORDS - Words that are likely domain-specific
            // These are rare enough that they'll match specific content
            var tokens = query.Split(new[] { ' ', ',', '.', '?', '!', ';', ':' }, StringSplitOptions.RemoveEmptyEntries);

            // Common English words that should NOT be extracted (too generic)
            var genericWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // Question words
                "which", "what", "where", "when", "why", "how", "who", "whose", "whom",
                // Common verbs
                "show", "get", "find", "search", "list", "display", "give", "tell", "fetch",
                "have", "has", "had", "do", "does", "did", "is", "are", "was", "were", "be", "been",
                // Common nouns (too generic for 40TB data)
                "record", "records", "document", "documents", "file", "files", "data", "item", "items",
                // Prepositions
                "in", "on", "at", "to", "for", "from", "with", "by", "about", "of", "the", "a", "an",
                // Conjunctions
                "and", "or", "but", "if", "then", "than", "that", "this", "these", "those",
                // Others
                "all", "some", "any", "me", "my", "mine", "you", "your", "yours"
            };

            foreach (var token in tokens)
            {
                var cleanToken = token.Trim().ToLowerInvariant();

                // Extract if:
                // - Length >= 4 (substantial word)
                // - NOT a generic word
                // - Contains letters (not pure symbol)
                if (cleanToken.Length >= 4 &&
                    !genericWords.Contains(cleanToken) &&
                    cleanToken.Any(char.IsLetter))
                {
                    keywords.Add(token.Trim());
                }
            }

            // 7. SPECIAL CHARACTERS PRESERVED - Technical codes with symbols
            // Matches: @username, #hashtag, version:1.2.3, file.ext, etc.
            var specialPattern = @"\b[A-Za-z0-9]+[@#:./\\][A-Za-z0-9.]+\b";
            var specials = System.Text.RegularExpressions.Regex.Matches(query, specialPattern);
            foreach (System.Text.RegularExpressions.Match match in specials)
            {
                keywords.Add(match.Value);
            }

            // Remove duplicates and filter out redundant single words that are part of phrases
            var uniqueKeywords = new List<string>();
            var keywordsLower = keywords.Select(k => k.ToLowerInvariant()).ToList();

            foreach (var keyword in keywords)
            {
                var keywordLower = keyword.ToLowerInvariant();

                // Skip if this keyword is already added (case-insensitive duplicate)
                if (uniqueKeywords.Any(uk => uk.ToLowerInvariant() == keywordLower))
                    continue;

                // Skip if this single word is part of a longer phrase in the list
                // Example: Skip "Pending" if "Pending Amount" exists
                var isSingleWord = !keyword.Contains(" ");
                if (isSingleWord)
                {
                    var isPartOfPhrase = keywordsLower.Any(k =>
                        k != keywordLower &&
                        k.Contains(" ") &&
                        k.Contains(keywordLower));

                    if (isPartOfPhrase)
                    {
                        // This single word is redundant - skip it
                        continue;
                    }
                }

                // Add if minimum length met
                if (keyword.Length >= 2)
                {
                    uniqueKeywords.Add(keyword);
                }
            }

            return uniqueKeywords;
        }

        /// <summary>
        /// Legacy method - kept for backward compatibility
        /// Use ExtractSmartKeywords instead
        /// </summary>
        public List<string> ExtractKeywordsAndNumbers(string query)
        {
            var keywords = new List<string>();

            // Extract numbers (including decimals, currency)
            var numberPattern = @"\$?[\d,]+\.?\d*";
            var numbers = System.Text.RegularExpressions.Regex.Matches(query, numberPattern);
            foreach (System.Text.RegularExpressions.Match match in numbers)
            {
                var cleanNumber = match.Value.Replace(",", "").Replace("$", "");
                if (!string.IsNullOrWhiteSpace(cleanNumber) && cleanNumber.Length > 0)
                {
                    keywords.Add(cleanNumber);
                }
            }

            // Extract quoted phrases
            var quotedPattern = @"""([^""]+)""";
            var quoted = System.Text.RegularExpressions.Regex.Matches(query, quotedPattern);
            foreach (System.Text.RegularExpressions.Match match in quoted)
            {
                if (!string.IsNullOrWhiteSpace(match.Groups[1].Value))
                {
                    keywords.Add(match.Groups[1].Value);
                }
            }

            return keywords;
        }

        /// <summary>
        /// Remove common query words that don't add value to CM index search
        /// </summary>
        public string RemoveCommonQueryWords(string query)
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

        #region Date/Time Extraction Methods

        /// <summary>
        /// Extract date or date range from natural language query with comprehensive support
        /// Now supports date ranges for terms like "recently", "last week", etc.
        /// Also supports "after", "before", "earliest", "latest", and file type filtering
        /// Returns (startDate, endDate) - both can be null if no date found
        /// </summary>
        public (DateTime? startDate, DateTime? endDate) ExtractDateRangeFromQuery(string query)
        {
            var lowerQuery = query.ToLowerInvariant();
            var now = DateTime.Now.Date;

            // Handle "earliest" or "oldest" - return a very early date to get all records, then we'll sort
            if (lowerQuery.Contains("earliest") || lowerQuery.Contains("oldest"))
            {
                _logger.LogInformation("Date filter: earliest/oldest - will sort by creation date ascending");
                // Return a very early date to include all records
                return (new DateTime(1900, 1, 1), DateTime.MaxValue);
            }

            // Handle "latest" or "newest" or "most recent" - return recent date range
            if (lowerQuery.Contains("latest") || lowerQuery.Contains("newest") || lowerQuery.Contains("most recent"))
            {
                // For "most recently created", get last 30 days but we'll sort by creation date descending
                var startDate = now.AddDays(-30);
                _logger.LogInformation("Date range filter: latest/newest = {StartDate} to {EndDate}", startDate, now);
                return (startDate, now);
            }

            // Handle "after" date queries (e.g., "after October 9, 2025", "created after 10/9/2025")
            var afterMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery, @"after\s+(.+?)(?:\s|$)");
            if (afterMatch.Success)
            {
                var dateString = afterMatch.Groups[1].Value.Trim();
                var parsedDate = ParseDateFromString(dateString, now);
                if (parsedDate.HasValue)
                {
                    _logger.LogInformation("Date filter: after {Date}", parsedDate.Value);
                    return (parsedDate.Value.AddDays(1), DateTime.MaxValue); // Start from day after
                }
            }

            // Handle "before" date queries (e.g., "before October 9, 2025", "created before 10/9/2025")
            var beforeMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery, @"before\s+(.+?)(?:\s|$)");
            if (beforeMatch.Success)
            {
                var dateString = beforeMatch.Groups[1].Value.Trim();
                var parsedDate = ParseDateFromString(dateString, now);
                if (parsedDate.HasValue)
                {
                    _logger.LogInformation("Date filter: before {Date}", parsedDate.Value);
                    return (new DateTime(1900, 1, 1), parsedDate.Value.AddDays(-1)); // End day before
                }
            }

            // Handle "between" date queries with enhanced support
            // Pattern 1: "between X and Y on [date]" - time range on specific date
            var betweenTimeOnDateMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery,
                @"between\s+(\d{1,2})\s*(am|pm)?\s+and\s+(\d{1,2})\s*(am|pm)?\s+on\s+(.+?)(?:\s|$)");
            if (betweenTimeOnDateMatch.Success)
            {
                var startHour = int.Parse(betweenTimeOnDateMatch.Groups[1].Value);
                var startAmPm = betweenTimeOnDateMatch.Groups[2].Value.ToLowerInvariant();
                var endHour = int.Parse(betweenTimeOnDateMatch.Groups[3].Value);
                var endAmPm = betweenTimeOnDateMatch.Groups[4].Value.ToLowerInvariant();
                var dateString = betweenTimeOnDateMatch.Groups[5].Value.Trim();

                // Convert to 24-hour format
                if (startAmPm == "pm" && startHour != 12) startHour += 12;
                if (startAmPm == "am" && startHour == 12) startHour = 0;
                if (endAmPm == "pm" && endHour != 12) endHour += 12;
                if (endAmPm == "am" && endHour == 12) endHour = 0;

                var baseDate = ParseDateFromString(dateString, now);
                if (baseDate.HasValue)
                {
                    var startDateTime = baseDate.Value.Date.AddHours(startHour);
                    var endDateTime = baseDate.Value.Date.AddHours(endHour);
                    _logger.LogInformation("Time range filter: between {StartTime} and {EndTime} on {Date}",
                        startDateTime, endDateTime, baseDate.Value.Date);
                    return (startDateTime, endDateTime);
                }
            }

            // Pattern 2: "between [month year] and [month year]" - month range
            var monthNamesForBetween = new Dictionary<string, int>
            {
                { "january", 1 }, { "jan", 1 },
                { "february", 2 }, { "feb", 2 },
                { "march", 3 }, { "mar", 3 },
                { "april", 4 }, { "apr", 4 },
                { "may", 5 },
                { "june", 6 }, { "jun", 6 },
                { "july", 7 }, { "jul", 7 },
                { "august", 8 }, { "aug", 8 },
                { "september", 9 }, { "sep", 9 }, { "sept", 9 },
                { "october", 10 }, { "oct", 10 },
                { "november", 11 }, { "nov", 11 },
                { "december", 12 }, { "dec", 12 }
            };

            // Build month pattern string
            var monthPattern = string.Join("|", monthNamesForBetween.Keys);

            // Pattern: "between January 2025 and March 2025" or "from Jan 2025 to Mar 2025"
            var betweenMonthsMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery,
                $@"(?:between|from)\s+({monthPattern})\s+(\d{{4}})\s+(?:and|to)\s+({monthPattern})\s+(\d{{4}})");
            if (betweenMonthsMatch.Success)
            {
                var startMonth = monthNamesForBetween[betweenMonthsMatch.Groups[1].Value];
                var startYear = int.Parse(betweenMonthsMatch.Groups[2].Value);
                var endMonth = monthNamesForBetween[betweenMonthsMatch.Groups[3].Value];
                var endYear = int.Parse(betweenMonthsMatch.Groups[4].Value);

                var startDate = new DateTime(startYear, startMonth, 1);
                var endDate = new DateTime(endYear, endMonth, DateTime.DaysInMonth(endYear, endMonth));

                _logger.LogInformation("Month range filter: between {StartMonth} {StartYear} and {EndMonth} {EndYear}",
                    betweenMonthsMatch.Groups[1].Value, startYear, betweenMonthsMatch.Groups[3].Value, endYear);
                return (startDate, endDate);
            }

            // Pattern 3: General "between X and Y" - handles various date formats
            // Enhanced to support multiple date format patterns with explicit date pattern matching
            var betweenPatterns = new[]
            {
                // Match explicit date patterns: DD/MM/YYYY or DD-MM-YYYY
                @"between\s+(\d{1,2}[/-]\d{1,2}[/-]\d{4})\s+(?:and|to)\s+(\d{1,2}[/-]\d{1,2}[/-]\d{4})",
                @"from\s+(\d{1,2}[/-]\d{1,2}[/-]\d{4})\s+to\s+(\d{1,2}[/-]\d{1,2}[/-]\d{4})",
                // Match month names: "January 2025 to March 2025"
                @"between\s+([a-z]+\s+\d{1,2},?\s+\d{4})\s+(?:and|to)\s+([a-z]+\s+\d{1,2},?\s+\d{4})",
                @"from\s+([a-z]+\s+\d{1,2},?\s+\d{4})\s+to\s+([a-z]+\s+\d{1,2},?\s+\d{4})"
            };

            foreach (var pattern in betweenPatterns)
            {
                var betweenMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery, pattern);
                if (betweenMatch.Success)
                {
                    var startDateString = betweenMatch.Groups[1].Value.Trim();
                    var endDateString = betweenMatch.Groups[2].Value.Trim();

                    _logger.LogInformation("   Pattern matched - Start: '{StartDateString}', End: '{EndDateString}'",
                        startDateString, endDateString);

                    // Try to parse with enhanced parsing that handles all formats
                    var parsedStartDate = ParseDateFromString(startDateString, now);
                    var parsedEndDate = ParseDateFromString(endDateString, now);

                    _logger.LogInformation("   Parsed - Start: {StartDate}, End: {EndDate}",
                        parsedStartDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "FAILED",
                        parsedEndDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "FAILED");

                    if (parsedStartDate.HasValue && parsedEndDate.HasValue)
                    {
                        // For date range queries, include the full end date (end of day)
                        var adjustedEndDate = parsedEndDate.Value.Date.AddDays(1).AddSeconds(-1);
                        _logger.LogInformation("Date range filter: from {StartDate} to {EndDate} (adjusted to end of day)",
                            parsedStartDate.Value.ToString("yyyy-MM-dd HH:mm:ss"),
                            adjustedEndDate.ToString("yyyy-MM-dd HH:mm:ss"));
                        return (parsedStartDate.Value, adjustedEndDate);
                    }
                    else
                    {
                        _logger.LogWarning("Failed to parse one or both dates, trying next pattern");
                    }
                }
            }

            // Handle "around" time queries (e.g., "around noon", "around 3 PM", "around midnight")
            var aroundTimeResult = ExtractAroundTimeFilter(lowerQuery, now);
            if (aroundTimeResult.HasValue)
            {
                return aroundTimeResult.Value;
            }

            // Handle "at [time]" queries (e.g., "at 10:39", "created at 15:30", "at 3:45 PM")
            var atTimeMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery, @"(?:at|created\s+at)\s+(\d{1,2}):?(\d{2})?\s*(am|pm)?");
            if (atTimeMatch.Success)
            {
                var hour = int.Parse(atTimeMatch.Groups[1].Value);
                var minute = atTimeMatch.Groups[2].Success ? int.Parse(atTimeMatch.Groups[2].Value) : 0;
                var ampm = atTimeMatch.Groups[3].Success ? atTimeMatch.Groups[3].Value.ToLowerInvariant() : "";

                // Convert to 24-hour format if AM/PM specified
                if (!string.IsNullOrEmpty(ampm))
                {
                    if (ampm == "pm" && hour != 12) hour += 12;
                    if (ampm == "am" && hour == 12) hour = 0;
                }

                // Create a time window: exact time ±1 minute
                var targetTime = now.Date.AddHours(hour).AddMinutes(minute);
                var startTime = targetTime.AddMinutes(-1);
                var endTime = targetTime.AddMinutes(1);
                _logger.LogInformation("Time filter: at {TargetTime} = {StartTime} to {EndTime}",
                    targetTime.ToString("HH:mm"), startTime, endTime);
                return (startTime, endTime);
            }

            // Handle time-based queries (e.g., "after 3:45 PM", "added after 15:30")
            var timeAfterMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery, @"after\s+(\d{1,2}):(\d{2})\s*(am|pm)?");
            if (timeAfterMatch.Success)
            {
                var hour = int.Parse(timeAfterMatch.Groups[1].Value);
                var minute = int.Parse(timeAfterMatch.Groups[2].Value);
                var ampm = timeAfterMatch.Groups[3].Value.ToLowerInvariant();

                // Convert to 24-hour format
                if (ampm == "pm" && hour != 12) hour += 12;
                if (ampm == "am" && hour == 12) hour = 0;

                var timeThreshold = now.Date.AddHours(hour).AddMinutes(minute);
                _logger.LogInformation("Time filter: after {Time}", timeThreshold);
                return (timeThreshold, DateTime.MaxValue);
            }

            // 1. Check for "today"
            if (lowerQuery.Contains("today"))
            {
                _logger.LogInformation("Date filter: today = {Date}", now);
                return (now, now);
            }

            // 2. Check for "recently" or "recent" (last 14 days)
            if (lowerQuery.Contains("recently") || lowerQuery.Contains("recent"))
            {
                var startDate = now.AddDays(-14);
                _logger.LogInformation("Date range filter: recently = {StartDate} to {EndDate}", startDate, now);
                return (startDate, now);
            }

            // 3. Check for "yesterday"
            if (lowerQuery.Contains("yesterday") && !lowerQuery.Contains("before yesterday"))
            {
                var yesterday = now.AddDays(-1);
                _logger.LogInformation("Date filter: yesterday = {Date} ({DateString}). Current date: {Now}",
                    yesterday, yesterday.ToString("yyyy-MM-dd"), now.ToString("yyyy-MM-dd"));
                return (yesterday, yesterday);
            }

            // 4. Check for "day before yesterday"
            if (lowerQuery.Contains("day before yesterday") || lowerQuery.Contains("before yesterday"))
            {
                var dayBeforeYesterday = now.AddDays(-2);
                _logger.LogInformation("Date filter: day before yesterday = {Date}", dayBeforeYesterday);
                return (dayBeforeYesterday, dayBeforeYesterday);
            }

            // 5. Check for "X days ago" (e.g., "3 days ago", "5 days ago") - treat as single day
            var daysAgoMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery, @"(\d+)\s*days?\s*ago");
            if (daysAgoMatch.Success)
            {
                var daysAgo = int.Parse(daysAgoMatch.Groups[1].Value);
                var date = now.AddDays(-daysAgo);
                _logger.LogInformation("Date filter: {Days} days ago = {Date}", daysAgo, date);
                return (date, date);
            }

            // 6. Check for "last X days" (e.g., "last 7 days", "last 30 days") - treat as range
            var lastDaysMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery, @"last\s+(\d+)\s*days?");
            if (lastDaysMatch.Success)
            {
                var days = int.Parse(lastDaysMatch.Groups[1].Value);
                var startDate = now.AddDays(-days);
                _logger.LogInformation("Date range filter: last {Days} days = {StartDate} to {EndDate}", days, startDate, now);
                return (startDate, now);
            }

            // 7. Check for "last week" (previous calendar week: Monday to Sunday)
            if (lowerQuery.Contains("last week"))
            {
                // Get current day of week (Monday = 1, Sunday = 0)
                var currentDayOfWeek = (int)now.DayOfWeek;
                if (currentDayOfWeek == 0) currentDayOfWeek = 7; // Sunday = 7

                // Calculate days to go back to last Monday
                var daysToLastMonday = currentDayOfWeek + 6; // Go back to previous week's Monday
                var lastMonday = now.AddDays(-daysToLastMonday).Date;
                var lastSunday = lastMonday.AddDays(6).Date.AddHours(23).AddMinutes(59).AddSeconds(59);

                _logger.LogInformation("Date range filter: last week = {StartDate} to {EndDate}", lastMonday, lastSunday);
                return (lastMonday, lastSunday);
            }

            // 8. Check for "this week" (from Monday to today)
            if (lowerQuery.Contains("this week"))
            {
                var daysSinceMonday = (int)now.DayOfWeek - 1;
                if (daysSinceMonday < 0) daysSinceMonday = 6; // Sunday
                var startOfWeek = now.AddDays(-daysSinceMonday);
                _logger.LogInformation("Date range filter: this week = {StartDate} to {EndDate}", startOfWeek, now);
                return (startOfWeek, now);
            }

            // 9. Check for "last month" (previous calendar month)
            if (lowerQuery.Contains("last month"))
            {
                // Get first day of last month
                var firstDayOfCurrentMonth = new DateTime(now.Year, now.Month, 1);
                var firstDayOfLastMonth = firstDayOfCurrentMonth.AddMonths(-1);

                // Get last day of last month
                var lastDayOfLastMonth = firstDayOfCurrentMonth.AddDays(-1).Date.AddHours(23).AddMinutes(59).AddSeconds(59);

                _logger.LogInformation("Date range filter: last month = {StartDate} to {EndDate}", firstDayOfLastMonth, lastDayOfLastMonth);
                return (firstDayOfLastMonth, lastDayOfLastMonth);
            }

            // 10. Check for "this month" (from 1st to today)
            if (lowerQuery.Contains("this month"))
            {
                var startOfMonth = new DateTime(now.Year, now.Month, 1);
                _logger.LogInformation("Date range filter: this month = {StartDate} to {EndDate}", startOfMonth, now);
                return (startOfMonth, now);
            }

            // 10a. Check for "last X weeks" (e.g., "last 2 weeks", "last 4 weeks")
            var lastWeeksMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery, @"last\s+(\d+)\s*weeks?");
            if (lastWeeksMatch.Success)
            {
                var weeks = int.Parse(lastWeeksMatch.Groups[1].Value);
                var startDate = now.AddDays(-weeks * 7);
                _logger.LogInformation("Date range filter: last {Weeks} weeks = {StartDate} to {EndDate}", weeks, startDate, now);
                return (startDate, now);
            }

            // 10b. Check for "last X months" (e.g., "last 3 months", "last 6 months")
            var lastMonthsMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery, @"last\s+(\d+)\s*months?");
            if (lastMonthsMatch.Success)
            {
                var months = int.Parse(lastMonthsMatch.Groups[1].Value);
                var startDate = now.AddMonths(-months);
                _logger.LogInformation("Date range filter: last {Months} months = {StartDate} to {EndDate}", months, startDate, now);
                return (startDate, now);
            }

            // 10c. Check for "last X years" (e.g., "last 2 years")
            var lastYearsMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery, @"last\s+(\d+)\s*years?");
            if (lastYearsMatch.Success)
            {
                var years = int.Parse(lastYearsMatch.Groups[1].Value);
                var startDate = now.AddYears(-years);
                _logger.LogInformation("Date range filter: last {Years} years = {StartDate} to {EndDate}", years, startDate, now);
                return (startDate, now);
            }

            // 10d. Check for "this year" (from Jan 1st to today)
            if (lowerQuery.Contains("this year"))
            {
                var startOfYear = new DateTime(now.Year, 1, 1);
                _logger.LogInformation("Date range filter: this year = {StartDate} to {EndDate}", startOfYear, now);
                return (startOfYear, now);
            }

            // 10e. Check for "last year" (entire previous year)
            if (lowerQuery.Contains("last year"))
            {
                var lastYear = now.Year - 1;
                var startOfLastYear = new DateTime(lastYear, 1, 1);
                var endOfLastYear = new DateTime(lastYear, 12, 31);
                _logger.LogInformation("Date range filter: last year = {StartDate} to {EndDate}", startOfLastYear, endOfLastYear);
                return (startOfLastYear, endOfLastYear);
            }

            // 10f. Check for specific year (e.g., "2024", "year 2023", "in 2022")
            // IMPORTANT: Use negative lookbehind and lookahead to avoid matching years that are part of dates
            // This prevents "09-10-2025" from being interpreted as "year 2025"
            // Also prevents matching years that follow month names (e.g., "September 2025" should be handled by month+year pattern)
            var yearMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery, @"(?<![\d-/])(?<!january\s)(?<!jan\s)(?<!february\s)(?<!feb\s)(?<!march\s)(?<!mar\s)(?<!april\s)(?<!apr\s)(?<!may\s)(?<!june\s)(?<!jun\s)(?<!july\s)(?<!jul\s)(?<!august\s)(?<!aug\s)(?<!september\s)(?<!sep\s)(?<!sept\s)(?<!october\s)(?<!oct\s)(?<!november\s)(?<!nov\s)(?<!december\s)(?<!dec\s)(?:year\s+|in\s+)?(\d{4})(?![-/\d])(?:\s|$|,)");
            if (yearMatch.Success)
            {
                var year = int.Parse(yearMatch.Groups[1].Value);
                if (year >= 1900 && year <= now.Year + 10) // Reasonable year range
                {
                    var startOfYear = new DateTime(year, 1, 1);
                    var endOfYear = new DateTime(year, 12, 31);
                    _logger.LogInformation("Date range filter: year {Year} = {StartDate} to {EndDate}", year, startOfYear, endOfYear);
                    return (startOfYear, endOfYear);
                }
            }

            // 10g. Check for quarters (e.g., "Q1 2024", "Q2", "first quarter 2023")
            var quarterMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery, @"(?:q|quarter)\s*([1-4])(?:\s+(\d{4}))?");
            if (quarterMatch.Success)
            {
                var quarter = int.Parse(quarterMatch.Groups[1].Value);
                var year = quarterMatch.Groups[2].Success ? int.Parse(quarterMatch.Groups[2].Value) : now.Year;

                var startMonth = (quarter - 1) * 3 + 1;
                var startDate = new DateTime(year, startMonth, 1);
                var endDate = startDate.AddMonths(3).AddDays(-1);

                _logger.LogInformation("Date range filter: Q{Quarter} {Year} = {StartDate} to {EndDate}", quarter, year, startDate, endDate);
                return (startDate, endDate);
            }

            // 10h. Check for "first quarter", "second quarter", etc.
            if (lowerQuery.Contains("first quarter"))
            {
                var startDate = new DateTime(now.Year, 1, 1);
                var endDate = new DateTime(now.Year, 3, 31);
                _logger.LogInformation("Date range filter: first quarter = {StartDate} to {EndDate}", startDate, endDate);
                return (startDate, endDate);
            }
            if (lowerQuery.Contains("second quarter"))
            {
                var startDate = new DateTime(now.Year, 4, 1);
                var endDate = new DateTime(now.Year, 6, 30);
                _logger.LogInformation("Date range filter: second quarter = {StartDate} to {EndDate}", startDate, endDate);
                return (startDate, endDate);
            }
            if (lowerQuery.Contains("third quarter"))
            {
                var startDate = new DateTime(now.Year, 7, 1);
                var endDate = new DateTime(now.Year, 9, 30);
                _logger.LogInformation("Date range filter: third quarter = {StartDate} to {EndDate}", startDate, endDate);
                return (startDate, endDate);
            }
            if (lowerQuery.Contains("fourth quarter"))
            {
                var startDate = new DateTime(now.Year, 10, 1);
                var endDate = new DateTime(now.Year, 12, 31);
                _logger.LogInformation("Date range filter: fourth quarter = {StartDate} to {EndDate}", startDate, endDate);
                return (startDate, endDate);
            }

            // 10i. Check for specific weeks (e.g., "week 1", "week 42 of 2024")
            var weekMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery, @"week\s+(\d+)(?:\s+of\s+(\d{4}))?");
            if (weekMatch.Success)
            {
                var weekNumber = int.Parse(weekMatch.Groups[1].Value);
                var year = weekMatch.Groups[2].Success ? int.Parse(weekMatch.Groups[2].Value) : now.Year;

                if (weekNumber >= 1 && weekNumber <= 53)
                {
                    var jan1 = new DateTime(year, 1, 1);
                    var daysOffset = (weekNumber - 1) * 7;
                    var startDate = jan1.AddDays(daysOffset - (int)jan1.DayOfWeek);
                    var endDate = startDate.AddDays(6);

                    _logger.LogInformation("Date range filter: week {Week} of {Year} = {StartDate} to {EndDate}", weekNumber, year, startDate, endDate);
                    return (startDate, endDate);
                }
            }

            // 10j. Check for "week of [date]" (e.g., "week of October 3")
            var weekOfMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery, @"week\s+of\s+(.+)");
            if (weekOfMatch.Success)
            {
                var dateString = weekOfMatch.Groups[1].Value.Trim();
                var parsedDate = ParseDateFromString(dateString, now);
                if (parsedDate.HasValue)
                {
                    var dayOfWeek = (int)parsedDate.Value.DayOfWeek;
                    var startOfWeek = parsedDate.Value.AddDays(-dayOfWeek); // Start on Sunday
                    var endOfWeek = startOfWeek.AddDays(6);

                    _logger.LogInformation("Date range filter: week of {Date} = {StartDate} to {EndDate}", parsedDate.Value, startOfWeek, endOfWeek);
                    return (startOfWeek, endOfWeek);
                }
            }

            // 10k. Check for specific month and year (e.g., "October 2024", "Jan 2023", "last October")
            var monthNames = new Dictionary<string, int>
            {
                { "january", 1 }, { "jan", 1 },
                { "february", 2 }, { "feb", 2 },
                { "march", 3 }, { "mar", 3 },
                { "april", 4 }, { "apr", 4 },
                { "may", 5 },
                { "june", 6 }, { "jun", 6 },
                { "july", 7 }, { "jul", 7 },
                { "august", 8 }, { "aug", 8 },
                { "september", 9 }, { "sep", 9 }, { "sept", 9 },
                { "october", 10 }, { "oct", 10 },
                { "november", 11 }, { "nov", 11 },
                { "december", 12 }, { "dec", 12 }
            };

            // Check for "last [month]" (e.g., "last October", "last Jan")
            foreach (var monthKvp in monthNames)
            {
                var monthName = monthKvp.Key;
                var monthNum = monthKvp.Value;
                var pattern = $@"last\s+{monthName}";
                var match = System.Text.RegularExpressions.Regex.Match(lowerQuery, pattern);
                if (match.Success)
                {
                    // Find the most recent occurrence of this month
                    var targetYear = now.Year;
                    if (now.Month < monthNum)
                    {
                        targetYear = now.Year - 1; // Last year if the month hasn't occurred this year yet
                    }
                    else if (now.Month == monthNum && now.Day < 15)
                    {
                        targetYear = now.Year - 1; // If we're early in the current month, "last October" means last year
                    }

                    var startDate = new DateTime(targetYear, monthNum, 1);
                    var endDate = new DateTime(targetYear, monthNum, DateTime.DaysInMonth(targetYear, monthNum), 23, 59, 59);
                    _logger.LogInformation("Date range filter: last {Month} = {StartDate} to {EndDate}", monthName, startDate, endDate);
                    return (startDate, endDate);
                }
            }

            // Check for specific month with optional year (e.g., "October 2024", "Jan 2023", "September")
            foreach (var monthKvp in monthNames)
            {
                var monthName = monthKvp.Key;
                var monthNum = monthKvp.Value;

                // Pattern: "October 2024" or "Oct 2024"
                var monthYearPattern = $@"{monthName}\s+(\d{{4}})";
                var monthYearMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery, monthYearPattern);
                if (monthYearMatch.Success)
                {
                    var year = int.Parse(monthYearMatch.Groups[1].Value);
                    var startDate = new DateTime(year, monthNum, 1);
                    var endDate = new DateTime(year, monthNum, DateTime.DaysInMonth(year, monthNum), 23, 59, 59);
                    _logger.LogInformation("Date range filter: {Month} {Year} = {StartDate} to {EndDate}", monthName, year, startDate, endDate);
                    return (startDate, endDate);
                }

                // Pattern: just month name without year (e.g., "October", "September") - use current year
                // But avoid matching if it's part of a date like "October 3"
                var justMonthPattern = $@"\b{monthName}\b(?!\s+\d{{1,2}})";
                var justMonthMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery, justMonthPattern);
                if (justMonthMatch.Success && !lowerQuery.Contains("last " + monthName))
                {
                    var year = now.Year;
                    var startDate = new DateTime(year, monthNum, 1);
                    var endDate = new DateTime(year, monthNum, DateTime.DaysInMonth(year, monthNum), 23, 59, 59);
                    _logger.LogInformation("Date range filter: {Month} (current year) = {StartDate} to {EndDate}", monthName, startDate, endDate);
                    return (startDate, endDate);
                }
            }

            // 11. Check for month names with day (e.g., "oct 3", "october 10", "3rd october", "10 oct")
            foreach (var monthKvp in monthNames)
            {
                var monthName = monthKvp.Key;
                var monthNum = monthKvp.Value;

                // Pattern: "oct 3", "october 10"
                var pattern1 = $@"{monthName}\s+(\d{{1,2}})(?:st|nd|rd|th)?(?:,?\s*(\d{{4}}))?";
                var match1 = System.Text.RegularExpressions.Regex.Match(lowerQuery, pattern1);
                if (match1.Success)
                {
                    var day = int.Parse(match1.Groups[1].Value);
                    var year = match1.Groups[2].Success ? int.Parse(match1.Groups[2].Value) : now.Year;
                    var date = new DateTime(year, monthNum, day);
                    _logger.LogInformation("Date filter: {Month} {Day}, {Year} = {Date}", monthName, day, year, date);
                    return (date, date);
                }

                // Pattern: "3rd october", "10 oct"
                var pattern2 = $@"(\d{{1,2}})(?:st|nd|rd|th)?\s+{monthName}(?:,?\s*(\d{{4}}))?";
                var match2 = System.Text.RegularExpressions.Regex.Match(lowerQuery, pattern2);
                if (match2.Success)
                {
                    var day = int.Parse(match2.Groups[1].Value);
                    var year = match2.Groups[2].Success ? int.Parse(match2.Groups[2].Value) : now.Year;
                    var date = new DateTime(year, monthNum, day);
                    _logger.LogInformation("Date filter: {Day} {Month}, {Year} = {Date}", day, monthName, year, date);
                    return (date, date);
                }
            }

            // 12. Try to extract specific dates (dd-mm-yyyy, dd/mm/yyyy, yyyy-mm-dd formats)
            // IMPORTANT: This handles ambiguous dates like "09-10-2025" by trying both interpretations
            var datePatterns = new[]
            {
                @"\b(\d{1,2})[-/](\d{1,2})[-/](\d{4})\b", // dd-mm-yyyy or mm-dd-yyyy
                @"\b(\d{4})[-/](\d{1,2})[-/](\d{1,2})\b"  // yyyy-mm-dd
            };

            foreach (var pattern in datePatterns)
            {
                var match = System.Text.RegularExpressions.Regex.Match(query, pattern);
                if (match.Success)
                {
                    try
                    {
                        var num1 = int.Parse(match.Groups[1].Value);
                        var num2 = int.Parse(match.Groups[2].Value);
                        var num3 = int.Parse(match.Groups[3].Value);

                        // Check if it's yyyy-mm-dd format (year is > 31)
                        if (num1 > 31)
                        {
                            var year = num1;
                            var month = num2;
                            var day = num3;

                            if (month >= 1 && month <= 12 && day >= 1 && day <= 31)
                            {
                                var date = new DateTime(year, month, day);
                                _logger.LogInformation("Date filter: specific date (yyyy-mm-dd) = {Date} ({DateString})", date, date.ToString("yyyy-MM-dd"));
                                return (date, date);
                            }
                        }
                        // For ambiguous dates (e.g., 09-10-2025), try both dd-mm-yyyy and mm-dd-yyyy
                        else
                        {
                            DateTime? parsedDate = null;
                            string parsedFormat = "";

                            // Try dd-mm-yyyy format first (European: day-month-year)
                            if (num1 >= 1 && num1 <= 31 && num2 >= 1 && num2 <= 12)
                            {
                                try
                                {
                                    parsedDate = new DateTime(num3, num2, num1);
                                    parsedFormat = "dd-MM-yyyy";
                                    _logger.LogInformation("Date filter: parsed as dd-MM-yyyy = {Date} ({DateString})", parsedDate, parsedDate.Value.ToString("yyyy-MM-dd"));
                                }
                                catch
                                {
                                    // Invalid date for this format, try next
                                }
                            }

                            // If dd-mm-yyyy didn't work or is ambiguous, try mm-dd-yyyy (American: month-day-year)
                            if (!parsedDate.HasValue && num1 >= 1 && num1 <= 12 && num2 >= 1 && num2 <= 31)
                            {
                                try
                                {
                                    parsedDate = new DateTime(num3, num1, num2);
                                    parsedFormat = "MM-dd-yyyy";
                                    _logger.LogInformation("Date filter: parsed as MM-dd-yyyy = {Date} ({DateString})", parsedDate, parsedDate.Value.ToString("yyyy-MM-dd"));
                                }
                                catch
                                {
                                    // Invalid date for this format
                                }
                            }

                            if (parsedDate.HasValue)
                            {
                                _logger.LogInformation("Successfully parsed date: {Date} using format {Format}",
                                    parsedDate.Value.ToString("yyyy-MM-dd"), parsedFormat);
                                return (parsedDate.Value, parsedDate.Value);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse date from pattern: {Pattern}", match.Value);
                    }
                }
            }

            return (null, null);
        }

        /// <summary>
        /// Parse date from natural language string with support for all common date formats
        /// Supports: MM/DD/YYYY, DD/MM/YYYY, YYYY-MM-DD, ISO 8601, compact formats, month names, etc.
        ///
        /// Supported formats:
        /// - ISO 8601: 2025-10-16, 2025-10-16T14:30:00, 2025-10-16T14:30:00Z
        /// - US format: 10/16/2025, 10-16-2025
        /// - UK/European format: 16/10/2025, 16-10-2025, 16.10.2025
        /// - Full date: Thursday, October 16, 2025
        /// - Short month: 16 Oct 2025, Oct 16 2025
        /// - Abbreviated: 16-Oct-2025 2:30 PM, 16-Oct-2025
        /// - Compact: 20251016, 20251016143000
        /// - Short year: 16/10/25, 16-10-25
        /// </summary>
        public DateTime? ParseDateFromString(string dateString, DateTime referenceDate)
        {
            if (string.IsNullOrWhiteSpace(dateString))
                return null;

            var originalString = dateString;
            dateString = dateString.Trim().ToLowerInvariant();

            // Handle month names with day and year
            var monthNames = new Dictionary<string, int>
            {
                { "january", 1 }, { "jan", 1 },
                { "february", 2 }, { "feb", 2 },
                { "march", 3 }, { "mar", 3 },
                { "april", 4 }, { "apr", 4 },
                { "may", 5 },
                { "june", 6 }, { "jun", 6 },
                { "july", 7 }, { "jul", 7 },
                { "august", 8 }, { "aug", 8 },
                { "september", 9 }, { "sep", 9 }, { "sept", 9 },
                { "october", 10 }, { "oct", 10 },
                { "november", 11 }, { "nov", 11 },
                { "december", 12 }, { "dec", 12 }
            };

            // 1. Try ISO 8601 formats first (highest priority for unambiguous dates)
            // Format: 2025-10-16T14:30:00Z or 2025-10-16T14:30:00 or 2025-10-16
            var iso8601Formats = new[]
            {
                "yyyy-MM-ddTHH:mm:ssZ",
                "yyyy-MM-ddTHH:mm:ss",
                "yyyy-MM-ddTHH:mm:ss.fffZ",
                "yyyy-MM-ddTHH:mm:ss.fff",
                "yyyy-MM-dd HH:mm:ss",
                "yyyy-MM-dd",
                "yyyy/MM/dd"
            };

            foreach (var format in iso8601Formats)
            {
                if (DateTime.TryParseExact(originalString, format,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var parsedDate))
                {
                    _logger.LogDebug("Parsed date '{DateString}' using ISO 8601 format '{Format}' -> {ParsedDate}",
                        originalString, format, parsedDate);
                    return parsedDate;
                }
            }

            // 2. Try compact formats: 20251016 (yyyyMMdd) or 20251016143000 (yyyyMMddHHmmss)
            var compactPattern = @"^(\d{8})(\d{6})?$";
            var compactMatch = System.Text.RegularExpressions.Regex.Match(originalString, compactPattern);
            if (compactMatch.Success)
            {
                var dateFormats = compactMatch.Groups[2].Success
                    ? new[] { "yyyyMMddHHmmss" }
                    : new[] { "yyyyMMdd" };

                foreach (var format in dateFormats)
                {
                    if (DateTime.TryParseExact(originalString, format,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var parsedDate))
                    {
                        _logger.LogDebug("Parsed date '{DateString}' using compact format '{Format}' -> {ParsedDate}",
                            originalString, format, parsedDate);
                        return parsedDate;
                    }
                }
            }

            // 3. Try full date format: "Thursday, October 16, 2025"
            var dayNames = new[] { "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday" };
            foreach (var dayName in dayNames)
            {
                if (dateString.StartsWith(dayName))
                {
                    // Remove day name and comma, then try to parse the rest
                    var withoutDay = dateString.Replace(dayName, "").TrimStart(',', ' ');
                    var parsedDate = ParseDateFromString(withoutDay, referenceDate);
                    if (parsedDate.HasValue)
                    {
                        _logger.LogDebug("Parsed full date '{DateString}' -> {ParsedDate}", originalString, parsedDate.Value);
                        return parsedDate.Value;
                    }
                }
            }

            // 4. Try month name patterns with various formats
            foreach (var monthKvp in monthNames)
            {
                var monthName = monthKvp.Key;
                var monthNum = monthKvp.Value;

                // Pattern 1: "16 Oct 2025" or "16-Oct-2025" or "16-Oct-2025 2:30 PM"
                var pattern1 = $@"(\d{{1,2}})[\s\-]{{1}}{monthName}[\s\-]{{1}}(\d{{2,4}})(?:\s+(\d{{1,2}}):(\d{{2}})\s*(am|pm)?)?";
                var match1 = System.Text.RegularExpressions.Regex.Match(dateString, pattern1);
                if (match1.Success)
                {
                    var day = int.Parse(match1.Groups[1].Value);
                    var yearStr = match1.Groups[2].Value;
                    var year = yearStr.Length == 2 ? 2000 + int.Parse(yearStr) : int.Parse(yearStr);

                    if (match1.Groups[3].Success) // Has time component
                    {
                        var hour = int.Parse(match1.Groups[3].Value);
                        var minute = int.Parse(match1.Groups[4].Value);
                        var ampm = match1.Groups[5].Success ? match1.Groups[5].Value : "";

                        // Convert to 24-hour format if AM/PM specified
                        if (!string.IsNullOrEmpty(ampm))
                        {
                            if (ampm == "pm" && hour != 12) hour += 12;
                            if (ampm == "am" && hour == 12) hour = 0;
                        }

                        var dateWithTime = new DateTime(year, monthNum, day, hour, minute, 0);
                        _logger.LogDebug("Parsed date '{DateString}' as {Day}-{Month}-{Year} with time -> {ParsedDate}",
                            originalString, day, monthName, year, dateWithTime);
                        return dateWithTime;
                    }

                    var date = new DateTime(year, monthNum, day);
                    _logger.LogDebug("Parsed date '{DateString}' as {Day}-{Month}-{Year} -> {ParsedDate}",
                        originalString, day, monthName, year, date);
                    return date;
                }

                // Pattern 2: "Oct 16 2025" or "October 16, 2025"
                var pattern2 = $@"{monthName}\s+(\d{{1,2}})(?:st|nd|rd|th)?(?:,?\s*(\d{{2,4}}))?";
                var match2 = System.Text.RegularExpressions.Regex.Match(dateString, pattern2);
                if (match2.Success)
                {
                    var day = int.Parse(match2.Groups[1].Value);
                    var yearStr = match2.Groups[2].Success ? match2.Groups[2].Value : referenceDate.Year.ToString();
                    var year = yearStr.Length == 2 ? 2000 + int.Parse(yearStr) : int.Parse(yearStr);

                    var date = new DateTime(year, monthNum, day);
                    _logger.LogDebug("Parsed date '{DateString}' as {Month} {Day}, {Year} -> {ParsedDate}",
                        originalString, monthName, day, year, date);
                    return date;
                }
            }

            // 5. Try explicit date format patterns with dots: 16.10.2025
            var dotFormatPatterns = new[]
            {
                "dd.MM.yyyy", "d.M.yyyy", "dd.MM.yy", "d.M.yy"
            };

            foreach (var format in dotFormatPatterns)
            {
                if (DateTime.TryParseExact(originalString, format,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var parsedDate))
                {
                    _logger.LogDebug("Parsed date '{DateString}' using dot format '{Format}' -> {ParsedDate}",
                        originalString, format, parsedDate);
                    return parsedDate;
                }
            }

            // 6. Try standard date formats (MM/DD/YYYY, DD/MM/YYYY, etc.)
            var standardFormats = new[]
            {
                // 4-digit year formats
                "MM/dd/yyyy", "M/d/yyyy", "MM-dd-yyyy", "M-d-yyyy",
                "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy",
                // With time
                "MM/dd/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm:ss", "M/d/yyyy h:mm tt", "d/M/yyyy h:mm tt",
                "MM-dd-yyyy h:mm tt", "dd-MM-yyyy h:mm tt",
                // 2-digit year formats
                "MM/dd/yy", "M/d/yy", "MM-dd-yy", "M-d-yy",
                "dd/MM/yy", "d/M/yy", "dd-MM-yy", "d-M-yy"
            };

            foreach (var format in standardFormats)
            {
                if (DateTime.TryParseExact(originalString, format,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var parsedDate))
                {
                    _logger.LogDebug("Parsed date '{DateString}' using standard format '{Format}' -> {ParsedDate}",
                        originalString, format, parsedDate);
                    return parsedDate;
                }
            }

            // 7. Try ambiguous patterns with 4-digit year (DD/MM/YYYY vs MM/DD/YYYY)
            var ambiguousPattern4 = @"^(\d{1,2})[/-](\d{1,2})[/-](\d{4})$";
            var ambiguousMatch4 = System.Text.RegularExpressions.Regex.Match(originalString, ambiguousPattern4);
            if (ambiguousMatch4.Success)
            {
                var num1 = int.Parse(ambiguousMatch4.Groups[1].Value);
                var num2 = int.Parse(ambiguousMatch4.Groups[2].Value);
                var year = int.Parse(ambiguousMatch4.Groups[3].Value);

                // If first number is > 12, it must be day (DD/MM/YYYY)
                if (num1 > 12 && num2 >= 1 && num2 <= 12)
                {
                    try
                    {
                        var date = new DateTime(year, num2, num1);
                        _logger.LogDebug("Parsed ambiguous date '{DateString}' as DD/MM/YYYY -> {ParsedDate}", originalString, date);
                        return date;
                    }
                    catch { }
                }

                // If second number is > 12, it must be day (MM/DD/YYYY)
                if (num2 > 12 && num1 >= 1 && num1 <= 12)
                {
                    try
                    {
                        var date = new DateTime(year, num1, num2);
                        _logger.LogDebug("Parsed ambiguous date '{DateString}' as MM/DD/YYYY -> {ParsedDate}", originalString, date);
                        return date;
                    }
                    catch { }
                }

                // Both numbers are <= 12, ambiguous - try DD/MM/YYYY first (European format)
                if (num1 >= 1 && num1 <= 31 && num2 >= 1 && num2 <= 12)
                {
                    try
                    {
                        var date = new DateTime(year, num2, num1);
                        _logger.LogDebug("Parsed ambiguous date '{DateString}' as DD/MM/YYYY (default) -> {ParsedDate}", originalString, date);
                        return date;
                    }
                    catch
                    {
                        // DD/MM/YYYY failed, try MM/DD/YYYY as fallback
                        if (num1 >= 1 && num1 <= 12 && num2 >= 1 && num2 <= 31)
                        {
                            try
                            {
                                var date = new DateTime(year, num1, num2);
                                _logger.LogDebug("Parsed ambiguous date '{DateString}' as MM/DD/YYYY (fallback) -> {ParsedDate}", originalString, date);
                                return date;
                            }
                            catch { }
                        }
                    }
                }
            }

            // 8. Try ambiguous patterns with 2-digit year (DD/MM/YY vs MM/DD/YY)
            var ambiguousPattern2 = @"^(\d{1,2})[/-](\d{1,2})[/-](\d{2})$";
            var ambiguousMatch2 = System.Text.RegularExpressions.Regex.Match(originalString, ambiguousPattern2);
            if (ambiguousMatch2.Success)
            {
                var num1 = int.Parse(ambiguousMatch2.Groups[1].Value);
                var num2 = int.Parse(ambiguousMatch2.Groups[2].Value);
                var yearShort = int.Parse(ambiguousMatch2.Groups[3].Value);
                var year = yearShort >= 0 && yearShort <= 99 ? 2000 + yearShort : yearShort;

                // If first number is > 12, it must be day (DD/MM/YY)
                if (num1 > 12 && num2 >= 1 && num2 <= 12)
                {
                    try
                    {
                        var date = new DateTime(year, num2, num1);
                        _logger.LogDebug("Parsed ambiguous date '{DateString}' as DD/MM/YY -> {ParsedDate}", originalString, date);
                        return date;
                    }
                    catch { }
                }

                // If second number is > 12, it must be day (MM/DD/YY)
                if (num2 > 12 && num1 >= 1 && num1 <= 12)
                {
                    try
                    {
                        var date = new DateTime(year, num1, num2);
                        _logger.LogDebug("Parsed ambiguous date '{DateString}' as MM/DD/YY -> {ParsedDate}", originalString, date);
                        return date;
                    }
                    catch { }
                }

                // Both numbers are <= 12, ambiguous - try DD/MM/YY first (European format)
                if (num1 >= 1 && num1 <= 31 && num2 >= 1 && num2 <= 12)
                {
                    try
                    {
                        var date = new DateTime(year, num2, num1);
                        _logger.LogDebug("Parsed ambiguous date '{DateString}' as DD/MM/YY (default) -> {ParsedDate}", originalString, date);
                        return date;
                    }
                    catch
                    {
                        // DD/MM/YY failed, try MM/DD/YY as fallback
                        if (num1 >= 1 && num1 <= 12 && num2 >= 1 && num2 <= 31)
                        {
                            try
                            {
                                var date = new DateTime(year, num1, num2);
                                _logger.LogDebug("Parsed ambiguous date '{DateString}' as MM/DD/YY (fallback) -> {ParsedDate}", originalString, date);
                                return date;
                            }
                            catch { }
                        }
                    }
                }
            }

            // 9. Last resort: try general date parsing
            if (DateTime.TryParse(originalString, out var result))
            {
                _logger.LogDebug("Parsed date '{DateString}' using general parser -> {ParsedDate}", originalString, result);
                return result;
            }

            _logger.LogWarning("Failed to parse date string: '{DateString}'", originalString);
            return null;
        }

        /// <summary>
        /// Extract "around" time filters from query with ±30 minute window
        /// Supports: "around noon", "around midnight", "around 3 PM", "around 15:00"
        /// </summary>
        public (DateTime startDate, DateTime endDate)? ExtractAroundTimeFilter(string lowerQuery, DateTime referenceDate)
        {
            const int DEFAULT_WINDOW_MINUTES = 30; // ±30 minutes window

            // Check for "around noon" or "around midday"
            if (lowerQuery.Contains("around noon") || lowerQuery.Contains("around midday"))
            {
                var noonTime = referenceDate.Date.AddHours(12);
                var startTime = noonTime.AddMinutes(-DEFAULT_WINDOW_MINUTES);
                var endTime = noonTime.AddMinutes(DEFAULT_WINDOW_MINUTES);
                _logger.LogInformation("Time filter: around noon = {StartTime} to {EndTime}", startTime, endTime);
                return (startTime, endTime);
            }

            // Check for "around midnight"
            if (lowerQuery.Contains("around midnight"))
            {
                var midnightTime = referenceDate.Date; // 00:00
                var startTime = midnightTime.AddMinutes(-DEFAULT_WINDOW_MINUTES); // Previous day 23:30
                var endTime = midnightTime.AddMinutes(DEFAULT_WINDOW_MINUTES); // Today 00:30
                _logger.LogInformation("Time filter: around midnight = {StartTime} to {EndTime}", startTime, endTime);
                return (startTime, endTime);
            }

            // Check for "morning" - full morning period (5 AM to 12 PM)
            if (lowerQuery.Contains("morning") || lowerQuery.Contains("in the morning"))
            {
                var morningStart = referenceDate.Date.AddHours(5); // 5:00 AM
                var morningEnd = referenceDate.Date.AddHours(12); // 12:00 PM
                _logger.LogInformation("Time filter: morning = {StartTime} to {EndTime}", morningStart, morningEnd);
                return (morningStart, morningEnd);
            }

            // Check for "afternoon" - full afternoon period (12 PM to 5 PM)
            if (lowerQuery.Contains("afternoon") || lowerQuery.Contains("in the afternoon"))
            {
                var afternoonStart = referenceDate.Date.AddHours(12); // 12:00 PM
                var afternoonEnd = referenceDate.Date.AddHours(17); // 5:00 PM
                _logger.LogInformation("Time filter: afternoon = {StartTime} to {EndTime}", afternoonStart, afternoonEnd);
                return (afternoonStart, afternoonEnd);
            }

            // Check for "evening" - full evening period (5 PM to 9 PM)
            if (lowerQuery.Contains("evening") || lowerQuery.Contains("in the evening"))
            {
                var eveningStart = referenceDate.Date.AddHours(17); // 5:00 PM
                var eveningEnd = referenceDate.Date.AddHours(21); // 9:00 PM
                _logger.LogInformation("Time filter: evening = {StartTime} to {EndTime}", eveningStart, eveningEnd);
                return (eveningStart, eveningEnd);
            }

            // Check for "night" - full night period (9 PM to 5 AM next day)
            if (lowerQuery.Contains("night") || lowerQuery.Contains("at night"))
            {
                var nightStart = referenceDate.Date.AddHours(21); // 9:00 PM
                var nightEnd = referenceDate.Date.AddDays(1).AddHours(5); // 5:00 AM next day
                _logger.LogInformation("Time filter: night = {StartTime} to {EndTime}", nightStart, nightEnd);
                return (nightStart, nightEnd);
            }

            // Check for "around [specific time]" (e.g., "around 3 PM", "around 15:00", "around 3:45 PM")
            var aroundTimeMatch = System.Text.RegularExpressions.Regex.Match(lowerQuery, @"around\s+(\d{1,2})(?::(\d{2}))?\s*(am|pm)?");
            if (aroundTimeMatch.Success)
            {
                var hour = int.Parse(aroundTimeMatch.Groups[1].Value);
                var minute = aroundTimeMatch.Groups[2].Success ? int.Parse(aroundTimeMatch.Groups[2].Value) : 0;
                var ampm = aroundTimeMatch.Groups[3].Success ? aroundTimeMatch.Groups[3].Value.ToLowerInvariant() : "";

                // Convert to 24-hour format if AM/PM specified
                if (!string.IsNullOrEmpty(ampm))
                {
                    if (ampm == "pm" && hour != 12) hour += 12;
                    if (ampm == "am" && hour == 12) hour = 0;
                }

                var targetTime = referenceDate.Date.AddHours(hour).AddMinutes(minute);
                var startTime = targetTime.AddMinutes(-DEFAULT_WINDOW_MINUTES);
                var endTime = targetTime.AddMinutes(DEFAULT_WINDOW_MINUTES);
                _logger.LogInformation("Time filter: around {TargetTime} = {StartTime} to {EndTime}", targetTime, startTime, endTime);
                return (startTime, endTime);
            }

            return null;
        }

        /// <summary>
        /// Extract file type filters from query
        /// </summary>
        public List<string> ExtractFileTypeFilters(string query)
        {
            var lowerQuery = query.ToLowerInvariant();
            var fileTypes = new List<string>();

            // Check for specific file type mentions
            if (lowerQuery.Contains("pdf"))
                fileTypes.Add("pdf");

            if (lowerQuery.Contains("word") || lowerQuery.Contains("docx") || lowerQuery.Contains("doc"))
            {
                fileTypes.AddRange(new[] { "docx", "doc" });
            }

            if (lowerQuery.Contains("excel") || lowerQuery.Contains("xlsx") || lowerQuery.Contains("xls"))
            {
                fileTypes.AddRange(new[] { "xlsx", "xls" });
            }

            if (lowerQuery.Contains("powerpoint") || lowerQuery.Contains("pptx") || lowerQuery.Contains("ppt"))
            {
                fileTypes.AddRange(new[] { "pptx", "ppt" });
            }

            if (lowerQuery.Contains("text") || lowerQuery.Contains("txt"))
                fileTypes.Add("txt");

            return fileTypes.Distinct().ToList();
        }

        /// <summary>
        /// Check if query is asking for earliest/latest records
        /// </summary>
        public (bool isEarliest, bool isLatest) ExtractSortingIntent(string query)
        {
            var lowerQuery = query.ToLowerInvariant();

            var isEarliest = lowerQuery.Contains("earliest") || lowerQuery.Contains("oldest") ||
                           lowerQuery.Contains("first created");

            var isLatest = lowerQuery.Contains("latest") || lowerQuery.Contains("newest") ||
                         lowerQuery.Contains("most recent") || lowerQuery.Contains("recently created");

            return (isEarliest, isLatest);
        }

        #endregion

        #region Search Optimization Methods

        /// <summary>
        /// Calculate dynamic search limit based on query complexity
        /// </summary>
        public int CalculateDynamicSearchLimit(int topK, bool isEarliest, bool isLatest, DateTime? startDate, DateTime? endDate, int fileTypeFiltersCount, int contentKeywordsCount)
        {
            var baseMultiplier = 3; // Base multiplier for simple queries

            // Increase multiplier for complex queries
            if (isEarliest || isLatest)
                baseMultiplier = Math.Max(baseMultiplier, 20); // Need more results to sort properly

            if (startDate.HasValue || endDate.HasValue)
                baseMultiplier = Math.Max(baseMultiplier, 10); // Date filtering needs more results

            if (fileTypeFiltersCount > 0)
                baseMultiplier += 5; // File type filtering

            if (contentKeywordsCount > 2)
                baseMultiplier += 3; // Complex content queries

            // Cap the maximum
            return Math.Min(topK * baseMultiplier, 1000);
        }

        /// <summary>
        /// Calculate dynamic minimum score based on query characteristics
        /// </summary>
        public float CalculateDynamicMinimumScore(float originalMinScore, string query, List<string> contentKeywords)
        {
            var adjustedScore = originalMinScore;
            var lowerQuery = query.ToLowerInvariant();

            // Lower threshold for specific content searches
            if (contentKeywords.Any(k => new[] { "api", "service", "system", "application" }.Contains(k.ToLowerInvariant())))
            {
                adjustedScore = Math.Max(0.1f, adjustedScore - 0.2f);
            }

            // Lower threshold for name searches
            if (contentKeywords.Any(k => char.IsUpper(k[0]))) // Proper nouns
            {
                adjustedScore = Math.Max(0.15f, adjustedScore - 0.15f);
            }

            // Lower threshold for document type searches
            if (lowerQuery.Contains("invoice") || lowerQuery.Contains("document") || lowerQuery.Contains("file"))
            {
                adjustedScore = Math.Max(0.2f, adjustedScore - 0.1f);
            }

            return adjustedScore;
        }

        #endregion

        #region Result Processing Methods

        /// <summary>
        /// Apply date range filter to results by comparing date_created field
        /// Supports filtering by start date, end date, or both (including time)
        /// </summary>
        public List<(string id, float similarity, Dictionary<string, object> metadata)> ApplyDateRangeFilter(
            List<(string id, float similarity, Dictionary<string, object> metadata)> results,
            DateTime? startDate,
            DateTime? endDate)
        {
            _logger.LogInformation("ApplyDateRangeFilter called with: StartDate={StartDate}, EndDate={EndDate}, TotalResults={Count}",
                startDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "null",
                endDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "null",
                results.Count);

            var filteredResults = results.Where(result =>
            {
                var dateCreated = GetMetadataValue<string>(result.metadata, "date_created");
                if (string.IsNullOrEmpty(dateCreated))
                {
                    _logger.LogInformation("Record has no date_created field");
                    return false;
                }

                // Try to parse the date_created field
                // IMPORTANT: MM/dd/yyyy must come FIRST since dates in Qdrant are stored in MM/DD/YYYY format
                var dateFormats = new[] { "MM/dd/yyyy", "M/d/yyyy", "MM/dd/yyyy HH:mm:ss",
                                "dd/MM/yyyy", "dd-MM-yyyy", "d/M/yyyy", "d-M-yyyy", "dd/MM/yyyy HH:mm:ss",
                                "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss" };
                DateTime parsedDate = default;
                bool dateParseSuccess = false;
                string usedFormat = "";

                foreach (var format in dateFormats)
                {
                    if (DateTime.TryParseExact(dateCreated, format, null, System.Globalization.DateTimeStyles.None, out parsedDate))
                    {
                        dateParseSuccess = true;
                        usedFormat = format;
                        break;
                    }
                }

                // Fallback: try general parse
                if (!dateParseSuccess)
                {
                    dateParseSuccess = DateTime.TryParse(dateCreated, out parsedDate);
                    if (dateParseSuccess)
                        usedFormat = "General parse";
                }

                if (!dateParseSuccess)
                {
                    _logger.LogWarning("Failed to parse date_created: '{DateCreated}'", dateCreated);
                    return false;
                }

                _logger.LogInformation("Parsed date_created: '{DateCreated}' -> {ParsedDate} (format: {Format})",
                    dateCreated, parsedDate.ToString("yyyy-MM-dd HH:mm:ss"), usedFormat);

                // Apply date range filtering
                bool withinRange = true;

                if (startDate.HasValue)
                {
                    // If startDate has time component, use exact DateTime comparison
                    // Otherwise, use date-only comparison
                    if (startDate.Value.TimeOfDay != TimeSpan.Zero)
                    {
                        withinRange &= parsedDate >= startDate.Value;
                        _logger.LogInformation("  StartDate check: {ParsedDate} >= {StartDate} = {Result}",
                            parsedDate, startDate.Value, parsedDate >= startDate.Value);
                    }
                    else
                    {
                        withinRange &= parsedDate.Date >= startDate.Value.Date;
                        _logger.LogInformation("  StartDate check (date only): {ParsedDate} >= {StartDate} = {Result}",
                            parsedDate.Date, startDate.Value.Date, parsedDate.Date >= startDate.Value.Date);
                    }
                }

                if (endDate.HasValue)
                {
                    // If endDate has time component, use exact DateTime comparison
                    // Otherwise, use date-only comparison
                    if (endDate.Value.TimeOfDay != TimeSpan.Zero)
                    {
                        withinRange &= parsedDate <= endDate.Value;
                        _logger.LogInformation("  EndDate check: {ParsedDate} <= {EndDate} = {Result}",
                            parsedDate, endDate.Value, parsedDate <= endDate.Value);
                    }
                    else
                    {
                        withinRange &= parsedDate.Date <= endDate.Value.Date;
                        _logger.LogInformation("  EndDate check (date only): {ParsedDate} <= {EndDate} = {Result}",
                            parsedDate.Date, endDate.Value.Date, parsedDate.Date <= endDate.Value.Date);
                    }
                }

                return withinRange;
            }).ToList();

            _logger.LogInformation("Date filtering complete: {FilteredCount} / {TotalCount} records match date criteria",
                filteredResults.Count, results.Count);

            return filteredResults;
        }

        /// <summary>
        /// Apply metadata filters to search results
        /// </summary>
        public List<(string id, float similarity, Dictionary<string, object> metadata)> ApplyMetadataFilters(
            List<(string id, float similarity, Dictionary<string, object> metadata)> results,
            Dictionary<string, object> filters)
        {
            return results.Where(result =>
            {
                foreach (var filter in filters)
                {
                    var safeKey = $"meta_{MakeSafeKey(filter.Key)}";

                    if (!result.metadata.ContainsKey(safeKey))
                        return false;

                    var metadataValue = result.metadata[safeKey]?.ToString() ?? "";
                    var filterValue = filter.Value?.ToString() ?? "";

                    // Perform case-insensitive comparison
                    if (!metadataValue.Equals(filterValue, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                return true;
            }).ToList();
        }

        /// <summary>
        /// Apply file type filter to search results based on file extensions or document types
        /// </summary>
        public List<(string id, float similarity, Dictionary<string, object> metadata)> ApplyFileTypeFilter(
            List<(string id, float similarity, Dictionary<string, object> metadata)> results,
            List<string> fileTypes)
        {
            return results.Where(result =>
            {
                // Check if the record has document content (electronic documents)
                var recordType = GetMetadataValue<string>(result.metadata, "record_type") ?? "";
                var chunkContent = GetMetadataValue<string>(result.metadata, "chunk_content") ?? "";
                var recordTitle = GetMetadataValue<string>(result.metadata, "record_title") ?? "";
                var fileExtension = GetMetadataValue<string>(result.metadata, "file_extension") ?? "";
                var storedFileType = GetMetadataValue<string>(result.metadata, "file_type") ?? "";
                var documentCategory = GetMetadataValue<string>(result.metadata, "document_category") ?? "";

                // Skip containers - we only want document files
                if (recordType.Equals("Container", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // Check file extension from metadata first (most reliable)
                foreach (var fileType in fileTypes)
                {
                    var extension = $".{fileType}";

                    // Check stored metadata fields
                    if (fileExtension.Equals(extension, StringComparison.OrdinalIgnoreCase) ||
                        storedFileType.Equals(fileType, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    // Check document category
                    if (documentCategory.Contains(fileType, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    // Check if title contains the file extension
                    if (recordTitle.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    // For semantic matching, check if content mentions the file type
                    if (chunkContent.Contains(fileType, StringComparison.OrdinalIgnoreCase) ||
                        chunkContent.Contains(extension, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    // Check for common document type patterns
                    if (fileType == "pdf" && (recordTitle.Contains("pdf", StringComparison.OrdinalIgnoreCase) ||
                        chunkContent.Contains("portable document", StringComparison.OrdinalIgnoreCase) ||
                        documentCategory.Contains("PDF", StringComparison.OrdinalIgnoreCase)))
                    {
                        return true;
                    }

                    if ((fileType == "docx" || fileType == "doc") &&
                        (recordTitle.Contains("word", StringComparison.OrdinalIgnoreCase) ||
                         chunkContent.Contains("microsoft word", StringComparison.OrdinalIgnoreCase) ||
                         documentCategory.Contains("Word", StringComparison.OrdinalIgnoreCase)))
                    {
                        return true;
                    }

                    if ((fileType == "xlsx" || fileType == "xls") &&
                        (recordTitle.Contains("excel", StringComparison.OrdinalIgnoreCase) ||
                         chunkContent.Contains("microsoft excel", StringComparison.OrdinalIgnoreCase) ||
                         chunkContent.Contains("spreadsheet", StringComparison.OrdinalIgnoreCase) ||
                         documentCategory.Contains("Excel", StringComparison.OrdinalIgnoreCase)))
                    {
                        return true;
                    }

                    if ((fileType == "pptx" || fileType == "ppt") &&
                        (recordTitle.Contains("powerpoint", StringComparison.OrdinalIgnoreCase) ||
                         chunkContent.Contains("microsoft powerpoint", StringComparison.OrdinalIgnoreCase) ||
                         chunkContent.Contains("presentation", StringComparison.OrdinalIgnoreCase) ||
                         documentCategory.Contains("PowerPoint", StringComparison.OrdinalIgnoreCase)))
                    {
                        return true;
                    }
                }

                return false;
            }).ToList();
        }

        /// <summary>
        /// Apply sorting by date created to the results
        /// </summary>
        public List<(string id, float similarity, Dictionary<string, object> metadata)> ApplyDateSorting(
            List<(string id, float similarity, Dictionary<string, object> metadata)> results,
            bool earliest)
        {
            if (earliest)
            {
                // Sort by date_created ascending
                return results.OrderBy(r => GetMetadataValue<string>(r.metadata, "date_created")).ToList();
            }
            else
            {
                // Sort by date_created descending
                return results.OrderByDescending(r => GetMetadataValue<string>(r.metadata, "date_created")).ToList();
            }
        }

        /// <summary>
        /// Build a content preview from metadata
        /// </summary>
        public string BuildContentPreview(Dictionary<string, object> metadata)
        {
            var preview = new StringBuilder();

            // Add key fields
            var title = GetMetadataValue<string>(metadata, "record_title");
            var dateCreated = GetMetadataValue<string>(metadata, "date_created");
            var recordType = GetMetadataValue<string>(metadata, "record_type");
            var container = GetMetadataValue<string>(metadata, "container");
            var assignee = GetMetadataValue<string>(metadata, "assignee");

            if (!string.IsNullOrEmpty(title))
                preview.AppendLine($"Title: {title}");

            if (!string.IsNullOrEmpty(dateCreated))
                preview.AppendLine($"Created: {dateCreated}");

            if (!string.IsNullOrEmpty(recordType))
                preview.AppendLine($"Type: {recordType}");

            if (!string.IsNullOrEmpty(container))
                preview.AppendLine($"Container: {container}");

            if (!string.IsNullOrEmpty(assignee))
                preview.AppendLine($"Assignee: {assignee}");

            // Add some metadata fields
            var metaFields = metadata
                .Where(kvp => kvp.Key.StartsWith("meta_") && kvp.Value != null)
                .Take(5);

            foreach (var field in metaFields)
            {
                var fieldName = field.Key.Replace("meta_", "").Replace("_", " ");
                preview.AppendLine($"{fieldName}: {field.Value}");
            }

            return preview.ToString();
        }

        #endregion

        #region Metadata Utility Methods

        /// <summary>
        /// Get metadata value with type conversion
        /// </summary>
        public T? GetMetadataValue<T>(Dictionary<string, object> metadata, string key)
        {
            if (!metadata.TryGetValue(key, out var value))
                return default;

            if (value is System.Text.Json.JsonElement jsonElement)
            {
                return System.Text.Json.JsonSerializer.Deserialize<T>(jsonElement.GetRawText());
            }

            try
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return default;
            }
        }

        /// <summary>
        /// Make a safe key name for metadata storage
        /// </summary>
        public string MakeSafeKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return "unknown";

            var safeKey = new string(key
                .ToLowerInvariant()
                .Select(c => char.IsLetterOrDigit(c) ? c : '_')
                .ToArray());

            while (safeKey.Contains("__"))
                safeKey = safeKey.Replace("__", "_");

            return safeKey.Trim('_');
        }

        #endregion
    }
}
