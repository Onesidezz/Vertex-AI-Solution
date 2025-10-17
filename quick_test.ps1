# Quick Search API Test - PowerShell Version
# Fast and focused testing for essential functionality

$baseUrl = "https://localhost:7170/api/RecordEmbedding/search"
$totalTests = 0
$passedTests = 0

Write-Host "?? Quick Search API Test" -ForegroundColor Green
Write-Host "================================"

function Quick-Test {
    param(
        [string]$Name,
        [string]$Query,
        [int]$TopK,
        [float]$MinScore
    )
    
    $script:totalTests++
    
    Write-Host "Test $($script:totalTests): $Name" -ForegroundColor Cyan
    
    try {
        # Prepare request
        $requestBody = @{
            query = $Query
            topK = $TopK
            minimumScore = $MinScore
        } | ConvertTo-Json -Depth 3
        
        # Make API call with timeout
        $response = Invoke-RestMethod -Uri $baseUrl -Method Post -Body $requestBody -ContentType "application/json" -TimeoutSec 10 -SkipCertificateCheck -ErrorAction Stop
        
        # Validate response
        if ($response.TotalResults -ge 0 -and $response.QueryTime -ge 0) {
            Write-Host "  ? PASSED - Found: $($response.TotalResults) results in $([math]::Round($response.QueryTime, 3))s" -ForegroundColor Green
            $script:passedTests++
        } else {
            Write-Host "  ? FAILED - Invalid response structure" -ForegroundColor Red
        }
        
    } catch {
        Write-Host "  ? FAILED - $($_.Exception.Message)" -ForegroundColor Red
    }
}

# Run essential tests
Write-Host "Running essential tests..."
Write-Host ""

# Test 1: Basic content search
Quick-Test -Name "Basic Search" -Query "invoice details of Umar khan" -TopK 20 -MinScore 0.3

# Test 2: Date-based search
Quick-Test -Name "Date Search" -Query "recently created records" -TopK 10 -MinScore 0.2

# Test 3: File type search
Quick-Test -Name "File Type Search" -Query "PDF documents" -TopK 10 -MinScore 0.3

# Test 4: Empty query handling
Quick-Test -Name "Empty Query" -Query "" -TopK 10 -MinScore 0.3

# Test 5: Parameter validation
Quick-Test -Name "Parameter Test" -Query "test search" -TopK 150 -MinScore 1.5

# Summary
Write-Host ""
Write-Host "================================"
Write-Host "?? QUICK TEST SUMMARY" -ForegroundColor Yellow
Write-Host "================================"
Write-Host "Total Tests: $totalTests"
Write-Host "Passed: $passedTests" -ForegroundColor Green
Write-Host "Failed: $($totalTests - $passedTests)" -ForegroundColor Red

$successRate = [math]::Round(($passedTests / $totalTests) * 100, 1)

if ($passedTests -eq $totalTests) {
    Write-Host "?? All tests passed! API is working correctly." -ForegroundColor Green
    Write-Host "Success Rate: $successRate%" -ForegroundColor Green
} elseif ($passedTests -gt 0) {
    Write-Host "?? Some tests failed. Check API configuration." -ForegroundColor Yellow
    Write-Host "Success Rate: $successRate%" -ForegroundColor Yellow
} else {
    Write-Host "?? All tests failed. API may be down or misconfigured." -ForegroundColor Red
    Write-Host "Success Rate: $successRate%" -ForegroundColor Red
}

Write-Host ""
Write-Host "?? Quick Tips:" -ForegroundColor Cyan
Write-Host "  • Make sure the API is running on https://localhost:7170"
Write-Host "  • Check that Qdrant vector database is accessible"
Write-Host "  • Verify Gemini API key is configured for embeddings"
Write-Host "  • Ensure Content Manager records are embedded"

exit $($totalTests - $passedTests)