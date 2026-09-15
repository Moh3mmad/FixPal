using System.ComponentModel.DataAnnotations;
namespace FixPal.Models;
public class RequestQuote
{
    public int Id { get; set; }
    public int MaintenanceRequestId { get; set; }
    public MaintenanceRequest MaintenanceRequest { get; set; } = null!;
    public int ProviderProfileId { get; set; }
    public ProviderProfile ProviderProfile { get; set; } = null!;
    public decimal MinimumPrice { get; set; }
    public decimal MaximumPrice { get; set; }
    [StringLength(500)] public string? Note { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
    public decimal? FinalPrice { get; set; }
    public DateTime? FinalPriceAcceptedAtUtc { get; set; }
}
