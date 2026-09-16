using System.ComponentModel.DataAnnotations;
namespace FixPal.Models;
public class RequestQuote
{
    public QuoteState State { get; set; }
    public int? CurrentRevisionNumber { get; set; }
    public int? AcceptedRevisionNumber { get; set; }
    public QuoteRevision? CurrentRevision { get; set; }
    public QuoteRevision? AcceptedRevision { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    public int Id { get; set; }
    public int MaintenanceRequestId { get; set; }
    public MaintenanceRequest MaintenanceRequest { get; set; } = null!;
    public int ProviderProfileId { get; set; }
    public ProviderProfile ProviderProfile { get; set; } = null!;
    // Original offer columns retained for additive migration compatibility; never updated by negotiation.
    public decimal MinimumPrice { get; set; }
    public decimal MaximumPrice { get; set; }
    [StringLength(500)] public string? Note { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
    public decimal? FinalPrice { get; set; }
    public DateTime? FinalPriceAcceptedAtUtc { get; set; }
}
