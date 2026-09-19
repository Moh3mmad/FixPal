using System.ComponentModel.DataAnnotations;
namespace FixPal.Models;
public enum EvidenceKind { General = 0, Before = 1, After = 2 }
public class RequestEvidence
{
    public int Id { get; set; }
    public int MaintenanceRequestId { get; set; }
    public MaintenanceRequest MaintenanceRequest { get; set; } = null!;
    [Required] public string UploadedById { get; set; } = string.Empty;
    public ApplicationUser UploadedBy { get; set; } = null!;
    public EvidenceKind Kind { get; set; }
    // False for historical rows: the old endpoint did not enforce semantic ownership.
    public bool OwnershipChecked { get; set; }
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public EvidenceKind DisplayKind => OwnershipChecked && Kind is EvidenceKind.Before or EvidenceKind.After ? Kind : EvidenceKind.General;
    [Required, StringLength(100)] public string StorageKey { get; set; } = string.Empty;
    [StringLength(2000)] public string? StorageUrl { get; set; }
    [Required, StringLength(50)] public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
