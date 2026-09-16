using System.ComponentModel.DataAnnotations;
using FixPal.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
namespace FixPal.Areas.Identity.Pages.Account;
[AllowAnonymous, EnableRateLimiting("identity")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class RegisterModel(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signInManager, FixPal.Services.AccountPhoneService phones) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    public string ReturnUrl { get; set; } = "/MaintenanceRequests";
    public class InputModel
    {
        [Required(ErrorMessage = "أدخل رقم هاتف للتواصل."), StringLength(40, ErrorMessage = "رقم الهاتف طويل جدًا.")]
        [Display(Name = "رقم الهاتف")] public string PhoneNumber { get; set; } = string.Empty;
        [Required(ErrorMessage = "أدخل البريد الإلكتروني."), EmailAddress(ErrorMessage = "أدخل بريدًا صحيحًا.")]
        [Display(Name = "البريد الإلكتروني")] public string Email { get; set; } = string.Empty;
        [Required(ErrorMessage = "أدخل كلمة المرور."), StringLength(100, MinimumLength = 6, ErrorMessage = "كلمة المرور بين 6 و100 حرف."), DataType(DataType.Password)]
        [Display(Name = "كلمة المرور")] public string Password { get; set; } = string.Empty;
        [Compare(nameof(Password), ErrorMessage = "كلمتا المرور غير متطابقتين."), DataType(DataType.Password)]
        [Display(Name = "تأكيد كلمة المرور")] public string ConfirmPassword { get; set; } = string.Empty;
    }
    public void OnGet(string? returnUrl = null) => ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl! : "/MaintenanceRequests";
    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        OnGet(returnUrl);
        if (!phones.TryNormalize(Input.PhoneNumber, out var normalized)) ModelState.AddModelError("Input.PhoneNumber", FixPal.Services.AccountPhoneService.ValidationMessage);
        if (!ModelState.IsValid) return Page();
        var email = Input.Email.Trim();
        var user = new ApplicationUser { UserName = email, Email = email, PhoneNumber = normalized, PhoneNumberConfirmed = false };
        var result = await users.CreateAsync(user, Input.Password);
        if (result.Succeeded)
        {
            // No roles are accepted from the browser or assigned through registration.
            await signInManager.SignInAsync(user, isPersistent: false);
            return LocalRedirect(ReturnUrl);
        }
        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Code.StartsWith("Password", StringComparison.Ordinal)
                ? "اختر كلمة مرور من 6 أحرف على الأقل تتضمن أحرفًا إنجليزية كبيرة وصغيرة ورقمًا ورمزًا."
                : "تعذر إنشاء الحساب بهذه البيانات. جرّب تسجيل الدخول أو استخدم بريدًا آخر.");
        return Page();
    }
}
