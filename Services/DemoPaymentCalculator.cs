using FixPal.Models;
using FixPal.Models.Enums;
namespace FixPal.Services;
public record DemoPaymentSummary(decimal DepositPreview, decimal? FinalPrice, decimal? PlatformFee, decimal? ProviderNet);
// Pure decimal arithmetic; no payment gateway or money transfer occurs.
public static class DemoPaymentCalculator
{
    public const decimal PlatformRate = 0.05m;
    public const decimal IllustrativeDepositRate = 0.20m;
    public static decimal Round(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
    public static DemoPaymentSummary? Calculate(RequestQuote? quote, MaintenanceRequestStatus status)
    {
        if (quote?.AcceptedAtUtc == null) return null;
        var deposit = Round(quote.MinimumPrice * IllustrativeDepositRate);
        if (status != MaintenanceRequestStatus.Completed || quote.FinalPriceAcceptedAtUtc == null || quote.FinalPrice == null)
            return new(deposit, quote.FinalPrice, null, null);
        var fee = Round(quote.FinalPrice.Value * PlatformRate);
        return new(deposit, quote.FinalPrice, fee, quote.FinalPrice.Value - fee);
    }
}
