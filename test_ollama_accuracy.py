"""
DocumentProcessingAPI — Ollama Accuracy Test
=============================================
Run this script on the machine where Ollama is running to test
keyword extraction and answer synthesis with the sample records.

Usage:
    python test_ollama_accuracy.py

Requirements:
    pip install requests
"""

import json
import re
import time
import requests

OLLAMA_URL = "http://localhost:11434"

# ─────────────────────────────────────────────────────────────────────────────
# SAMPLE DATA (the 4 records you provided)
# ─────────────────────────────────────────────────────────────────────────────

RECORDS = [
    {
        "record_uri": 558,
        "record_title": "iuoiinno",
        "date_created": "01/28/2026 10:51:56",
        "record_type": "Container",
        "assignee": "UKHAN2",
        "all_parts": "D26/2: iuoiinno",
        "chunk_content": """Record Title: iuoiinno
Record URI: 558
Date Created: 01/28/2026 10:51:56
Alternative Date Formats: 28/01/2026, 2026-01-28, January 28, 2026, Jan 28, 2026, 28 January 2026
Time of Day: morning, late morning
Month: January 2026
Quarter: Q1 2026
Day of Week: Wednesday
Year: 2026
Week of Year: Week5 of 2026
Record Type: Container
Assignee: UKHAN2
All Parts: D26/2: iuoiinno
Access Control: TRIM.SDK.TrimAccessControlList"""
    },
    {
        "record_uri": 557,
        "record_title": "External link test",
        "date_created": "01/28/2026 10:20:05",
        "record_type": "Container",
        "assignee": "UKHAN2",
        "all_parts": "D26/139: External link test",
        "chunk_content": """Record Title: External link test
Record URI: 557
Date Created: 01/28/2026 10:20:05
Alternative Date Formats: 28/01/2026, 2026-01-28, January 28, 2026, Jan 28, 2026, 28 January 2026
Time of Day: morning, late morning
Month: January 2026
Quarter: Q1 2026
Day of Week: Wednesday
Year: 2026
Week of Year: Week5 of 2026
Record Type: Container
Assignee: UKHAN2
All Parts: D26/139: External link test
Access Control: TRIM.SDK.TrimAccessControlList"""
    },
    {
        "record_uri": 550,
        "record_title": "ouhiuhiu",
        "date_created": "01/23/2026 19:05:49",
        "record_type": "Document File",
        "assignee": "UKHAN2",
        "all_parts": "D26/133: ouhiuhiu",
        "chunk_content": """Record Title: ouhiuhiu
Record URI: 550
Date Created: 01/23/2026 19:05:49
Alternative Date Formats: 23/01/2026, 2026-01-23, January 23, 2026, Jan 23, 2026, 23 January 2026
Time of Day: evening
Month: January 2026
Quarter: Q1 2026
Day of Week: Friday
Year: 2026
Week of Year: Week4 of 2026
Record Type: Document File
Assignee: UKHAN2
All Parts: D26/133: ouhiuhiu
Access Control: TRIM.SDK.TrimAccessControlList
--- Document Content ---
presented to"""
    },
    {
        "record_uri": 549,
        "record_title": "cm_architecture_diagram (2)",
        "date_created": "01/22/2026 15:25:03",
        "record_type": "Document File",
        "assignee": "UKHAN2",
        "all_parts": "D26/132: cm_architecture_diagram (2)",
        "chunk_content": """Record Title: cm_architecture_diagram (2)
Record URI: 549
Date Created: 01/22/2026 15:25:03
Alternative Date Formats: 22/01/2026, 2026-01-22, January 22, 2026, Jan 22, 2026, 22 January 2026
Time of Day: afternoon, late afternoon
Month: January 2026
Quarter: Q1 2026
Day of Week: Thursday
Year: 2026
Week of Year: Week4 of 2026
Record Type: Document File
Container: 26/66
Assignee: UKHAN2
All Parts: D26/132: cm_architecture_diagram (2)
Access Control: TRIM.SDK.TrimAccessControlList
--- Document Content ---
[PROCESSING ERROR] Failed to extract text from file: File type '.svg' is not supported."""
    }
]

# Test queries
TEST_QUERIES = [
    "Show me all containers assigned to UKHAN2",
    "Find architecture diagram documents",
    "What records were created on Wednesday January 28",
    "Show document files from January 2026",
    "external link",
    "Find records created in the evening",
]

# ─────────────────────────────────────────────────────────────────────────────
# HELPERS
# ─────────────────────────────────────────────────────────────────────────────

def get_available_models():
    """List models available in Ollama."""
    try:
        r = requests.get(f"{OLLAMA_URL}/api/tags", timeout=5)
        r.raise_for_status()
        data = r.json()
        return [m["name"] for m in data.get("models", [])]
    except Exception as e:
        return []


def call_ollama(model: str, prompt: str, system: str = None, num_ctx: int = 4096,
                num_predict: int = 512, temperature: float = 0.1) -> tuple[str, float]:
    """Call Ollama /api/generate. Returns (response_text, elapsed_seconds)."""
    body = {
        "model": model,
        "prompt": prompt,
        "stream": False,
        "options": {
            "temperature": temperature,
            "top_p": 0.9,
            "top_k": 20,
            "num_predict": num_predict,
            "num_ctx": num_ctx,
            "repeat_penalty": 1.1,
        }
    }
    if system:
        body["system"] = system

    start = time.time()
    r = requests.post(f"{OLLAMA_URL}/api/generate", json=body, timeout=300)
    elapsed = time.time() - start
    r.raise_for_status()
    return r.json().get("response", ""), round(elapsed, 2)


# ─────────────────────────────────────────────────────────────────────────────
# PARSING — mirrors the fixed ParseKeywordsFromGeminiResponse in C#
# ─────────────────────────────────────────────────────────────────────────────

def parse_keywords(response: str) -> list[str]:
    """4-strategy JSON array parser matching the fixed C# method."""
    if not response or not response.strip():
        return []

    cleaned = response.strip()

    # Strategy 1: direct parse
    result = _try_parse_json_array(cleaned)
    if result is not None:
        return result

    # Strategy 2: strip markdown fences
    stripped = re.sub(r'^```(?:json)?\s*', '', cleaned, flags=re.MULTILINE)
    stripped = re.sub(r'```\s*$', '', stripped, flags=re.MULTILINE).strip()
    if stripped != cleaned:
        result = _try_parse_json_array(stripped)
        if result is not None:
            return result

    # Strategy 3: regex — find any JSON array in response
    for match in re.finditer(r'\[[\s\S]*?\]', response):
        result = _try_parse_json_array(match.group())
        if result is not None:
            return result

    # Strategy 4: extract all quoted strings
    quoted = re.findall(r'"([^"\\]*(?:\\.[^"\\]*)*)"', response)
    filtered = [q.strip() for q in quoted if len(q.strip()) > 1]
    if filtered:
        print(f"  ⚠️  Strategy 4 (quoted strings fallback) used — {len(filtered)} strings")
        return filtered

    print(f"  ❌ All parse strategies failed. Raw: {response[:150]}")
    return []


def _try_parse_json_array(text: str) -> list[str] | None:
    text = text.strip()
    if not text.startswith("["):
        return None
    try:
        parsed = json.loads(text)
        if isinstance(parsed, list):
            return [str(item).strip() for item in parsed if str(item).strip()]
    except json.JSONDecodeError:
        pass
    return None


# ─────────────────────────────────────────────────────────────────────────────
# TEST 1: KEYWORD EXTRACTION
# ─────────────────────────────────────────────────────────────────────────────

def test_keyword_extraction(model: str):
    print(f"\n{'='*70}")
    print(f"  TEST 1: KEYWORD EXTRACTION  |  Model: {model}")
    print(f"{'='*70}")

    system_prompt = (
        "You are a search keyword extractor. "
        "You ONLY output a valid JSON array of strings. "
        "No markdown, no explanation, no extra text — just the JSON array."
    )

    for query in TEST_QUERIES:
        prompt = f"""Extract the meaningful content keywords from this search query.

QUERY: "{query}"

Rules:
1. Extract: names, topics, specific terms, technical terms, numbers with context (e.g. "17 years", "Q3")
2. Exclude: calendar dates (January 2026, 2026-01-28), action words (find, show, get), file type words (PDF, Word), generic words (documents, records, files)
3. If the query is ONLY about dates or file types, return []

Examples:
Query: "Find documents about WEAI" → ["WEAI"]
Query: "resumes with 5+ years Python experience" → ["resumes", "5+ years", "Python", "experience"]
Query: "Show me documents from January 2026" → []
Query: "financial reports Q3" → ["financial reports", "Q3"]

Return ONLY a JSON array:"""

        try:
            response, elapsed = call_ollama(
                model, prompt,
                system=system_prompt,
                num_ctx=2048,
                num_predict=256
            )
            keywords = parse_keywords(response)

            print(f"\n  Query : {query}")
            print(f"  Raw   : {response.strip()[:120]}")
            print(f"  ✅ Keywords ({len(keywords)}, {elapsed}s): {keywords}")

        except Exception as e:
            print(f"\n  Query : {query}")
            print(f"  ❌ Error: {e}")


# ─────────────────────────────────────────────────────────────────────────────
# TEST 2: ANSWER SYNTHESIS
# ─────────────────────────────────────────────────────────────────────────────

def build_context(records: list[dict]) -> str:
    lines = ["Context from documents:", "---"]
    for r in records:
        lines.append(f"[Record {r['record_uri']}: {r['record_title']}]")
        lines.append(r["chunk_content"])
        lines.append("")
    lines.append("---")
    return "\n".join(lines)


def test_answer_synthesis(model: str):
    print(f"\n{'='*70}")
    print(f"  TEST 2: ANSWER SYNTHESIS  |  Model: {model}")
    print(f"{'='*70}")

    system_prompt = (
        "You are a helpful document search assistant. "
        "Answer questions accurately using ONLY the provided context. "
        "Do not hallucinate or add information not present in the context."
    )

    synthesis_queries = [
        ("What containers does UKHAN2 have?", RECORDS[:2]),
        ("When was the architecture diagram created?", [RECORDS[3]]),
        ("Which records were created on a Friday?", RECORDS),
        ("What document files exist and what are their titles?", RECORDS),
        ("Summarise all records created in week 4 of 2026", RECORDS),
    ]

    for query, relevant_records in synthesis_queries:
        context = build_context(relevant_records)
        prompt = f"""Question: {query}

{context}

Instructions:
1. Read the context carefully and find information that answers the question
2. Provide a direct answer in 2-4 sentences
3. Only use information from the context above
4. If the answer is not in the context, say so
5. List the relevant record URIs at the end

Answer:"""

        try:
            response, elapsed = call_ollama(
                model, prompt,
                system=system_prompt,
                num_ctx=8192,
                num_predict=512
            )
            print(f"\n  Question : {query}")
            print(f"  Answer   : {response.strip()}")
            print(f"  ⏱️  Time: {elapsed}s")
            print()

        except Exception as e:
            print(f"\n  Question : {query}")
            print(f"  ❌ Error: {e}")


# ─────────────────────────────────────────────────────────────────────────────
# TEST 3: JSON PARSER ROBUSTNESS (no model needed)
# ─────────────────────────────────────────────────────────────────────────────

def test_parser_robustness():
    print(f"\n{'='*70}")
    print(f"  TEST 3: JSON PARSER ROBUSTNESS (no model needed)")
    print(f"{'='*70}")

    test_cases = [
        # (input_string, expected_result, description)
        ('["WEAI"]',                              ["WEAI"],                    "Clean JSON"),
        ('["containers", "UKHAN2"]',              ["containers", "UKHAN2"],    "Clean JSON multi"),
        ('```json\n["architecture"]\n```',         ["architecture"],            "Markdown code fence"),
        ('```\n["external link"]\n```',            ["external link"],           "Plain code fence"),
        ('Here are keywords: ["diagram", "Q3"]', ["diagram", "Q3"],           "Text before array"),
        ('The keywords are:\n["iuoiinno"].',       ["iuoiinno"],               "Text before + dot after"),
        ('[]',                                    [],                          "Empty array"),
        ('[\n  "containers",\n  "UKHAN2"\n]',     ["containers", "UKHAN2"],   "Indented JSON"),
        ('"External link test"',                  ["External link test"],      "Single string (no brackets)"),
        ('Not a JSON response at all.',           [],                          "No JSON (empty expected)"),
    ]

    passed = 0
    failed = 0
    for raw, expected, description in test_cases:
        result = parse_keywords(raw)
        ok = result == expected
        status = "✅ PASS" if ok else "❌ FAIL"
        if ok:
            passed += 1
        else:
            failed += 1
        print(f"  {status} | {description}")
        if not ok:
            print(f"         Expected: {expected}")
            print(f"         Got     : {result}")

    print(f"\n  Results: {passed} passed, {failed} failed out of {len(test_cases)} tests")


# ─────────────────────────────────────────────────────────────────────────────
# MAIN
# ─────────────────────────────────────────────────────────────────────────────

def main():
    print("\n" + "="*70)
    print("  DocumentProcessingAPI — Ollama Accuracy Test")
    print("="*70)

    # Check Ollama
    print(f"\nChecking Ollama at {OLLAMA_URL}...")
    available_models = get_available_models()
    if not available_models:
        print("  ❌ Ollama not reachable or no models installed.")
        print("  Make sure Ollama is running: ollama serve")
        print("\n  Running parser robustness test only (no model needed)...")
        test_parser_robustness()
        return

    print(f"  ✅ Ollama is running")
    print(f"  Available models: {available_models}")

    # Choose model — prefer llama3.1:8b, fall back to whatever is available
    preferred = ["llama3.1:8b", "llama3.1:8b-instruct-q4_0", "qwen2.5:7b", "mistral:7b-instruct-v0.3",
                 "gemma3:12b", "gemma2:9b", "gemma:7b"]
    model = None
    for pref in preferred:
        if any(pref in m for m in available_models):
            model = next(m for m in available_models if pref in m)
            break

    if not model:
        model = available_models[0]
        print(f"  ⚠️  None of the preferred models found. Using: {model}")
    else:
        print(f"  ✅ Using model: {model}")

    # Run all tests
    test_parser_robustness()
    test_keyword_extraction(model)
    test_answer_synthesis(model)

    print(f"\n{'='*70}")
    print("  ALL TESTS COMPLETE")
    print(f"{'='*70}\n")


if __name__ == "__main__":
    main()
