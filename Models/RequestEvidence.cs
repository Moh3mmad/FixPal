using System.ComponentModel.DataAnnotations;
namespace FixPal.Models;
public enum EvidenceKind { Before = 1, After = 2 }
public class RequestEvidence
{
    public int Id { get; set; }
    public int MaintenanceRequestId { get; set; }
    public MaintenanceRequest MaintenanceRequest { get; set; } = null!;
    [Required] public string UploadedById { get; set; } = string.Empty;
    public ApplicationUser UploadedBy { get; set; } = null!;
    public EvidenceKind Kind { get; set; }
    [Required, StringLength(100)] public string StorageKey { get; set; } = string.Empty;
    [StringLength(2000)] public string? StorageUrl { get; set; }
    [Required, StringLength(50)] public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
