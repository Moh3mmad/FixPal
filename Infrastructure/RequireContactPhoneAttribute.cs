using FixPal.Models.Enums;
using FixPal.Models.ViewModels;
using FixPal.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
namespace FixPal.Infrastructure;

// Only applied to new contact-sensitive commands; never to login or record reads.
public sealed class RequireContactPhoneAttribute : TypeFilterAttribute
{
    public RequireContactPhoneAttribute() : base(typeof(ContactPhoneFilter)) { }
}
public class ContactPhoneFilter(AccountPhoneService phones) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.ActionArguments.Values.OfType<CreateMaintenanceRequestViewModel>().Any(m => m.RequestType == RequestType.PublicReport)
            || await phones.HasUsableAsync(context.HttpContext.User)) { await next(); return; }
        var controller = (Controller)context.Controller;
        controller.TempData["ErrorMessage"] = "أضف رقم هاتف صالحًا إلى حسابك ثم عُد لإكمال هذه الخطوة.";
        var id = context.ActionArguments.TryGetValue("id", out var value) ? value as int? : null;
        var returnUrl = context.ActionDescriptor.RouteValues["action"] == "Claim" ? "/ProviderRequests/Available" : id.HasValue ? "/MaintenanceRequests/Details/" + id.Value : "/MaintenanceRequests/Create";
        context.Result = new RedirectToPageResult("/Account/Manage/Index", new { area = "Identity", returnUrl });
    }
}
