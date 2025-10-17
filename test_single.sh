#!/bin/bash

# Single Test for "invoice details of Umar khan" query
# Quick validation of the search functionality

echo "?? Testing: 'invoice details of Umar khan'"
echo "========================================"

# Test the specific query
response=$(curl -s -k -X POST "https://localhost:7170/api/RecordEmbedding/search" \
    -H "Content-Type: application/json" \
    -H "Accept: application/json" \
    -d '{
        "query": "invoice details of Umar khan",
        "topK": 20,
        "minimumScore": 0.3
    }' 2>/dev/null)

# Check if we got a valid response
if echo "$response" | grep -q '"TotalResults"'; then
    # Extract key information
    total_results=$(echo "$response" | grep -o '"TotalResults":[0-9]*' | cut -d: -f2)
    query_time=$(echo "$response" | grep -o '"QueryTime":[0-9.]*' | cut -d: -f2)
    
    echo "? API Response Received"
    echo "?? Results Found: $total_results"
    echo "??  Query Time: ${query_time}s"
    
    # Check if we have AI summary
    if echo "$response" | grep -q '"SynthesizedAnswer"'; then
        echo "?? AI Summary: Available"
    else
        echo "?? AI Summary: Not available"
    fi
    
    # Show first few results if any
    if [ "$total_results" -gt 0 ]; then
        echo ""
        echo "?? Sample Results:"
        echo "$response" | grep -o '"RecordTitle":"[^"]*"' | head -3 | sed 's/"RecordTitle":"//;s/"$//' | while read title; do
            echo "  • $title"
        done
    else
        echo ""
        echo "?? No results found. This could mean:"
        echo "  • No records match 'Umar khan' or 'invoice'"
        echo "  • Minimum score (0.3) is too high"
        echo "  • Records haven't been embedded yet"
        echo "  • Try lowering minimumScore to 0.1"
    fi
    
    echo ""
    echo "? Test Status: PASSED - API is working"
    
elif echo "$response" | grep -q "error\|Error\|exception"; then
    echo "? API Error Response:"
    echo "$response" | head -5
    echo ""
    echo "?? Check:"
    echo "  • API is running on https://localhost:7170"
    echo "  • Gemini API key is configured"
    echo "  • Qdrant vector database is accessible"
    
else
    echo "? No valid response received"
    echo "Raw response (first 200 chars):"
    echo "${response:0:200}..."
    echo ""
    echo "?? Possible issues:"
    echo "  • API server not running"
    echo "  • SSL/HTTPS certificate issues"
    echo "  • Network connectivity problems"
    echo "  • Port 7170 not accessible"
fi

echo ""
echo "?? To run with lower threshold:"
echo "curl -k -X POST 'https://localhost:7170/api/RecordEmbedding/search' \\"
echo "  -H 'Content-Type: application/json' \\"
echo "  -d '{\"query\": \"invoice details of Umar khan\", \"topK\": 20, \"minimumScore\": 0.1}'"