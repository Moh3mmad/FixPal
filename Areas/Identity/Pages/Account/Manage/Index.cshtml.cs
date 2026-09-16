using System.ComponentModel.DataAnnotations;
using FixPal.Models;
using FixPal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace FixPal.Areas.Identity.Pages.Account.Manage;

[Authorize, ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class IndexModel(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, AccountPhoneService phones) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    public string ReturnUrl { get; set; } = "/MaintenanceRequests";
    public class InputModel
    {
        [Required(ErrorMessage = "أدخل رقم هاتف للتواصل."), StringLength(40, ErrorMessage = "رقم الهاتف طويل جدًا.")]
        [Display(Name = "رقم الهاتف")]
        public string PhoneNumber { get; set; } = string.Empty;
    }
    private void SetReturn(string? returnUrl) => ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl! : "/MaintenanceRequests";
    public async Task<IActionResult> OnGetAsync(string? returnUrl)
    {
        SetReturn(returnUrl);
        var user = await users.GetUserAsync(User);
        if (user == null) return Challenge();
        Input.PhoneNumber = user.PhoneNumber ?? string.Empty;
        return Page();
    }
    public async Task<IActionResult> OnPostAsync(string? returnUrl)
    {
        SetReturn(returnUrl);
        var user = await users.GetUserAsync(User);
        if (user == null) return Challenge();
        if (!phones.TryNormalize(Input.PhoneNumber, out var normalized)) ModelState.AddModelError("Input.PhoneNumber", AccountPhoneService.ValidationMessage);
        if (!ModelState.IsValid) return Page();
        var result = await users.SetPhoneNumberAsync(user, normalized);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "تعذر حفظ رقم الهاتف. حاول مرة أخرى.");
            return Page();
        }
        await signIn.RefreshSignInAsync(user);
        TempData["SuccessMessage"] = "تم حفظ رقم هاتفك. يمكنك متابعة طلبك.";
        return LocalRedirect(ReturnUrl);
    }
}
