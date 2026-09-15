using FixPal.Models;

namespace FixPal.Models.ViewModels
{
    public class AdminDashboardViewModel
    {
        public int Page { get; init; } = 1;
        public int TotalPages => Math.Max(1, (PendingProvidersCount + 11) / 12);
        public int AreasCount { get; init; }
        public int CategoriesCount { get; init; }
        public int ProvidersCount { get; init; }
        public int PendingProvidersCount { get; init; }

        public IReadOnlyList<Area> Areas { get; init; } = Array.Empty<Area>();
        public IReadOnlyList<ServiceCategory> Categories { get; init; } =
            Array.Empty<ServiceCategory>();
        public IReadOnlyList<ProviderProfile> PendingProviders { get; init; } =
            Array.Empty<ProviderProfile>();
    }
}
