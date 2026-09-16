using System.Security.Claims;
using FixPal.Models;
using Microsoft.AspNetCore.Identity;
using PhoneNumbers;
namespace FixPal.Services;

public class AccountPhoneService(IConfiguration configuration, UserManager<ApplicationUser> users)
{
    public const string ValidationMessage = "أدخل رقم هاتف صالحًا مع رمز الدولة، أو رقمًا محليًا فلسطينيًا.";
    public bool TryNormalize(string? input, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(input) || input.Length > 40) return false;
        var chars = input.Trim().Select(c => c is >= '٠' and <= '٩' ? (char)('0' + c - '٠')
            : c is >= '۰' and <= '۹' ? (char)('0' + c - '۰') : c).ToArray();
        if (chars.Any(c => !(c is >= '0' and <= '9' || c is '+' or '-' or '(' or ')' or ' '))) return false;
        var value = new string(chars);
        if (value.StartsWith("00", StringComparison.Ordinal)) value = "+" + value[2..];
        try
        {
            var util = PhoneNumberUtil.GetInstance();
            var number = util.Parse(value, configuration["Contact:DefaultRegion"] ?? "PS");
            if (number.HasExtension || !util.IsValidNumber(number)) return false;
            normalized = util.Format(number, PhoneNumberFormat.E164);
            return true;
        }
        catch (NumberParseException) { return false; }
    }
    public async Task<bool> HasUsableAsync(ClaimsPrincipal principal)
    {
        var user = await users.GetUserAsync(principal);
        return user != null && TryNormalize(user.PhoneNumber, out _);
    }
}
