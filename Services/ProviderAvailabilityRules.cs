using FixPal.Models.Enums;
namespace FixPal.Services;
public static class ProviderAvailabilityRules
{
    public static ProviderAvailability Effective(ProviderAvailability state, DateTime? after, DateTime nowUtc)
        => state == ProviderAvailability.AvailableAfter && after.HasValue && after <= nowUtc
            ? ProviderAvailability.AvailableNow : state;
    public static string Label(ProviderAvailability state, DateTime? after)
        => Effective(state, after, DateTime.UtcNow) switch
        {
            ProviderAvailability.AvailableNow => "متاح الآن",
            ProviderAvailability.AvailableAfter => "متاح بعد الموعد المحدد",
            _ => "غير متاح حاليًا"
        };
}
