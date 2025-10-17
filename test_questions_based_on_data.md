# Test Questions and Expected Answers Based on Content Manager Data

## Data Overview
- **Container**: 25/1
- **Date Range**: October 9-10, 2025
- **Total Records**: 17 documents
- **File Types**: PDF, Word (.docx), Excel (.xlsx), PowerPoint (.pptx)

---

## Test Questions

### 1. SPECIFIC DATE QUERIES

#### Q1.1: "Show me all records created on October 9, 2025"
**Expected Answer**: Should return 9 records:
- 25/1/3 - Project Proposal.docx (10/09/2025 9:15:30 AM)
- 25/1/4 - Budget Report.xlsx (10/09/2025 10:30:45 AM)
- 25/1/5 - Meeting Minutes.docx (10/09/2025 11:45:12 AM)
- 25/1/6 - Marketing Plan.pptx (10/09/2025 1:20:33 PM)
- 25/1/7 - Sales Data.xlsx (10/09/2025 2:35:18 PM)
- 25/1/8 - Technical Specification.pdf (10/09/2025 3:50:27 PM)
- 25/1/9 - Employee Handbook.pdf (10/09/2025 4:15:42 PM)
- 25/1/10 - Training Materials.pptx (10/09/2025 5:30:55 PM)
- 25/1/11 - Quarterly Review.docx (10/09/2025 6:45:10 PM)

#### Q1.2: "Show me all records created on October 10, 2025"
**Expected Answer**: Should return 8 records:
- 25/1/12 - Contract Agreement.pdf (10/10/2025 8:00:15 AM)
- 25/1/13 - Invoice Template.xlsx (10/10/2025 9:20:30 AM)
- 25/1/14 - Policy Document.docx (10/10/2025 10:35:45 AM)
- 25/1/15 - Product Roadmap.pptx (10/10/2025 11:50:20 AM)
- 25/1/16 - Risk Assessment.xlsx (10/10/2025 1:10:35 PM)
- 25/1/17 - User Guide.pdf (10/10/2025 2:25:48 PM)
- 25/1/18 - Compliance Report.docx (10/10/2025 3:40:52 PM)
- 25/1/19 - Strategic Plan.pptx (10/10/2025 4:55:10 PM)

---

### 2. TIME-OF-DAY QUERIES

#### Q2.1: "Show me documents created in the morning"
**Expected Answer**: Should return documents created between 5:00 AM - 12:00 PM:
- 25/1/3 - Project Proposal.docx (10/09/2025 9:15:30 AM) - Morning
- 25/1/4 - Budget Report.xlsx (10/09/2025 10:30:45 AM) - Morning
- 25/1/5 - Meeting Minutes.docx (10/09/2025 11:45:12 AM) - Morning
- 25/1/12 - Contract Agreement.pdf (10/10/2025 8:00:15 AM) - Morning
- 25/1/13 - Invoice Template.xlsx (10/10/2025 9:20:30 AM) - Morning
- 25/1/14 - Policy Document.docx (10/10/2025 10:35:45 AM) - Morning
- 25/1/15 - Product Roadmap.pptx (10/10/2025 11:50:20 AM) - Morning

#### Q2.2: "Show me documents created in the afternoon"
**Expected Answer**: Should return documents created between 12:00 PM - 5:00 PM:
- 25/1/6 - Marketing Plan.pptx (10/09/2025 1:20:33 PM) - Afternoon
- 25/1/7 - Sales Data.xlsx (10/09/2025 2:35:18 PM) - Afternoon
- 25/1/8 - Technical Specification.pdf (10/09/2025 3:50:27 PM) - Afternoon
- 25/1/9 - Employee Handbook.pdf (10/09/2025 4:15:42 PM) - Afternoon
- 25/1/16 - Risk Assessment.xlsx (10/10/2025 1:10:35 PM) - Afternoon
- 25/1/17 - User Guide.pdf (10/10/2025 2:25:48 PM) - Afternoon
- 25/1/18 - Compliance Report.docx (10/10/2025 3:40:52 PM) - Afternoon
- 25/1/19 - Strategic Plan.pptx (10/10/2025 4:55:10 PM) - Afternoon

#### Q2.3: "Show me documents created in the evening"
**Expected Answer**: Should return documents created between 5:00 PM - 9:00 PM:
- 25/1/10 - Training Materials.pptx (10/09/2025 5:30:55 PM) - Evening
- 25/1/11 - Quarterly Review.docx (10/09/2025 6:45:10 PM) - Evening

---

### 3. BETWEEN DATE RANGE QUERIES

#### Q3.1: "Show me records created between October 9, 2025 and October 10, 2025"
**Expected Answer**: Should return all 17 records from both days

#### Q3.2: "Show me records created between 09/10/2025 and 10/10/2025"
**Expected Answer**: Should return all 17 records (both US format MM/DD/YYYY and European format DD/MM/YYYY should work)

#### Q3.3: "Show me records from 10/09/2025 to 10/10/2025"
**Expected Answer**: Should return all 17 records

---

### 4. BETWEEN TIME RANGE ON SPECIFIC DATE

#### Q4.1: "Show me records created between 9 AM and 12 PM on October 9, 2025"
**Expected Answer**: Should return 3 records:
- 25/1/3 - Project Proposal.docx (10/09/2025 9:15:30 AM)
- 25/1/4 - Budget Report.xlsx (10/09/2025 10:30:45 AM)
- 25/1/5 - Meeting Minutes.docx (10/09/2025 11:45:12 AM)

#### Q4.2: "Show me records created between 1 PM and 5 PM on October 9, 2025"
**Expected Answer**: Should return 4 records:
- 25/1/6 - Marketing Plan.pptx (10/09/2025 1:20:33 PM)
- 25/1/7 - Sales Data.xlsx (10/09/2025 2:35:18 PM)
- 25/1/8 - Technical Specification.pdf (10/09/2025 3:50:27 PM)
- 25/1/9 - Employee Handbook.pdf (10/09/2025 4:15:42 PM)

#### Q4.3: "Show me records created between 8 AM and 11 AM on October 10, 2025"
**Expected Answer**: Should return 3 records:
- 25/1/12 - Contract Agreement.pdf (10/10/2025 8:00:15 AM)
- 25/1/13 - Invoice Template.xlsx (10/10/2025 9:20:30 AM)
- 25/1/14 - Policy Document.docx (10/10/2025 10:35:45 AM)

---

### 5. AROUND TIME QUERIES (Fuzzy Time Matching)

#### Q5.1: "Show me records created around 9 AM"
**Expected Answer**: Should return records within ±30 minutes of 9:00 AM:
- 25/1/3 - Project Proposal.docx (10/09/2025 9:15:30 AM) - within 30 min window
- 25/1/13 - Invoice Template.xlsx (10/10/2025 9:20:30 AM) - within 30 min window

#### Q5.2: "Show me records created around 10:30 AM"
**Expected Answer**: Should return records within ±30 minutes of 10:30 AM:
- 25/1/4 - Budget Report.xlsx (10/09/2025 10:30:45 AM) - exact match
- 25/1/14 - Policy Document.docx (10/10/2025 10:35:45 AM) - within 30 min window

#### Q5.3: "Show me records created around noon"
**Expected Answer**: Should return records within ±30 minutes of 12:00 PM:
- 25/1/5 - Meeting Minutes.docx (10/09/2025 11:45:12 AM) - within 30 min window
- 25/1/15 - Product Roadmap.pptx (10/10/2025 11:50:20 AM) - within 30 min window

#### Q5.4: "Show me records created around 2 PM"
**Expected Answer**: Should return records within ±30 minutes of 2:00 PM:
- 25/1/7 - Sales Data.xlsx (10/09/2025 2:35:18 PM) - slightly outside but close
- 25/1/17 - User Guide.pdf (10/10/2025 2:25:48 PM) - within 30 min window

---

### 6. FILE TYPE + DATE COMBINATION QUERIES

#### Q6.1: "Show me PDF files created on October 9, 2025"
**Expected Answer**: Should return 2 PDF records:
- 25/1/8 - Technical Specification.pdf (10/09/2025 3:50:27 PM)
- 25/1/9 - Employee Handbook.pdf (10/09/2025 4:15:42 PM)

#### Q6.2: "Show me Excel files created between October 9 and October 10, 2025"
**Expected Answer**: Should return 3 Excel records:
- 25/1/4 - Budget Report.xlsx (10/09/2025 10:30:45 AM)
- 25/1/7 - Sales Data.xlsx (10/09/2025 2:35:18 PM)
- 25/1/13 - Invoice Template.xlsx (10/10/2025 9:20:30 AM)
- 25/1/16 - Risk Assessment.xlsx (10/10/2025 1:10:35 PM)

#### Q6.3: "Show me Word documents created in the morning"
**Expected Answer**: Should return Word docs created 5 AM - 12 PM:
- 25/1/3 - Project Proposal.docx (10/09/2025 9:15:30 AM)
- 25/1/5 - Meeting Minutes.docx (10/09/2025 11:45:12 AM)
- 25/1/14 - Policy Document.docx (10/10/2025 10:35:45 AM)

#### Q6.4: "Show me PowerPoint files created in the afternoon on October 10, 2025"
**Expected Answer**: Should return PowerPoint files created 12 PM - 5 PM on Oct 10:
- 25/1/19 - Strategic Plan.pptx (10/10/2025 4:55:10 PM)

---

### 7. SORTING QUERIES

#### Q7.1: "Which record has the earliest creation date?"
**Expected Answer**: Should return:
- 25/1/12 - Contract Agreement.pdf (10/10/2025 8:00:15 AM)
Wait, that's not earliest. The earliest should be:
- 25/1/3 - Project Proposal.docx (10/09/2025 9:15:30 AM)

#### Q7.2: "What are the most recently created documents?"
**Expected Answer**: Should return (sorted by most recent first):
1. 25/1/11 - Quarterly Review.docx (10/09/2025 6:45:10 PM)
2. 25/1/10 - Training Materials.pptx (10/09/2025 5:30:55 PM)
3. 25/1/19 - Strategic Plan.pptx (10/10/2025 4:55:10 PM)

#### Q7.3: "Show me the latest files from October 10, 2025"
**Expected Answer**: Should return most recent from Oct 10:
1. 25/1/19 - Strategic Plan.pptx (10/10/2025 4:55:10 PM)
2. 25/1/18 - Compliance Report.docx (10/10/2025 3:40:52 PM)
3. 25/1/17 - User Guide.pdf (10/10/2025 2:25:48 PM)

#### Q7.4: "Find the oldest records"
**Expected Answer**: Should return oldest records first:
1. 25/1/3 - Project Proposal.docx (10/09/2025 9:15:30 AM)
2. 25/1/4 - Budget Report.xlsx (10/09/2025 10:30:45 AM)
3. 25/1/5 - Meeting Minutes.docx (10/09/2025 11:45:12 AM)

---

### 8. COMBINED COMPLEX QUERIES

#### Q8.1: "Show me Excel files created in the morning between October 9 and October 10, 2025"
**Expected Answer**: Should return Excel files created 5 AM - 12 PM:
- 25/1/4 - Budget Report.xlsx (10/09/2025 10:30:45 AM)
- 25/1/13 - Invoice Template.xlsx (10/10/2025 9:20:30 AM)

#### Q8.2: "Find PDF documents created in the afternoon on October 9, 2025"
**Expected Answer**: Should return PDF files created 12 PM - 5 PM on Oct 9:
- 25/1/8 - Technical Specification.pdf (10/09/2025 3:50:27 PM)
- 25/1/9 - Employee Handbook.pdf (10/09/2025 4:15:42 PM)

#### Q8.3: "Show me PowerPoint files created between 10 AM and 6 PM on October 9, 2025"
**Expected Answer**: Should return PowerPoint files in that time range:
- 25/1/6 - Marketing Plan.pptx (10/09/2025 1:20:33 PM)
- 25/1/10 - Training Materials.pptx (10/09/2025 5:30:55 PM)

---

### 9. CONTAINER-SPECIFIC QUERIES

#### Q9.1: "Show me all records in container 25/1"
**Expected Answer**: Should return all 17 records

#### Q9.2: "How many documents are in container 25/1 created on October 9, 2025?"
**Expected Answer**: 9 documents

#### Q9.3: "What types of files are in container 25/1?"
**Expected Answer**: PDF, Word (.docx), Excel (.xlsx), PowerPoint (.pptx)

---

### 10. SPECIFIC TIME PRECISION QUERIES

#### Q10.1: "Show me records created after 3 PM on October 9, 2025"
**Expected Answer**: Should return records after 3:00 PM:
- 25/1/8 - Technical Specification.pdf (10/09/2025 3:50:27 PM)
- 25/1/9 - Employee Handbook.pdf (10/09/2025 4:15:42 PM)
- 25/1/10 - Training Materials.pptx (10/09/2025 5:30:55 PM)
- 25/1/11 - Quarterly Review.docx (10/09/2025 6:45:10 PM)

#### Q10.2: "Show me records created before 10 AM on October 10, 2025"
**Expected Answer**: Should return records before 10:00 AM:
- 25/1/12 - Contract Agreement.pdf (10/10/2025 8:00:15 AM)
- 25/1/13 - Invoice Template.xlsx (10/10/2025 9:20:30 AM)

#### Q10.3: "Show me records created between 11 AM and 2 PM across both days"
**Expected Answer**: Should return records in that time range:
- 25/1/5 - Meeting Minutes.docx (10/09/2025 11:45:12 AM)
- 25/1/6 - Marketing Plan.pptx (10/09/2025 1:20:33 PM)
- 25/1/15 - Product Roadmap.pptx (10/10/2025 11:50:20 AM)
- 25/1/16 - Risk Assessment.xlsx (10/10/2025 1:10:35 PM)

---

## Testing Notes

### Important Considerations:
1. **Time Zone**: All times appear to be in local time zone
2. **Date Format**: System should handle both US (MM/DD/YYYY) and European (DD/MM/YYYY) formats
3. **Time Precision**: All records have timestamps down to the second (HH:mm:ss)
4. **Semantic Matching**: Natural language queries should work (e.g., "morning", "afternoon", "around noon")

### Test Methodology in Postman:
```json
POST http://localhost:5000/api/RecordEmbedding/search
Content-Type: application/json

{
    "query": "YOUR_QUERY_HERE",
    "topK": 20,
    "minimumScore": 0.3
}
```

### Success Criteria:
- ✅ All date range queries return correct records
- ✅ Time-of-day queries (morning, afternoon, evening) work correctly
- ✅ "Between" queries with various formats work
- ✅ Time range queries on specific dates work
- ✅ "Around" time queries with ±30 min window work
- ✅ Combined queries (file type + date + time) work
- ✅ Sorting queries return results in correct order
- ✅ Results include proper DateCreated with time in format: MM/DD/YYYY HH:mm:ss
