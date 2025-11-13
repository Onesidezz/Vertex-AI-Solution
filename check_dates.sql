-- Check sample dates in the database
SELECT
    "RecordUri",
    "RecordTitle",
    "DateCreated",
    "FileType",
    "IndexedAt"
FROM "Embeddings"
ORDER BY "IndexedAt" DESC
LIMIT 10;

-- Check date range
SELECT
    MIN("DateCreated") as earliest_date,
    MAX("DateCreated") as latest_date,
    COUNT(*) as total_records
FROM "Embeddings";

-- Check how many records have dates
SELECT
    COUNT(*) as total_records,
    COUNT("DateCreated") as records_with_dates,
    COUNT(*) - COUNT("DateCreated") as records_without_dates
FROM "Embeddings";
