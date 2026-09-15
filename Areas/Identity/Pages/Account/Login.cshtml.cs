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
public class LoginModel(SignInManager<ApplicationUser> signInManager) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    public string ReturnUrl { get; set; } = "/MaintenanceRequests";
    public class InputModel
    {
        [Required(ErrorMessage = "أدخل البريد الإلكتروني."), EmailAddress(ErrorMessage = "أدخل بريدًا صحيحًا.")]
        [Display(Name = "البريد الإلكتروني")] public string Email { get; set; } = string.Empty;
        [Required(ErrorMessage = "أدخل كلمة المرور."), DataType(DataType.Password)]
        [Display(Name = "كلمة المرور")] public string Password { get; set; } = string.Empty;
        [Display(Name = "تذكرني")] public bool RememberMe { get; set; }
    }
    public void OnGet(string? returnUrl = null) => ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl! : "/MaintenanceRequests";
    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        OnGet(returnUrl);
        if (!ModelState.IsValid) return Page();
        var result = await signInManager.PasswordSignInAsync(Input.Email.Trim(), Input.Password, Input.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded) return LocalRedirect(ReturnUrl);
        if (result.RequiresTwoFactor) return RedirectToPage("./LoginWith2fa", new { ReturnUrl, Input.RememberMe });
        ModelState.AddModelError(string.Empty, result.IsLockedOut ? "محاولات كثيرة. انتظر خمس دقائق ثم حاول مجددًا." : "تعذر تسجيل الدخول. تحقق من البريد وكلمة المرور.");
        return Page();
    }
}
