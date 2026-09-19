using System.ComponentModel.DataAnnotations;

namespace FixPal.Models;

// Intentionally self-published profile content, never a RequestEvidence reference.
public class ProviderPortfolioItem
{
    public int Id { get; set; }
    public int ProviderProfileId { get; set; }
    public ProviderProfile ProviderProfile { get; set; } = null!;
    [Required, StringLength(120)] public string Title { get; set; } = string.Empty;
    [StringLength(500)] public string? Description { get; set; }
    [Required, StringLength(100)] public string StorageKey { get; set; } = string.Empty;
    [Required, StringLength(50)] public string ContentType { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public bool IsArchived { get; set; }
}
