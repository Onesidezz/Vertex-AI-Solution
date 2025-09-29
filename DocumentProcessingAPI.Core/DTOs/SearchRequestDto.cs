using System.ComponentModel.DataAnnotations;

namespace DocumentProcessingAPI.Core.DTOs;

public class SearchRequestDto
{
    [Required]
    [StringLength(1000, MinimumLength = 1)]
    public string Query { get; set; } = string.Empty;

    [Range(1, 50)]
    public int TopK { get; set; } = 5;

    [Range(0.0, 1.0)]
    public float MinimumScore { get; set; } = 0.0f;

    public Guid? DocumentId { get; set; }

    [Range(1, 100)]
    public int PageNumber { get; set; } = 1;

    [Range(10, 100)]
    public int PageSize { get; set; } = 20;
}

public class SearchResponseDto
{
    public string Query { get; set; } = string.Empty;
    public List<SearchResultDto> Results { get; set; } = new();
    public int TotalResults { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public float QueryTime { get; set; }

    // Answer synthesis for NLP questions
    public string? SynthesizedAnswer { get; set; }
    public QuestionType QuestionType { get; set; } = QuestionType.General;
    public float AnswerConfidence { get; set; }
}

public enum QuestionType
{
    General,
    Definition,    // "what is", "what are"
    Procedure,     // "how to", "how do"
    Example,       // "example", "sample"
    Location,      // "where", "which"
    Comparison     // "difference", "vs"
}

public class SearchResultDto
{
    public Guid ChunkId { get; set; }
    public Guid DocumentId { get; set; }
    public string DocumentName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public float RelevanceScore { get; set; }
    public int PageNumber { get; set; }
    public int ChunkSequence { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

// RAG DTOs
public class RagRequestDto
{
    [Required]
    [StringLength(1000, MinimumLength = 1)]
    public string Question { get; set; } = string.Empty;

    [Range(1, 20)]
    public int MaxSources { get; set; } = 5;

    [Range(0.0, 1.0)]
    public float MinimumScore { get; set; } = 0.3f;

    public Guid? DocumentId { get; set; }

    public bool IncludeSourceText { get; set; } = true;

    public bool StreamResponse { get; set; } = false;

    [Range(100, 4000)]
    public int MaxContextLength { get; set; } = 2000;
}

public class RagResponseDto
{
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public List<RagSourceDto> Sources { get; set; } = new();
    public float ConfidenceScore { get; set; }
    public float ResponseTime { get; set; }
    public string Model { get; set; } = string.Empty;
    public int TokensUsed { get; set; }
}

public class RagStreamResponseDto
{
    public string Delta { get; set; } = string.Empty;
    public bool IsComplete { get; set; }
    public List<RagSourceDto>? Sources { get; set; }
    public float? ConfidenceScore { get; set; }
}

public class RagSourceDto
{
    public Guid ChunkId { get; set; }
    public Guid DocumentId { get; set; }
    public string DocumentName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public float RelevanceScore { get; set; }
    public int PageNumber { get; set; }
    public int ChunkSequence { get; set; }
}