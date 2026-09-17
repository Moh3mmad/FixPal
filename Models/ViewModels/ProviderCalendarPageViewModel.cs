using FixPal.Services;

namespace FixPal.Models.ViewModels;

public sealed record ProviderCalendarPageViewModel(
    ProviderCalendarReadModel? Calendar,
    ProviderBlackoutManagementReadModel? BlackoutManagement);
