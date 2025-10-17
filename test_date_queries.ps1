# Comprehensive Date Query Testing Script
# Tests all date filtering capabilities of the Record Search API

$baseUrl = "http://localhost:5000/api/RecordEmbedding/search"

Write-Host "====================================" -ForegroundColor Cyan
Write-Host "Comprehensive Date Query Test Suite" -ForegroundColor Cyan
Write-Host "====================================" -ForegroundColor Cyan
Write-Host ""

# Test queries organized by category
$testQueries = @{
    "SPECIFIC DATES" = @(
        "records created today",
        "documents from yesterday",
        "files created on October 9, 2025",
        "records from 09-10-2025"
    )

    "DATE RANGES" = @(
        "documents created after October 9, 2025",
        "records before January 1, 2025",
        "files from last 7 days",
        "documents from last week"
    )

    "WEEKS" = @(
        "records from this week",
        "documents from week 1",
        "files from week 42 of 2024",
        "records from the week of October 3",
        "documents from last 4 weeks",
        "files from last 2 weeks"
    )

    "MONTHS" = @(
        "records from this month",
        "documents from last month",
        "files from October 2024",
        "records from last October",
        "documents from September",
        "files from last 6 months",
        "records from last 3 months"
    )

    "YEARS" = @(
        "records from this year",
        "documents from last year",
        "files from 2024",
        "records from year 2023",
        "documents from last 2 years"
    )

    "QUARTERS" = @(
        "records from Q1 2024",
        "documents from Q2",
        "files from first quarter",
        "records from second quarter 2023",
        "documents from fourth quarter"
    )

    "SORTING" = @(
        "Which record has the earliest creation date?",
        "What are the most recently created documents?",
        "Show me the latest files",
        "Find the oldest records"
    )

    "TIME-OF-DAY QUERIES" = @(
        "documents created in the morning",
        "files created in the afternoon",
        "records created in the evening",
        "documents created at night",
        "files created around noon",
        "records created around midnight",
        "documents created around 3 PM",
        "files created around 9:30 AM"
    )

    "BETWEEN DATE RANGES" = @(
        "records created between October 1, 2025 and October 10, 2025",
        "documents from October 5, 2025 to October 12, 2025",
        "files between January 1, 2024 and December 31, 2024",
        "records created from 09-01-2025 to 09-30-2025"
    )

    "COMBINED QUERIES" = @(
        "Excel files from Q1 2024",
        "Word documents created in October 2024",
        "PDF files from last month",
        "documents created after 3:45 PM today",
        "records created in the morning between October 1 and October 10",
        "files created in the afternoon from last week"
    )
}

function Test-Query {
    param(
        [string]$query,
        [int]$queryNumber,
        [int]$totalQueries
    )

    Write-Host "[$queryNumber/$totalQueries] Testing: " -NoNewline -ForegroundColor Yellow
    Write-Host "$query" -ForegroundColor White

    $body = @{
        query = $query
        topK = 5
        minimumScore = 0.3
    } | ConvertTo-Json

    try {
        $response = Invoke-RestMethod -Uri $baseUrl -Method Post -Body $body -ContentType "application/json" -TimeoutSec 30

        Write-Host "  ✓ Results: $($response.totalResults) records found" -ForegroundColor Green
        Write-Host "  ✓ Query Time: $([math]::Round($response.queryTime, 2))s" -ForegroundColor Green

        if ($response.results.Count -gt 0) {
            Write-Host "  ✓ Sample Record: $($response.results[0].recordTitle) (Created: $($response.results[0].dateCreated))" -ForegroundColor Cyan
        }

        if ($response.synthesizedAnswer) {
            $shortAnswer = if ($response.synthesizedAnswer.Length -gt 100) {
                $response.synthesizedAnswer.Substring(0, 100) + "..."
            } else {
                $response.synthesizedAnswer
            }
            Write-Host "  ✓ AI Summary: $shortAnswer" -ForegroundColor Magenta
        }

        Write-Host ""
        return $true
    }
    catch {
        Write-Host "  ✗ Error: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host ""
        return $false
    }
}

# Run all tests
$totalTests = 0
$successfulTests = 0
$queryCounter = 0

foreach ($category in $testQueries.Keys | Sort-Object) {
    $totalTests += $testQueries[$category].Count
}

Write-Host "Starting test execution ($totalTests total queries)..." -ForegroundColor Green
Write-Host ""

foreach ($category in $testQueries.Keys | Sort-Object) {
    Write-Host "=====================================" -ForegroundColor Yellow
    Write-Host "Category: $category" -ForegroundColor Yellow
    Write-Host "=====================================" -ForegroundColor Yellow

    foreach ($query in $testQueries[$category]) {
        $queryCounter++
        if (Test-Query -query $query -queryNumber $queryCounter -totalQueries $totalTests) {
            $successfulTests++
        }

        # Small delay between requests
        Start-Sleep -Milliseconds 500
    }

    Write-Host ""
}

# Summary
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "Test Summary" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "Total Queries: $totalTests" -ForegroundColor White
Write-Host "Successful: $successfulTests" -ForegroundColor Green
Write-Host "Failed: $($totalTests - $successfulTests)" -ForegroundColor Red
Write-Host "Success Rate: $([math]::Round(($successfulTests / $totalTests) * 100, 2))%" -ForegroundColor $(if ($successfulTests -eq $totalTests) { "Green" } else { "Yellow" })
Write-Host ""

if ($successfulTests -eq $totalTests) {
    Write-Host "🎉 All tests passed!" -ForegroundColor Green
} else {
    Write-Host "⚠️ Some tests failed. Check the API logs for details." -ForegroundColor Yellow
}
