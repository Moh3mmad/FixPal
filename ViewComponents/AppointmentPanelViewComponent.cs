using FixPal.Services;
using Microsoft.AspNetCore.Mvc;

namespace FixPal.ViewComponents;

public sealed class AppointmentPanelViewComponent(AppointmentService appointments) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(int maintenanceRequestId, CancellationToken ct)
    {
        var model = await appointments.GetForRequestAsync(UserClaimsPrincipal, maintenanceRequestId, ct);
        return model == null ? Content(string.Empty) : View(model);
    }
}
