using System.ComponentModel.DataAnnotations;
namespace FixPal.Models;
public class ProviderReview
{
    public int Id { get; set; }
    public int MaintenanceRequestId { get; set; }
    public MaintenanceRequest MaintenanceRequest { get; set; } = null!;
    public int ProviderProfileId { get; set; }
    public ProviderProfile ProviderProfile { get; set; } = null!;
    [Required] public string CustomerId { get; set; } = string.Empty;
    public ApplicationUser Customer { get; set; } = null!;
    [Range(1,5)] public int Rating { get; set; }
    [StringLength(1000)] public string? Comment { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
