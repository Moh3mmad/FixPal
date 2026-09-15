using FixPal.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace FixPal.Controllers;
[AllowAnonymous]
public class ServicesController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct) => View(await db.ServiceCategories.AsNoTracking().OrderBy(c => c.Name).ThenBy(c => c.Id).ToListAsync(ct));
}
