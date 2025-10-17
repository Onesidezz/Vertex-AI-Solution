#!/bin/bash

# Quick Search API Test - Fast and Focused
# Tests only essential functionality with minimal overhead

BASE_URL="https://localhost:7170/api/RecordEmbedding/search"
TOTAL_TESTS=0
PASSED_TESTS=0

# Colors
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

echo -e "${GREEN}?? Quick Search API Test${NC}"
echo "================================"

# Function to run a quick test
quick_test() {
    local name="$1"
    local query="$2"
    local topK="$3"
    local min_score="$4"
    
    TOTAL_TESTS=$((TOTAL_TESTS + 1))
    
    echo -e "${CYAN}Test $TOTAL_TESTS: $name${NC}"
    
    # Escape quotes in query
    local escaped_query=$(echo "$query" | sed 's/"/\\"/g')
    local json_payload="{\"query\": \"$escaped_query\", \"topK\": $topK, \"minimumScore\": $min_score}"
    
    # Make API call with timeout
    local response=$(timeout 10s curl -s -k -X POST "$BASE_URL" \
        -H "Content-Type: application/json" \
        -H "Accept: application/json" \
        -d "$json_payload" 2>/dev/null)
    
    # Check if response contains expected JSON structure
    if echo "$response" | grep -q '"TotalResults"' && echo "$response" | grep -q '"QueryTime"'; then
        local total_results=$(echo "$response" | grep -o '"TotalResults":[0-9]*' | cut -d: -f2)
        local query_time=$(echo "$response" | grep -o '"QueryTime":[0-9.]*' | cut -d: -f2)
        
        echo -e "  ${GREEN}? PASSED${NC} - Found: $total_results results in ${query_time}s"
        PASSED_TESTS=$((PASSED_TESTS + 1))
    else
        echo -e "  ${RED}? FAILED${NC} - Invalid response"
        echo "  Response: ${response:0:100}..."
    fi
}

# Run essential tests only
echo "Running essential tests..."
echo

# Test 1: Basic search
quick_test "Basic Search" "invoice details of Umar khan" 20 0.3

# Test 2: Date search  
quick_test "Date Search" "recently created records" 10 0.2

# Test 3: File type search
quick_test "File Type Search" "PDF documents" 10 0.3

# Test 4: Empty query handling
quick_test "Empty Query" "" 10 0.3

# Test 5: Parameter validation
quick_test "Parameter Test" "test search" 150 1.5

echo
echo "================================"
echo -e "${YELLOW}?? QUICK TEST SUMMARY${NC}"
echo "================================"
echo "Total Tests: $TOTAL_TESTS"
echo -e "Passed: ${GREEN}$PASSED_TESTS${NC}"
echo -e "Failed: ${RED}$((TOTAL_TESTS - PASSED_TESTS))${NC}"

if [ $PASSED_TESTS -eq $TOTAL_TESTS ]; then
    echo -e "${GREEN}?? All tests passed! API is working.${NC}"
    exit 0
elif [ $PASSED_TESTS -gt 0 ]; then
    echo -e "${YELLOW}?? Some tests failed. Check API configuration.${NC}"
    exit 1
else
    echo -e "${RED}?? All tests failed. API may be down.${NC}"
    exit 2
fi