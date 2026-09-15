using System.Security.Claims;
using FixPal.Data;
using FixPal.Infrastructure.Identity;
using FixPal.Models;
using FixPal.Models.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
namespace FixPal.Services;
public record RequestAccess(MaintenanceRequest Request, bool IsOwner, bool IsProvider, bool IsAdmin)
{
    public bool CanParticipate => IsOwner || IsProvider;
}
public class RequestAccessService(ApplicationDbContext db, UserManager<ApplicationUser> users)
{
    public async Task<int?> ProviderIdAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        if (!principal.IsInRole(AppRoles.Provider)) return null;
        var user = await users.GetUserAsync(principal);
        if (user == null || !await users.IsInRoleAsync(user, AppRoles.Provider)) return null;
        return await db.ProviderProfiles.AsNoTracking().Where(p => p.UserId == user.Id && p.ApprovalStatus == ApprovalStatus.Approved)
            .Select(p => (int?)p.Id).SingleOrDefaultAsync(ct);
    }
    public async Task<RequestAccess?> GetAsync(ClaimsPrincipal principal, int id, CancellationToken ct)
    {
        var user = await users.GetUserAsync(principal);
        if (user == null) return null;
        var providerId = await ProviderIdAsync(principal, ct);
        var admin = principal.IsInRole(AppRoles.Admin) && await users.IsInRoleAsync(user, AppRoles.Admin);
        var request = await db.MaintenanceRequests.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id &&
            (r.CustomerId == user.Id || admin || (providerId != null && r.ProviderProfileId == providerId)), ct);
        return request == null ? null : new(request, request.CustomerId == user.Id, providerId != null && request.ProviderProfileId == providerId, admin);
    }
}
