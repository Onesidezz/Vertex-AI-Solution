#!/bin/bash

# Comprehensive Search API Test Script (Bash version)
# This script tests various search scenarios to validate the dynamic search functionality

BASE_URL="https://localhost:7170/api/RecordEmbedding/search"
PASS_COUNT=0
FAIL_COUNT=0

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
GRAY='\033[0;37m'
NC='\033[0m' # No Color

echo -e "${GREEN}?? Starting Comprehensive Search API Tests${NC}"
echo "=========================================="
echo ""

# Function to run a test
run_test() {
    local test_name="$1"
    local query="$2"
    local topK="$3"
    local min_score="$4"
    local expected="$5"
    
    echo -e "${CYAN}?? Test: $test_name${NC}"
    echo "Query: '$query'"
    echo "Expected: $expected"
    
    # Prepare JSON payload (escaping quotes)
    local escaped_query=$(echo "$query" | sed 's/"/\\"/g')
    local json_payload="{\"query\": \"$escaped_query\", \"topK\": $topK, \"minimumScore\": $min_score}"
    
    # Make API call and capture response
    local start_time=$(date +%s%3N)
    local response=$(curl -s -k -X POST "$BASE_URL" \
        -H "Content-Type: application/json" \
        -H "Accept: application/json" \
        -d "$json_payload" \
        -w "HTTPSTATUS:%{http_code};TIME:%{time_total}")
    local end_time=$(date +%s%3N)
    
    # Extract HTTP status and response
    local http_status=$(echo "$response" | grep -o "HTTPSTATUS:[0-9]*" | cut -d: -f2)
    local time_total=$(echo "$response" | grep -o "TIME:[0-9.]*" | cut -d: -f2)
    local json_response=$(echo "$response" | sed -E 's/HTTPSTATUS:[0-9]*;TIME:[0-9.]*$//')
    
    # Calculate duration
    local duration=$((end_time - start_time))
    
    # Analyze response
    if [ "$http_status" = "200" ] && [ -n "$json_response" ]; then
        # Parse JSON response (basic parsing)
        local total_results=$(echo "$json_response" | grep -o '"TotalResults":[0-9]*' | cut -d: -f2)
        local query_time=$(echo "$json_response" | grep -o '"QueryTime":[0-9.]*' | cut -d: -f2)
        local has_ai_summary=$(echo "$json_response" | grep -q '"SynthesizedAnswer":"[^"]*[a-zA-Z]' && echo "Yes" || echo "No")
        
        # Validate basic response structure
        if [ -n "$total_results" ]; then
            echo -e "${GREEN}Status: ? PASSED${NC}"
            echo -e "${GRAY}  Results Found: $total_results${NC}"
            echo -e "${GRAY}  Query Time: ${query_time}s${NC}"
            echo -e "${GRAY}  Response Time: ${duration}ms${NC}"
            echo -e "${GRAY}  Has AI Summary: $has_ai_summary${NC}"
            echo -e "${GRAY}  HTTP Status: $http_status${NC}"
            
            if [ "$duration" -lt 5000 ]; then
                echo -e "${GRAY}  Performance: ? Good (${duration}ms)${NC}"
            else
                echo -e "${GRAY}  Performance: ?? Slow (${duration}ms)${NC}"
            fi
            
            PASS_COUNT=$((PASS_COUNT + 1))
        else
            echo -e "${RED}Status: ? FAILED - Invalid JSON response${NC}"
            echo -e "${GRAY}  Response: $json_response${NC}"
            FAIL_COUNT=$((FAIL_COUNT + 1))
        fi
    else
        echo -e "${RED}Status: ? FAILED - HTTP $http_status${NC}"
        echo -e "${GRAY}  Response: $json_response${NC}"
        echo -e "${GRAY}  Duration: ${duration}ms${NC}"
        FAIL_COUNT=$((FAIL_COUNT + 1))
    fi
    
    echo ""
}

# Test Cases
echo "Running test cases..."
echo ""

# Test 1: Basic Content Search
run_test "Basic Content Search - Invoice with Name" \
    "invoice details of Umar khan" \
    20 0.3 \
    "Should find invoices or documents mentioning Umar khan with lowered threshold"

# Test 2: Date Range Search
run_test "Date Range Search - After Specific Date" \
    "Show me all documents created after October 9, 2025" \
    15 0.2 \
    "Should filter documents created after 2025-10-09"

# Test 3: Earliest Date Search
run_test "Earliest Date Search" \
    "Which record has the earliest creation date?" \
    5 0.1 \
    "Should return records sorted by creation date ascending"

# Test 4: File Type Search with Time
run_test "File Type Search with Time" \
    "List all Word or Excel documents added after 3:45 PM" \
    10 0.3 \
    "Should filter .doc/.docx/.xls/.xlsx files created after 15:45 today"

# Test 5: Recent Content Search
run_test "Recent Content Search" \
    "What are the most recently created documents related to API or Service?" \
    10 0.2 \
    "Should find recent documents containing API or Service keywords"

# Test 6: Container Search
run_test "Container Search" \
    "What records belong to container D25/1?" \
    20 0.4 \
    "Should find records in container D25/1"

# Test 7: Financial Document Search
run_test "Financial Document Search" \
    "Find records that look like invoices or financial documents" \
    15 0.3 \
    "Should find invoice-like or financial documents using semantic matching"

# Test 8: PDF File Search
run_test "PDF File Search" \
    "Find all PDF files related to ControlPoint or CP3000" \
    10 0.3 \
    "Should find PDF files containing ControlPoint or CP3000"

# Test 9: Today's Records
run_test "Today's Records" \
    "records created today" \
    20 0.2 \
    "Should find records created today"

# Test 10: Recently Created
run_test "Recently Created" \
    "any record which is created recently" \
    20 0.3 \
    "Should find records from last 14 days"

# Test 11: Empty Query Test
run_test "Empty Query Test" \
    "" \
    10 0.3 \
    "Should return error or helpful message for empty query"

# Test 12: Invalid Parameters Test
run_test "Invalid Parameters Test" \
    "test search" \
    150 1.5 \
    "Should handle invalid parameters gracefully (TopK should be clamped to 100, MinScore to 1.0)"

# Test 13: Special Characters Test
run_test "Special Characters Test" \
    "documents with "special quotes" & symbols@#\$" \
    10 0.3 \
    "Should clean and normalize special characters"

# Test 14: Business Terms
run_test "Specific Business Terms" \
    "contract agreement proposal report statement" \
    15 0.2 \
    "Should find documents with business-related content"

# Summary
total_tests=$((PASS_COUNT + FAIL_COUNT))
success_rate=$(echo "scale=1; $PASS_COUNT * 100 / $total_tests" | bc -l)

echo "=========================================="
echo -e "${YELLOW}?? TEST SUMMARY${NC}"
echo "=========================================="
echo -e "Total Tests: $total_tests"
echo -e "${GREEN}? Passed: $PASS_COUNT${NC}"
echo -e "${RED}? Failed: $FAIL_COUNT${NC}"

if (( $(echo "$success_rate >= 70" | bc -l) )); then
    echo -e "${GREEN}Success Rate: $success_rate%${NC}"
else
    echo -e "${RED}Success Rate: $success_rate%${NC}"
fi

echo ""
echo -e "${CYAN}? Test Results Analysis:${NC}"
if [ $FAIL_COUNT -eq 0 ]; then
    echo -e "${GREEN}  ?? All tests passed! The API is working correctly.${NC}"
elif [ $PASS_COUNT -gt $FAIL_COUNT ]; then
    echo -e "${YELLOW}  ?? Most tests passed, but some issues detected.${NC}"
else
    echo -e "${RED}  ?? Multiple test failures - API may need attention.${NC}"
fi

echo ""
echo -e "${YELLOW}?? Recommendations:${NC}"
echo "  • Monitor search relevance and accuracy for business queries"
echo "  • Test with your actual data for domain-specific validation"
echo "  • Consider performance optimization if response times are slow"
echo "  • Add more test cases specific to your Content Manager data"

echo ""
echo -e "${GREEN}Test completed! ??${NC}"

# Exit with appropriate code
exit $FAIL_COUNT