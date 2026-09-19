using FixPal.Data;
using FixPal.Models;
using FixPal.Models.Enums;
using FixPal.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FixPal.Controllers
{
    [Authorize]
    public class ProviderApplicationController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<ProviderApplicationController> _logger;
        private readonly FixPal.Services.AccountPhoneService _phones;

        public ProviderApplicationController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            ILogger<ProviderApplicationController> logger, FixPal.Services.AccountPhoneService phones)
        {
            _context = context;
            _userManager = userManager;
            _logger = logger;
            _phones = phones;
        }

        [HttpGet]
        public async Task<IActionResult> Apply()
        {
            var user = await _userManager.GetUserAsync(User);

            if (user == null)
            {
                return Challenge();
            }

            var alreadyApplied = await _context.ProviderProfiles
                .AsNoTracking()
                .AnyAsync(p => p.UserId == user.Id);

            if (alreadyApplied)
            {
                return RedirectToAction(nameof(Status));
            }

            await LoadDropdownsAsync();

            return View(new ProviderApplicationViewModel
            {
                PhoneNumber = user.PhoneNumber
            });
        }

        [HttpPost]
        public async Task<IActionResult> Apply(
     ProviderApplicationViewModel model,
     CancellationToken ct)
        {
            var user = await _userManager.GetUserAsync(User);

            if (user == null)
            {
                return Challenge();
            }

            var alreadyApplied = await _context.ProviderProfiles
                .AsNoTracking()
                .AnyAsync(p => p.UserId == user.Id, ct);

            if (alreadyApplied)
            {
                return RedirectToAction(nameof(Status));
            }

            // Never trust select values posted by the browser. Validate their
            // existence again on the server before creating relationships.
            var categoryExists = await _context.ServiceCategories
                .AsNoTracking()
                .AnyAsync(c => c.Id == model.ServiceCategoryId, ct);

            var areaExists = await _context.Areas
                .AsNoTracking()
                .AnyAsync(a => a.Id == model.AreaId && a.City.IsActive, ct);

            if (!categoryExists)
            {
                ModelState.AddModelError(
                    nameof(model.ServiceCategoryId),
                    "التخصص المحدد غير موجود.");
            }

            if (!areaExists)
            {
                ModelState.AddModelError(
                    nameof(model.AreaId),
                    "المنطقة المحددة غير موجودة.");
            }

            if (!_phones.TryNormalize(model.PhoneNumber, out var normalized))
            {
                ModelState.AddModelError(
                    nameof(model.PhoneNumber),
                    FixPal.Services.AccountPhoneService.ValidationMessage);
            }

            if (!ModelState.IsValid)
            {
                await LoadDropdownsAsync();
                return View(model);
            }

            var strategy = _context.Database.CreateExecutionStrategy();

            try
            {
                var result = await strategy.ExecuteAsync(async () =>
                {
                    // A retry must not reuse entities tracked by a failed attempt.
                    _context.ChangeTracker.Clear();

                    await using var transaction =
                        await _context.Database.BeginTransactionAsync(ct);

                    // If a previous CommitAsync succeeded but its acknowledgement
                    // was lost, treat the existing application as success.
                    if (await _context.ProviderProfiles
                        .AsNoTracking()
                        .AnyAsync(p => p.UserId == user.Id, ct))
                    {
                        await transaction.CommitAsync(ct);
                        return (Succeeded: true, Errors: Array.Empty<string>());
                    }

                    var attemptUser =
                        await _userManager.FindByIdAsync(user.Id);

                    if (attemptUser == null)
                    {
                        return (
                            Succeeded: false,
                            Errors: new[]
                            {
                        "تعذر العثور على حساب المستخدم."
                            });
                    }

                    if (!string.IsNullOrWhiteSpace(model.PhoneNumber))
                    {
                        var updateUserResult =
                            await _userManager.SetPhoneNumberAsync(
                                attemptUser,
                                normalized);

                        if (!updateUserResult.Succeeded)
                        {
                            return (
                                Succeeded: false,
                                Errors: updateUserResult.Errors
                                    .Select(e => e.Description)
                                    .ToArray());
                        }
                    }

                    var providerProfile = new ProviderProfile
                    {
                        UserId = user.Id,
                        DisplayName = model.DisplayName.Trim(),
                        ProviderType = ProviderType.Individual,
                        ApprovalStatus = ApprovalStatus.Pending,
                        ServiceCategoryId = model.ServiceCategoryId,
                        AreaId = model.AreaId,
                        Description = string.IsNullOrWhiteSpace(model.Description)
                            ? null
                            : model.Description.Trim(),
                        CreatedAt = DateTime.UtcNow
                    };

                    await _context.ProviderProfiles.AddAsync(
                        providerProfile,
                        ct);

                    await _context.SaveChangesAsync(ct);

                    await transaction.CommitAsync(ct);

                    return (
                        Succeeded: true,
                        Errors: Array.Empty<string>());
                });

                if (!result.Succeeded)
                {
                    foreach (var error in result.Errors)
                    {
                        ModelState.AddModelError(
                            string.Empty,
                            error);
                    }

                    await LoadDropdownsAsync();
                    return View(model);
                }

                TempData["SuccessMessage"] =
                    "تم إرسال طلب الانضمام بنجاح. سنعرض لك حالة المراجعة هنا.";

                return RedirectToAction(nameof(Status));
            }
            catch (DbUpdateException ex)
            {
                _context.ChangeTracker.Clear();

                _logger.LogError(
                    ex,
                    "Database error while creating provider profile for user {UserId}.",
                    user.Id);

                ModelState.AddModelError(
                    string.Empty,
                    "تعذر حفظ الطلب حاليًا. تأكد من البيانات وحاول مرة أخرى.");
            }
            catch (Exception ex)
            {
                _context.ChangeTracker.Clear();

                _logger.LogError(
                    ex,
                    "Unexpected error while creating provider profile for user {UserId}.",
                    user.Id);

                ModelState.AddModelError(
                    string.Empty,
                    "حدث خطأ غير متوقع. حاول مرة أخرى بعد قليل.");
            }

            await LoadDropdownsAsync();
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Status()
        {
            var user = await _userManager.GetUserAsync(User);

            if (user == null)
            {
                return Challenge();
            }

            var providerProfile = await _context.ProviderProfiles
                .AsNoTracking()
                .Include(p => p.ServiceCategory)
                .Include(p => p.Area)
                .ThenInclude(a => a!.City)
                .FirstOrDefaultAsync(p => p.UserId == user.Id);

            if (providerProfile == null)
            {
                return RedirectToAction(nameof(Apply));
            }

            return View(providerProfile);
        }

        private async Task LoadDropdownsAsync()
        {
            var categories = await _context.ServiceCategories
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .ToListAsync();

            var areas = await _context.Areas
                .AsNoTracking()
                .Where(a => a.City.IsActive)
                .OrderBy(a => a.City.Name).ThenBy(a => a.Name)
                .Select(a => new { a.Id, Name = a.Name + " — " + a.City.Name })
                .ToListAsync();

            ViewBag.Categories = new SelectList(categories, "Id", "Name");
            ViewBag.Areas = new SelectList(areas, "Id", "Name");
        }
    }
}
