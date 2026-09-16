using FixPal.Data;
using FixPal.Infrastructure.Identity;
using FixPal.Models;
using FixPal.Models.Enums;
namespace FixPal.Services;

public static class ProviderEligibility
{
    public static IQueryable<ProviderProfile> Active(ApplicationDbContext db) => db.ProviderProfiles.Where(p =>
        p.ApprovalStatus == ApprovalStatus.Approved && p.ProviderType == ProviderType.Individual && p.Area!.City.IsActive
        && db.UserRoles.Any(ur => ur.UserId == p.UserId && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRoles.Provider)));

    public static IQueryable<ProviderProfile> ForRequest(ApplicationDbContext db, int categoryId, int areaId, string? ownerId) =>
        Active(db).Where(p => p.ServiceCategoryId == categoryId && p.AreaId == areaId && p.UserId != ownerId);
}
