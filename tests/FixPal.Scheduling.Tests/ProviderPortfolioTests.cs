using System.Security.Claims;
using System.Text;
using FixPal.Controllers;
using FixPal.Data;
using FixPal.Models;
using FixPal.Models.Enums;
using FixPal.Models.ViewModels;
using FixPal.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace FixPal.Scheduling.Tests;

[Collection(nameof(SqlSchedulingCollection))]
public sealed class ProviderPortfolioTests(SqlSchedulingFixture fixture)
{
    [SqlServerFact]
    public async Task OwningProviderPublishesSeparateImage_PostedOwnershipIsIgnored_CorrectProfileOnly()
    {
        fixture.RequireLocalDb();
        await using var s = await fixture.CreateScenarioAsync();
        await using var other = await fixture.CreateScenarioAsync();
        using var media = new TestMedia();
        var controller = Controller(s, s.Provider, media.Portfolio);
        controller.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["ProviderProfileId"] = other.ProviderProfileId.ToString(), ["ProviderId"] = other.ProviderProfileId.ToString(),
            ["UserId"] = other.ProviderId, ["OwnerId"] = other.ProviderId
        });
        Assert.IsType<RedirectToActionResult>(await controller.Create(Input(), default));
        var row = await s.Db.ProviderPortfolioItems.AsNoTracking().SingleAsync(p => p.ProviderProfileId == s.ProviderProfileId);
        Assert.Equal(s.ProviderProfileId, row.ProviderProfileId);
        Assert.False(row.IsArchived);
        Assert.NotEqual(default, row.CreatedAtUtc);
        Assert.Matches("^[a-f0-9]{32}\\.png$", row.StorageKey);
        Assert.Equal("image/png", row.ContentType);
        var own = await Profile(s, s.ProviderProfileId);
        Assert.Equal(row.Id, Assert.Single(own.Portfolio.Items).Id);
        Assert.Empty((await Profile(s, other.ProviderProfileId)).Portfolio.Items);
        Assert.Equal(0, own.CompletedServiceCount);
        var html = System.Net.WebUtility.HtmlDecode(await Render(own));
        Assert.Contains("منشور بواسطة المزود", html);
        Assert.DoesNotContain("Verified", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("موثق", html);
        var image = Assert.IsType<FileStreamResult>(await Controller(s, Anonymous(), media.Portfolio).Image(row.Id, default));
        await image.FileStream.DisposeAsync();
    }

    [SqlServerFact]
    public async Task CustomerAnonymousUnapprovedAndUnrelatedCannotMutateOthersPortfolio()
    {
        fixture.RequireLocalDb();
        await using var s = await fixture.CreateScenarioAsync();
        await using var other = await fixture.CreateScenarioAsync();
        using var media = new TestMedia();
        await Controller(s, s.Provider, media.Portfolio).Create(Input(), default);
        var row = await s.Db.ProviderPortfolioItems.SingleAsync(p => p.ProviderProfileId == s.ProviderProfileId);
        foreach (var actor in new[] { s.Customer, Anonymous() })
        {
            var c = Controller(s, actor, media.Portfolio);
            Assert.IsType<ForbidResult>(await c.Create(Input(), default));
            Assert.IsType<ForbidResult>(await c.Archive(row.Id, default));
        }
        Assert.IsType<NotFoundResult>(await Controller(s, other.Provider, media.Portfolio).Archive(row.Id, default));
        await s.Db.ProviderProfiles.Where(p => p.Id == s.ProviderProfileId)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.ApprovalStatus, ApprovalStatus.Pending));
        Assert.IsType<ForbidResult>(await Controller(s, s.Provider, media.Portfolio).Create(Input(), default));
        Assert.IsType<NotFoundResult>(await Controller(s, Anonymous(), media.Portfolio).Image(row.Id, default));
        Assert.True(await s.Db.ProviderPortfolioItems.AnyAsync(p => p.Id == row.Id));
    }

    [SqlServerFact]
    public async Task ArchiveHidesPublicCardAndDirectImage_PreservesOwnerHistoryAndFile()
    {
        fixture.RequireLocalDb();
        await using var s = await fixture.CreateScenarioAsync();
        using var media = new TestMedia();
        var c = Controller(s, s.Provider, media.Portfolio);
        await c.Create(Input(), default);
        var row = await s.Db.ProviderPortfolioItems.AsNoTracking().SingleAsync(p => p.ProviderProfileId == s.ProviderProfileId);
        Assert.IsType<RedirectToActionResult>(await c.Archive(row.Id, default));
        Assert.Empty((await Profile(s, s.ProviderProfileId)).Portfolio.Items);
        Assert.IsType<NotFoundResult>(await Controller(s, Anonymous(), media.Portfolio).Image(row.Id, default));
        var management = Assert.IsType<PagedResult<PortfolioItemViewModel>>(Assert.IsType<ViewResult>(await c.Index()).Model);
        Assert.True(Assert.Single(management.Items).IsArchived);
        var ownerImage = Assert.IsType<FileStreamResult>(await c.Image(row.Id, default));
        await ownerImage.FileStream.DisposeAsync();
        Assert.Single(Directory.GetFiles(Path.Combine(media.Root, "App_Data", "ProviderPortfolio")));
    }

    [SqlServerFact]
    public async Task OwnerRestoresSameArchivedRecordAndPublicImage_OtherActorsCannotRestore()
    {
        fixture.RequireLocalDb();
        await using var s = await fixture.CreateScenarioAsync();
        await using var other = await fixture.CreateScenarioAsync();
        using var media = new TestMedia();
        var owner = Controller(s, s.Provider, media.Portfolio);
        await owner.Create(Input(), default);
        var original = await s.Db.ProviderPortfolioItems.AsNoTracking()
            .SingleAsync(p => p.ProviderProfileId == s.ProviderProfileId);
        await owner.Archive(original.Id, default);

        var unrelated = Controller(s, other.Provider, media.Portfolio);
        unrelated.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["ProviderProfileId"] = s.ProviderProfileId.ToString(), ["UserId"] = s.ProviderId
        });
        var denied = Assert.IsType<NotFoundObjectResult>(await unrelated.Restore(original.Id, default));
        Assert.Contains("تعذر إعادة نشر العمل", Assert.IsType<string>(denied.Value));
        foreach (var actor in new[] { s.Customer, Anonymous() })
            Assert.IsType<ForbidResult>(await Controller(s, actor, media.Portfolio).Restore(original.Id, default));
        Assert.Empty((await Profile(s, s.ProviderProfileId)).Portfolio.Items);
        Assert.IsType<NotFoundResult>(await Controller(s, Anonymous(), media.Portfolio).Image(original.Id, default));
        var archived = Assert.IsType<PagedResult<PortfolioItemViewModel>>(Assert.IsType<ViewResult>(await owner.Index()).Model);
        Assert.True(Assert.Single(archived.Items).IsArchived);

        Assert.IsType<RedirectToActionResult>(await owner.Restore(original.Id, default));
        Assert.Equal("أُعيد نشر العمل في ملفك العام.", owner.TempData["SuccessMessage"]);
        var restored = Assert.Single(await s.Db.ProviderPortfolioItems.AsNoTracking()
            .Where(p => p.ProviderProfileId == s.ProviderProfileId).ToListAsync());
        Assert.False(restored.IsArchived);
        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(original.StorageKey, restored.StorageKey);
        Assert.Equal(original.CreatedAtUtc, restored.CreatedAtUtc);
        Assert.Equal(original.Title, restored.Title);
        Assert.Equal(original.Id, Assert.Single((await Profile(s, s.ProviderProfileId)).Portfolio.Items).Id);
        var image = Assert.IsType<FileStreamResult>(await Controller(s, Anonymous(), media.Portfolio).Image(original.Id, default));
        await image.FileStream.DisposeAsync();

        await owner.Archive(original.Id, default);
        await s.Db.ProviderProfiles.Where(p => p.Id == s.ProviderProfileId)
            .ExecuteUpdateAsync(p => p.SetProperty(x => x.ApprovalStatus, ApprovalStatus.Pending));
        Assert.IsType<ForbidResult>(await owner.Restore(original.Id, default));
        Assert.True(await s.Db.ProviderPortfolioItems.AsNoTracking().Where(p => p.Id == original.Id).Select(p => p.IsArchived).SingleAsync());
    }

    [SqlServerFact]
    public async Task RealValidatorRejectsFakeTruncatedOversizedAndEmptyImages_AndBoundsMetadata()
    {
        fixture.RequireLocalDb();
        await using var s = await fixture.CreateScenarioAsync();
        using var media = new TestMedia();
        foreach (var bytes in new[] { Encoding.UTF8.GetBytes("<script>fake image</script>"),
            new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a },
            new byte[LocalPrivateMediaStorage.MaxBytes + 1], Array.Empty<byte>() })
        {
            var c = Controller(s, s.Provider, media.Portfolio);
            Assert.IsType<ViewResult>(await c.Create(new() { Title = "Test", Image = File(bytes) }, default));
            Assert.False(c.ModelState.IsValid);
        }
        foreach (var model in new[] { new CreatePortfolioItemViewModel { Title = " ", Image = Png() },
            new() { Title = new string('x', 121), Image = Png() },
            new() { Title = "Valid", Description = new string('x', 501), Image = Png() } })
            Assert.IsType<ViewResult>(await Controller(s, s.Provider, media.Portfolio).Create(model, default));
        Assert.Empty(await s.Db.ProviderPortfolioItems.Where(p => p.ProviderProfileId == s.ProviderProfileId).ToListAsync());
        Assert.Empty(Directory.GetFiles(media.Root, "*", SearchOption.AllDirectories));
        await Assert.ThrowsAsync<InvalidDataException>(() => media.Portfolio.OpenAsync("../RequestEvidence/image.png", default));
        await Assert.ThrowsAsync<InvalidDataException>(() => media.Portfolio.OpenAsync(new string('a', 32) + ".png\n", default));
    }

    [SqlServerFact]
    public async Task BeforeAfterAndLegacyEvidenceNeverBecomePortfolio_StorageNamespacesAreSeparate()
    {
        fixture.RequireLocalDb();
        await using var s = await fixture.CreateScenarioAsync();
        using var media = new TestMedia();
        var privateImage = await media.Private.SaveAsync(Png(), default);
        foreach (var entry in new[] { (EvidenceKind.Before, s.CustomerId, true), (EvidenceKind.After, s.ProviderId, true), (EvidenceKind.After, s.CustomerId, false) })
            s.Db.RequestEvidence.Add(new RequestEvidence { MaintenanceRequestId = s.RequestId, Kind = entry.Item1,
                UploadedById = entry.Item2, OwnershipChecked = entry.Item3, StorageKey = privateImage.Key,
                ContentType = privateImage.ContentType, Size = privateImage.Size, CreatedAtUtc = DateTime.UtcNow });
        await s.Db.SaveChangesAsync();
        Assert.Empty((await Profile(s, s.ProviderProfileId)).Portfolio.Items);
        Assert.Empty(await s.Db.ProviderPortfolioItems.Where(p => p.ProviderProfileId == s.ProviderProfileId).ToListAsync());
        await Assert.ThrowsAnyAsync<IOException>(() => media.Portfolio.OpenAsync(privateImage.Key, default));
        var evidence = await s.Db.RequestEvidence.AsNoTracking().Where(e => e.MaintenanceRequestId == s.RequestId).ToListAsync();
        Assert.Equal(EvidenceKind.General, evidence.Single(e => !e.OwnershipChecked).DisplayKind);
        await Controller(s, s.Provider, media.Portfolio).Create(Input(), default);
        Assert.Equal(3, await s.Db.RequestEvidence.CountAsync(e => e.MaintenanceRequestId == s.RequestId));
        Assert.Single((await Profile(s, s.ProviderProfileId)).Portfolio.Items);
        Assert.DoesNotContain(privateImage.Key, System.Text.Json.JsonSerializer.Serialize(await Profile(s, s.ProviderProfileId)));
    }

    [SqlServerFact]
    public async Task CompletedCountRequiresTruthfulAgreementAndFinalAcceptance_CompletionNeedsNoPortfolio()
    {
        fixture.RequireLocalDb();
        await using var s = await fixture.CreateScenarioAsync();
        Assert.Equal(0, (await Profile(s, s.ProviderProfileId)).CompletedServiceCount);
        Assert.Equal(MutationResult.Success, await s.Workflow.TransitionAsync(s.Provider, s.RequestId,
            MaintenanceRequestStatus.Accepted, MaintenanceRequestStatus.InProgress, default));
        await s.AddAcceptedFinalPriceAsync();
        Assert.Equal(MutationResult.Success, await s.Workflow.TransitionAsync(s.Provider, s.RequestId,
            MaintenanceRequestStatus.InProgress, MaintenanceRequestStatus.Completed, default));
        Assert.Equal(1, (await Profile(s, s.ProviderProfileId)).CompletedServiceCount);
        Assert.Empty((await Profile(s, s.ProviderProfileId)).Portfolio.Items);
        await using var falseHistory = await fixture.CreateScenarioAsync(withAgreement: false,
            existingProviderId: s.ProviderProfileId, providerUserId: s.ProviderId);
        await falseHistory.Db.MaintenanceRequests.Where(r => r.Id == falseHistory.RequestId)
            .ExecuteUpdateAsync(x => x.SetProperty(r => r.Status, MaintenanceRequestStatus.Completed).SetProperty(r => r.CompletedAtUtc, DateTime.UtcNow));
        Assert.Equal(1, (await Profile(s, s.ProviderProfileId)).CompletedServiceCount);
    }

    [SqlServerFact]
    public async Task AdditiveMigrationFollowsOwnershipMigration_AndDoesNotReclassifyEvidence()
    {
        fixture.RequireLocalDb();
        var name = "FixPal_PortfolioMigration_" + Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(
            $"Server=(localdb)\\mssqllocaldb;Database={name};Trusted_Connection=True;TrustServerCertificate=True").Options;
        await using var db = new ApplicationDbContext(options);
        try
        {
            await db.GetService<IMigrator>().MigrateAsync("20260918085007_AddRequestEvidenceOwnership");
            var appliedBefore = (await db.Database.GetAppliedMigrationsAsync()).ToList();
            Assert.EndsWith("AddRequestEvidenceOwnership", appliedBefore.Last());
            await db.Database.MigrateAsync();
            var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
            Assert.Equal(appliedBefore.Count + 1, applied.Count);
            Assert.EndsWith("AddProviderPortfolio", applied.Last());
            Assert.Equal(0, await db.ProviderPortfolioItems.CountAsync());
            Assert.Equal(0, await db.RequestEvidence.CountAsync());
        }
        finally { await db.Database.EnsureDeletedAsync(); }
    }

    private static async Task<PublicProviderViewModel> Profile(TestScenario s, int id) =>
        Assert.IsType<PublicProviderViewModel>(Assert.IsType<ViewResult>(await new ProvidersController(s.Db).Details(id)).Model);
    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());
    private static CreatePortfolioItemViewModel Input() => new() { Title = "إصلاح خزانة", Description = "عمل نشره المزود", Image = Png() };
    private static IFormFile Png()
    {
        using var image = new Image<Rgba32>(2, 2);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return File(stream.ToArray());
    }
    private static IFormFile File(byte[] bytes) => new FormFile(new MemoryStream(bytes), 0, bytes.Length, "Image", "../../untrusted.png")
    { Headers = new HeaderDictionary(), ContentType = "image/png" };
    private static ProviderPortfolioController Controller(TestScenario s, ClaimsPrincipal actor, IPortfolioMediaStorage storage)
    {
        var http = new DefaultHttpContext { User = actor };
        return new(s.Db, s.Services.GetRequiredService<RequestAccessService>(), storage, NullLogger<ProviderPortfolioController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = http }, TempData = new TempDataDictionary(http, new EmptyTempDataProvider()) };
    }
    private sealed class EmptyTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
    private sealed class TestMedia : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "FixPal_Portfolio_" + Guid.NewGuid().ToString("N"));
        public PortfolioMediaStorage Portfolio { get; }
        public LocalPrivateMediaStorage Private { get; }
        public TestMedia()
        {
            Directory.CreateDirectory(Root);
            var env = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Root, EnvironmentName = "Testing" }).Environment;
            Portfolio = new(env); Private = new(env);
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
    private static async Task<string> Render(PublicProviderViewModel model)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        { ApplicationName = typeof(ProvidersController).Assembly.GetName().Name, EnvironmentName = "Testing" });
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(ProvidersController).Assembly);
        await using var app = builder.Build();
        await using var scope = app.Services.CreateAsyncScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var routes = new RouteData(); routes.Routers.Add(new RouteCollection());
        var action = new ActionContext(http, routes, new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
        var view = scope.ServiceProvider.GetRequiredService<ICompositeViewEngine>().GetView(null, "/Views/Shared/_ProviderPortfolio.cshtml", false);
        Assert.True(view.Success);
        using var output = new StringWriter();
        var data = new ViewDataDictionary<PublicProviderViewModel>(new EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = model };
        await view.View.RenderAsync(new ViewContext(action, view.View, data, new TempDataDictionary(http, new EmptyTempDataProvider()), output, new HtmlHelperOptions()));
        return output.ToString();
    }
}
