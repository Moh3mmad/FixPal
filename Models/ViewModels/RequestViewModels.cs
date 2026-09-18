using System.Linq.Expressions;
using FixPal.Models.Enums;
namespace FixPal.Models.ViewModels;
public class RequestSummaryViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string? ProviderName { get; set; }
    public RequestType RequestType { get; set; }
    public MaintenanceRequestStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public static Expression<Func<MaintenanceRequest, RequestSummaryViewModel>> Projection => r => new()
    {
        Id = r.Id, Title = r.Title, Category = r.ServiceCategory.Name,
        Location = r.Area.Name + " — " + r.Area.City.Name,
        ProviderName = r.ProviderProfile == null ? null : r.ProviderProfile.DisplayName,
        RequestType = r.RequestType, Status = r.Status, CreatedAtUtc = r.CreatedAtUtc
    };
}
public class RequestDetailsViewModel : RequestSummaryViewModel
{
    public EvidenceSummary Evidence { get; set; } = new();
    public CommunicationState Communication { get; set; } = new();
    public bool IsLegacy { get; set; }
    public bool HasAgreement { get; set; }
    public bool CanReview { get; set; }
    public bool CanDecideQuote { get; set; }
    public PagedResult<QuoteHistoryItem> QuoteHistory { get; set; } = new();
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public bool CanViewProblemLocation { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime? AcceptedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public bool CanManage { get; set; }
    public bool IsOwner { get; set; }
    public RequestQuote? Quote { get; set; }
    public ProviderReview? Review { get; set; }
    public static Expression<Func<MaintenanceRequest, RequestDetailsViewModel>> DetailProjection => r => new()
    {
        Id = r.Id, IsLegacy = r.IsLegacy, Title = r.Title, Description = r.Description, Category = r.ServiceCategory.Name,
        Location = r.Area.Name + " — " + r.Area.City.Name,
        ProviderName = r.ProviderProfile == null ? null : r.ProviderProfile.DisplayName,
        RequestType = r.RequestType, Status = r.Status, CreatedAtUtc = r.CreatedAtUtc,
        AcceptedAtUtc = r.AcceptedAtUtc, StartedAtUtc = r.StartedAtUtc, CompletedAtUtc = r.CompletedAtUtc
    };
}
public record ClaimableRequestItem(int Id, string Category, string Location, DateTime CreatedAtUtc);
public class EvidenceSummary
{
    public int BeforeCount { get; set; }
    public int AfterCount { get; set; }
    public int GeneralCount { get; set; }
    public int? BeforeImageId { get; set; }
    public int? AfterImageId { get; set; }
}
public record QuoteHistoryItem(int Number, decimal MinimumPrice, decimal MaximumPrice, string? Note, DateTime CreatedAtUtc,
    QuoteState? Decision, string? DecisionNote, DateTime? DecidedAtUtc);
