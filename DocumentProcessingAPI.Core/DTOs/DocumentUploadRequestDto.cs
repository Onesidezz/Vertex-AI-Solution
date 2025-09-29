using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace DocumentProcessingAPI.Core.DTOs;

public class DocumentUploadRequestDto
{
    [Required]
    public IFormFile File { get; set; } = null!;

    [StringLength(36)]
    public string? UserId { get; set; }

    public bool ProcessImmediately { get; set; } = true;
}