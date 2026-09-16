using System.ComponentModel.DataAnnotations;
namespace FixPal.Models;

public enum QuoteState { Draft = 0, AwaitingCustomer = 1, Accepted = 2, Rejected = 3, RevisionRequested = 4 }

// Immutable terms. Decisions are separate append-only records, not edits to an offer.
public class QuoteRevision
{
    public int RequestQuoteId { get; set; }
    public int Number { get; set; }
    public RequestQuote RequestQuote { get; set; } = null!;
    [Required] public string ProviderAuthorId { get; set; } = string.Empty;
    public decimal MinimumPrice { get; set; }
    public decimal MaximumPrice { get; set; }
    [StringLength(500)] public string? Note { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class QuoteDecision
{
    public int RequestQuoteId { get; set; }
    public int RevisionNumber { get; set; }
    public QuoteRevision Revision { get; set; } = null!;
    public QuoteState State { get; set; }
    [Required] public string CustomerAuthorId { get; set; } = string.Empty;
    [StringLength(500)] public string? Note { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
