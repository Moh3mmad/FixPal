using System.ComponentModel.DataAnnotations;
using FixPal.Data;
using FixPal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
namespace FixPal.Controllers;
public class DiagnosisInput
{
    [Required, StringLength(2000, MinimumLength = 10)] public string Description { get; set; } = "";
    public IFormFile? Image { get; set; }
    public bool Consent { get; set; }
}
[Authorize, ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class ProblemDiagnosisController(IProblemDiagnosisService diagnosis, ApplicationDbContext db) : Controller
{
    [HttpPost, EnableRateLimiting("diagnosis"), RequestSizeLimit(6 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<IActionResult> Analyze(DiagnosisInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid || !input.Consent || input.Image?.Length > LocalPrivateMediaStorage.MaxBytes)
            return BadRequest(new DiagnosisResponse("invalid", "اكتب وصفًا من 10 إلى 2000 حرف ووافق على إرسال الوصف والصورة الاختيارية. الحد الأقصى للصورة 5 ميغابايت."));
        var categories = await db.ServiceCategories.AsNoTracking().OrderBy(c => c.Id).Take(100).Select(c => new DiagnosisCategory(c.Id, c.Name)).ToListAsync(ct);
        return Json(await diagnosis.DiagnoseAsync(input.Description.Trim(), categories, input.Image, ct));
    }
}
