# Comprehensive Search API Test Script
# This script tests various search scenarios to validate the dynamic search functionality

$baseUrl = "https://localhost:7170/api/RecordEmbedding/search"
$headers = @{
    "Content-Type" = "application/json"
    "Accept" = "application/json"
}

# Test cases with expected behavior
$testCases = @(
    @{
        Name = "Basic Content Search - Invoice with Name"
        Query = "invoice details of Umar khan"
        TopK = 20
        MinScore = 0.3
        ExpectedBehavior = "Should find invoices or documents mentioning Umar khan with lowered threshold"
    },
    @{
        Name = "Date Range Search - After Specific Date"
        Query = "Show me all documents created after October 9, 2025"
        TopK = 15
        MinScore = 0.2
        ExpectedBehavior = "Should filter documents created after 2025-10-09"
    },
    @{
        Name = "Earliest Date Search"
        Query = "Which record has the earliest creation date?"
        TopK = 5
        MinScore = 0.1
        ExpectedBehavior = "Should return records sorted by creation date ascending"
    },
    @{
        Name = "File Type Search with Time"
        Query = "List all Word or Excel documents added after 3:45 PM"
        TopK = 10
        MinScore = 0.3
        ExpectedBehavior = "Should filter .doc/.docx/.xls/.xlsx files created after 15:45 today"
    },
    @{
        Name = "Recent Content Search"
        Query = "What are the most recently created documents related to API or Service?"
        TopK = 10
        MinScore = 0.2
        ExpectedBehavior = "Should find recent documents containing API or Service keywords"
    },
    @{
        Name = "Container Search"
        Query = "What records belong to container D25/1?"
        TopK = 20
        MinScore = 0.4
        ExpectedBehavior = "Should find records in container D25/1"
    },
    @{
        Name = "Financial Document Search"
        Query = "Find records that look like invoices or financial documents"
        TopK = 15
        MinScore = 0.3
        ExpectedBehavior = "Should find invoice-like or financial documents using semantic matching"
    },
    @{
        Name = "PDF File Search"
        Query = "Find all PDF files related to ControlPoint or CP3000"
        TopK = 10
        MinScore = 0.3
        ExpectedBehavior = "Should find PDF files containing ControlPoint or CP3000"
    },
    @{
        Name = "Today's Records"
        Query = "records created today"
        TopK = 20
        MinScore = 0.2
        ExpectedBehavior = "Should find records created today"
    },
    @{
        Name = "Recently Created"
        Query = "any record which is created recently"
        TopK = 20
        MinScore = 0.3
        ExpectedBehavior = "Should find records from last 14 days"
    },
    @{
        Name = "Empty Query Test"
        Query = ""
        TopK = 10
        MinScore = 0.3
        ExpectedBehavior = "Should return error or helpful message for empty query"
    },
    @{
        Name = "Invalid Parameters Test"
        Query = "test search"
        TopK = 150  # Should be clamped to 100
        MinScore = 1.5  # Should be clamped to 1.0
        ExpectedBehavior = "Should handle invalid parameters gracefully"
    },
    @{
        Name = "Special Characters Test"
        Query = "documents with "special quotes" & symbols@#$"
        TopK = 10
        MinScore = 0.3
        ExpectedBehavior = "Should clean and normalize special characters"
    },
    @{
        Name = "Very Long Query Test"
        Query = "This is a very long query with many words that should still be processed correctly by the search engine and return relevant results even though it contains a lot of text and might be more complex than usual queries"
        TopK = 10
        MinScore = 0.3
        ExpectedBehavior = "Should handle long queries without errors"
    },
    @{
        Name = "Specific Business Terms"
        Query = "contract agreement proposal report statement"
        TopK = 15
        MinScore = 0.2
        ExpectedBehavior = "Should find documents with business-related content"
    }
)

# Initialize test results
$results = @()
$passCount = 0
$failCount = 0

Write-Host "?? Starting Comprehensive Search API Tests" -ForegroundColor Green
Write-Host "=========================================="
Write-Host ""

foreach ($testCase in $testCases) {
    Write-Host "?? Test: $($testCase.Name)" -ForegroundColor Cyan
    Write-Host "Query: '$($testCase.Query)'"
    Write-Host "Expected: $($testCase.ExpectedBehavior)"
    
    try {
        # Prepare request body
        $requestBody = @{
            query = $testCase.Query
            topK = $testCase.TopK
            minimumScore = $testCase.MinScore
        } | ConvertTo-Json -Depth 10
        
        # Make API call
        $startTime = Get-Date
        try {
            $response = Invoke-RestMethod -Uri $baseUrl -Method Post -Body $requestBody -Headers $headers -SkipCertificateCheck
            $endTime = Get-Date
            $duration = ($endTime - $startTime).TotalMilliseconds
            
            # Analyze response
            $success = $true
            $message = ""
            $details = @{}
            
            if ($response) {
                $details = @{
                    "Results Found" = $response.TotalResults
                    "Query Time" = "$([math]::Round($response.QueryTime, 3))s"
                    "Response Time" = "$([math]::Round($duration, 0))ms"
                    "Has AI Summary" = if ($response.SynthesizedAnswer) { "Yes" } else { "No" }
                }
                
                # Basic validation
                if ($response.Query -eq $testCase.Query) {
                    $details["Query Echo"] = "? Correct"
                } else {
                    $details["Query Echo"] = "? Mismatch"
                    $success = $false
                }
                
                # Check for reasonable response times
                if ($duration -lt 10000) { # Less than 10 seconds
                    $details["Performance"] = "? Good ($([math]::Round($duration, 0))ms)"
                } else {
                    $details["Performance"] = "?? Slow ($([math]::Round($duration, 0))ms)"
                }
                
                # Special case validations
                if ($testCase.Name -contains "Empty Query" -and $response.TotalResults -eq 0) {
                    $details["Empty Query Handling"] = "? Handled correctly"
                }
                
                if ($testCase.Name -contains "Invalid Parameters" -and $response.TotalResults -ge 0) {
                    $details["Parameter Validation"] = "? Parameters validated"
                }
                
                $message = "? PASSED"
                $passCount++
            } else {
                $success = $false
                $message = "? FAILED - No response received"
                $failCount++
            }
            
        } catch {
            $endTime = Get-Date
            $duration = ($endTime - $startTime).TotalMilliseconds
            $success = $false
            $message = "? FAILED - $($_.Exception.Message)"
            $details = @{
                "Error" = $_.Exception.Message
                "Response Time" = "$([math]::Round($duration, 0))ms"
            }
            $failCount++
        }
        
        # Display results
        Write-Host "Status: $message" -ForegroundColor $(if ($success) { "Green" } else { "Red" })
        
        foreach ($key in $details.Keys) {
            Write-Host "  $key`: $($details[$key])" -ForegroundColor Gray
        }
        
        # Store result
        $results += @{
            TestName = $testCase.Name
            Query = $testCase.Query
            Success = $success
            Message = $message
            Details = $details
            Duration = $duration
        }
        
    } catch {
        Write-Host "? FAILED - Test execution error: $($_.Exception.Message)" -ForegroundColor Red
        $failCount++
    }
    
    Write-Host ""
    Start-Sleep -Seconds 1  # Brief pause between tests
}

# Summary
Write-Host "=========================================="
Write-Host "?? TEST SUMMARY" -ForegroundColor Yellow
Write-Host "=========================================="
Write-Host "Total Tests: $($testCases.Count)" -ForegroundColor White
Write-Host "? Passed: $passCount" -ForegroundColor Green
Write-Host "? Failed: $failCount" -ForegroundColor Red
Write-Host "Success Rate: $([math]::Round(($passCount / $testCases.Count) * 100, 1))%" -ForegroundColor $(if ($passCount -gt $failCount) { "Green" } else { "Red" })

if ($failCount -gt 0) {
    Write-Host ""
    Write-Host "? FAILED TESTS:" -ForegroundColor Red
    foreach ($result in $results | Where-Object { -not $_.Success }) {
        Write-Host "  • $($result.TestName): $($result.Message)" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "? Performance Analysis:" -ForegroundColor Cyan
$avgDuration = ($results | Measure-Object -Property Duration -Average).Average
Write-Host "  Average Response Time: $([math]::Round($avgDuration, 0))ms"
$fastestTest = ($results | Sort-Object Duration | Select-Object -First 1)
Write-Host "  Fastest Test: $($fastestTest.TestName) ($([math]::Round($fastestTest.Duration, 0))ms)"
$slowestTest = ($results | Sort-Object Duration -Descending | Select-Object -First 1)
Write-Host "  Slowest Test: $($slowestTest.TestName) ($([math]::Round($slowestTest.Duration, 0))ms)"

Write-Host ""
Write-Host "?? Recommendations:" -ForegroundColor Yellow
if ($avgDuration -gt 2000) {
    Write-Host "  • Consider optimizing query processing (avg response time > 2s)" -ForegroundColor Yellow
}
if ($failCount -gt ($testCases.Count * 0.2)) {
    Write-Host "  • High failure rate detected - check API functionality" -ForegroundColor Yellow
}
Write-Host "  • Monitor search relevance and accuracy for business queries" -ForegroundColor Yellow
Write-Host "  • Consider adding more specific test cases for your domain" -ForegroundColor Yellow

Write-Host ""
Write-Host "Test completed! ??" -ForegroundColor Green