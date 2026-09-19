using FixPal.Data;
using FixPal.Infrastructure.Identity;
using FixPal.Models;
using FixPal.Models.Enums;
using FixPal.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FixPal.Controllers
{
    [Authorize(Roles = AppRoles.Admin)]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<AdminController> _logger;

        public AdminController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            ILogger<AdminController> logger)
        {
            _context = context;
            _userManager = userManager;
            _logger = logger;
        }

        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null || !await _userManager.IsInRoleAsync(user, AppRoles.Admin))
            {
                context.Result = Forbid();
                return;
            }
            await next();
        }

        [HttpGet]
        public async Task<IActionResult> Index(int page = 1, CancellationToken ct = default)
        {
            var pendingCount = await _context.ProviderProfiles.CountAsync(p => p.ApprovalStatus == ApprovalStatus.Pending, ct);
            page = Math.Clamp(page, 1, Math.Max(1, (pendingCount + 11) / 12));
            var model = new AdminDashboardViewModel
            {
                Page = page,
                AreasCount = await _context.Areas.AsNoTracking().CountAsync(),
                CategoriesCount = await _context.ServiceCategories.AsNoTracking().CountAsync(),
                ProvidersCount = await _context.ProviderProfiles.AsNoTracking().CountAsync(),
                PendingProvidersCount = await _context.ProviderProfiles
                    .AsNoTracking()
                    .CountAsync(p => p.ApprovalStatus == ApprovalStatus.Pending),

                Areas = await _context.Areas
                    .AsNoTracking()
                    .OrderBy(a => a.Name)
                    .ToListAsync(),

                Categories = await _context.ServiceCategories
                    .AsNoTracking()
                    .OrderBy(c => c.Name)
                    .ToListAsync(),

                PendingProviders = await _context.ProviderProfiles
                    .AsNoTracking()
                    .Include(p => p.User)
                    .Include(p => p.Area)
                    .ThenInclude(a => a!.City)
                    .Include(p => p.ServiceCategory)
                    .Where(p => p.ApprovalStatus == ApprovalStatus.Pending)
                    .OrderByDescending(p => p.CreatedAt)
                    .ThenByDescending(p => p.Id)
                    .Skip((page - 1) * 12).Take(12)
                    .ToListAsync(ct)
            };

            return View(model);
        }
        [HttpPost]
        public async Task<IActionResult> ApproveProvider(
            int id,
            CancellationToken ct)
        {
            var provider = await _context.ProviderProfiles
                .AsNoTracking()
                .Include(p => p.User)
                .FirstOrDefaultAsync(p => p.Id == id, ct);

            if (provider == null)
            {
                return NotFound();
            }

            if (provider.User == null)
            {
                TempData["ErrorMessage"] =
                    "تعذر العثور على حساب المستخدم المرتبط بهذا الطلب.";

                return RedirectToAction(nameof(Index));
            }

            if (provider.ApprovalStatus != ApprovalStatus.Pending)
            {
                TempData["ErrorMessage"] =
                    "تمت مراجعة هذا الطلب بالفعل. لا يمكن تغيير القرار من هذا الإجراء.";

                return RedirectToAction(nameof(Index));
            }

            var displayName = provider.DisplayName;
            var strategy = _context.Database.CreateExecutionStrategy();

            try
            {
                var succeeded = await strategy.ExecuteAsync(async () =>
                {
                    ct.ThrowIfCancellationRequested();

                    _context.ChangeTracker.Clear();

                    await using var transaction =
                        await _context.Database.BeginTransactionAsync(ct);

                    var current = await _context.ProviderProfiles
                        .Include(p => p.User)
                        .FirstOrDefaultAsync(p => p.Id == id, ct);

                    if (current == null || current.User == null)
                    {
                        return false;
                    }

                    // If a previous CommitAsync actually succeeded but its
                    // acknowledgement was lost, this retry is already complete.
                    if (current.ApprovalStatus == ApprovalStatus.Approved)
                    {
                        return true;
                    }

                    if (current.ApprovalStatus != ApprovalStatus.Pending)
                    {
                        return false;
                    }

                    if (!await _userManager.IsInRoleAsync(
                            current.User,
                            AppRoles.Provider))
                    {
                        var roleResult =
                            await _userManager.AddToRoleAsync(
                                current.User,
                                AppRoles.Provider);

                        if (!roleResult.Succeeded)
                        {
                            var errors = string.Join(
                                ", ",
                                roleResult.Errors.Select(
                                    e => e.Description));

                            throw new InvalidOperationException(
                                $"Failed to assign Provider role. {errors}");
                        }
                    }

                    current.ApprovalStatus =
                        ApprovalStatus.Approved;

                    await _context.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);

                    return true;
                });

                if (!succeeded)
                {
                    TempData["ErrorMessage"] =
                        "تمت مراجعة هذا الطلب بالفعل. لا يمكن تغيير القرار من هذا الإجراء.";

                    return RedirectToAction(nameof(Index));
                }

                TempData["SuccessMessage"] =
                    $"تم اعتماد {displayName} كمزود خدمة بنجاح.";
            }
            catch (Exception ex)
            {
                _context.ChangeTracker.Clear();

                _logger.LogError(
                    ex,
                    "Failed to approve provider profile {ProviderId}.",
                    id);

                TempData["ErrorMessage"] =
                    "تعذر اعتماد الطلب حاليًا. لم يتم حفظ أي تغيير جزئي.";
            }

            return RedirectToAction(nameof(Index));
        }
        [HttpPost]
        public async Task<IActionResult> RejectProvider(
            int id,
            CancellationToken ct)
        {
            var provider = await _context.ProviderProfiles
                .AsNoTracking()
                .Include(p => p.User)
                .FirstOrDefaultAsync(p => p.Id == id, ct);

            if (provider == null)
            {
                return NotFound();
            }

            if (provider.ApprovalStatus != ApprovalStatus.Pending)
            {
                TempData["ErrorMessage"] =
                    "تمت مراجعة هذا الطلب بالفعل. لا يمكن تغيير القرار من هذا الإجراء.";

                return RedirectToAction(nameof(Index));
            }

            var displayName = provider.DisplayName;
            var strategy = _context.Database.CreateExecutionStrategy();

            try
            {
                var succeeded = await strategy.ExecuteAsync(async () =>
                {
                    ct.ThrowIfCancellationRequested();

                    _context.ChangeTracker.Clear();

                    await using var transaction =
                        await _context.Database.BeginTransactionAsync(ct);

                    var current = await _context.ProviderProfiles
                        .Include(p => p.User)
                        .FirstOrDefaultAsync(p => p.Id == id, ct);

                    if (current == null)
                    {
                        return false;
                    }

                    // Covers an uncertain previous commit.
                    if (current.ApprovalStatus == ApprovalStatus.Rejected)
                    {
                        return true;
                    }

                    if (current.ApprovalStatus != ApprovalStatus.Pending)
                    {
                        return false;
                    }

                    if (current.User != null &&
                        await _userManager.IsInRoleAsync(
                            current.User,
                            AppRoles.Provider))
                    {
                        var roleResult =
                            await _userManager.RemoveFromRoleAsync(
                                current.User,
                                AppRoles.Provider);

                        if (!roleResult.Succeeded)
                        {
                            var errors = string.Join(
                                ", ",
                                roleResult.Errors.Select(
                                    e => e.Description));

                            throw new InvalidOperationException(
                                $"Failed to remove Provider role. {errors}");
                        }
                    }

                    current.ApprovalStatus =
                        ApprovalStatus.Rejected;

                    await _context.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);

                    return true;
                });

                if (!succeeded)
                {
                    TempData["ErrorMessage"] =
                        "تمت مراجعة هذا الطلب بالفعل. لا يمكن تغيير القرار من هذا الإجراء.";

                    return RedirectToAction(nameof(Index));
                }

                TempData["SuccessMessage"] =
                    $"تم رفض طلب {displayName}.";
            }
            catch (Exception ex)
            {
                _context.ChangeTracker.Clear();

                _logger.LogError(
                    ex,
                    "Failed to reject provider profile {ProviderId}.",
                    id);

                TempData["ErrorMessage"] =
                    "تعذر رفض الطلب حاليًا. لم يتم حفظ أي تغيير جزئي.";
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
