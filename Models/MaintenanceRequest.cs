using System.ComponentModel.DataAnnotations;
using FixPal.Models.Enums;
namespace FixPal.Models;
public class MaintenanceRequest
{
    public int Id { get; set; }
    [Required] public string CustomerId { get; set; } = string.Empty;
    public ApplicationUser Customer { get; set; } = null!;
    public int? ProviderProfileId { get; set; }
    public ProviderProfile? ProviderProfile { get; set; }
    public int ServiceCategoryId { get; set; }
    public ServiceCategory ServiceCategory { get; set; } = null!;
    public int AreaId { get; set; }
    public Area Area { get; set; } = null!;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    [Required, StringLength(150)] public string Title { get; set; } = string.Empty;
    [Required, StringLength(2000)] public string Description { get; set; } = string.Empty;
    public RequestType RequestType { get; set; }
    public MaintenanceRequestStatus Status { get; set; } = MaintenanceRequestStatus.Pending;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? AcceptedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
