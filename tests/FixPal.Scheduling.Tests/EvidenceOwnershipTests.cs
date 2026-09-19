using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FixPal.Controllers;
using FixPal.Data;
using FixPal.Models;
using FixPal.Models.Enums;
using FixPal.Models.ViewModels;
using FixPal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
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
public sealed class EvidenceOwnershipTests(SqlSchedulingFixture fixture)
{
    [SqlServerFact]
    public async Task BeforeBelongsToCustomer_AfterBelongsToAssignedProvider_IdentityCannotBePosted()
    {
        fixture.RequireLocalDb();
        await using var s = await fixture.CreateScenarioAsync();
        using var storage = new TestStorage();
        var customer = Controller(s, s.Customer, storage);
        customer.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["UploadedById"] = s.ProviderId, ["CustomerId"] = s.ProviderId,
            ["ProviderId"] = "999999", ["OwnershipChecked"] = "false"
        });
        Assert.IsType<NotFoundResult>(await customer.Upload(s.RequestId, EvidenceKind.After, Png(), default));
        Assert.IsType<NotFoundResult>(await Controller(s, s.Provider, storage).Upload(s.RequestId, EvidenceKind.Before, Png(), default));
        await customer.Upload(s.RequestId, EvidenceKind.Before, Png(), default);
        var before = await s.Db.RequestEvidence.AsNoTracking().SingleAsync(e => e.MaintenanceRequestId == s.RequestId);
        Assert.Equal(s.CustomerId, before.UploadedById);
        Assert.True(before.OwnershipChecked);
        Assert.Equal(EvidenceKind.Before, before.DisplayKind);
        Assert.NotEqual(default, before.CreatedAtUtc);
        Assert.IsType<NotFoundResult>(await Controller(s, s.Provider, storage).Upload(s.RequestId, EvidenceKind.After, Png(), default));
        Assert.Equal(MutationResult.Success, await s.Workflow.TransitionAsync(s.Provider, s.RequestId,
            MaintenanceRequestStatus.Accepted, MaintenanceRequestStatus.InProgress, default));
        await Controller(s, s.Provider, storage).Upload(s.RequestId, EvidenceKind.After, Png(), default);
        var after = await s.Db.RequestEvidence.AsNoTracking().SingleAsync(e => e.MaintenanceRequestId == s.RequestId && e.Kind == EvidenceKind.After);
        Assert.Equal(s.ProviderId, after.UploadedById);
        Assert.True(after.OwnershipChecked);
        var details = (await s.Services.GetRequiredService<RequestDetailsService>().GetAsync(s.Customer, s.RequestId, 1, default))!;
        Assert.Equal(1, details.Evidence.BeforeCount);
        Assert.Equal(1, details.Evidence.AfterCount);
        Assert.Equal(before.Id, details.Evidence.BeforeImageId);
        Assert.Equal(after.Id, details.Evidence.AfterImageId);
    }

    [SqlServerFact]
    public async Task UnrelatedAndAnonymousCannotUploadOrRead_PrivateImagesStayPrivate()
    {
        fixture.RequireLocalDb();
        await using var s = await fixture.CreateScenarioAsync();
        await using var other = await fixture.CreateScenarioAsync();
        using var storage = new TestStorage();
        await Controller(s, s.Customer, storage).Upload(s.RequestId, EvidenceKind.Before, Png(), default);
        var evidence = await s.Db.RequestEvidence.SingleAsync(e => e.MaintenanceRequestId == s.RequestId);
        foreach (var actor in new[] { s.Unrelated, other.Provider })
        {
            var c = Controller(s, actor, storage);
            foreach (var kind in new[] { EvidenceKind.Before, EvidenceKind.After, EvidenceKind.General, (EvidenceKind)123 })
                Assert.IsType<NotFoundResult>(await c.Upload(s.RequestId, kind, Png(), default));
            Assert.IsType<NotFoundResult>(await c.Image(evidence.Id, default));
            Assert.IsType<NotFoundResult>(await c.Index(s.RequestId));
        }
        Assert.IsType<ChallengeResult>(await Controller(s, new ClaimsPrincipal(new ClaimsIdentity()), storage)
            .Upload(s.RequestId, EvidenceKind.Before, Png(), default));
        Assert.NotEmpty(typeof(RequestEvidenceController).GetCustomAttributes(typeof(AuthorizeAttribute), true));
        var image = Assert.IsType<FileStreamResult>(await Controller(s, s.Customer, storage).Image(evidence.Id, default));
        await image.FileStream.DisposeAsync();
        var publicData = await s.Db.ProviderProfiles.Select(PublicProviderViewModel.Projection).ToListAsync();
        Assert.DoesNotContain(evidence.StorageKey, JsonSerializer.Serialize(publicData));
        Assert.DoesNotContain("Evidence", JsonSerializer.Serialize(publicData));
        Assert.False(Directory.Exists(Path.Combine(storage.Root, "wwwroot")));
    }

    [SqlServerFact]
    public async Task LifecycleBlocksLateBeforeAndEarlyOrLateAfter_AndRequiresTruthfulAgreement()
    {
        fixture.RequireLocalDb();
        await using var s = await fixture.CreateScenarioAsync();
        using var storage = new TestStorage();
        foreach (var state in Enum.GetValues<MaintenanceRequestStatus>())
        {
            await s.Db.MaintenanceRequests.Where(r => r.Id == s.RequestId).ExecuteUpdateAsync(x => x
                .SetProperty(r => r.Status, state)
                .SetProperty(r => r.StartedAtUtc, state == MaintenanceRequestStatus.InProgress || state == MaintenanceRequestStatus.Completed ? DateTime.UtcNow : (DateTime?)null));
            var grant = await s.Services.GetRequiredService<RequestAccessService>().GetAsync(s.Customer, s.RequestId, default);
            var policy = s.Services.GetRequiredService<RequestEvidencePolicy>();
            Assert.Equal(state is MaintenanceRequestStatus.Pending or MaintenanceRequestStatus.Accepted ? EvidenceKind.Before : (EvidenceKind?)null,
                await policy.UploadKindAsync(grant, default));
            var result = await Controller(s, s.Provider, storage).Upload(s.RequestId, EvidenceKind.After, Png(), default);
            Assert.Equal(state == MaintenanceRequestStatus.InProgress, result is RedirectToActionResult);
        }
        await using var unagreed = await fixture.CreateScenarioAsync(withAgreement: false);
        await unagreed.Db.MaintenanceRequests.Where(r => r.Id == unagreed.RequestId).ExecuteUpdateAsync(x => x
            .SetProperty(r => r.Status, MaintenanceRequestStatus.InProgress).SetProperty(r => r.StartedAtUtc, DateTime.UtcNow));
        Assert.IsType<NotFoundResult>(await Controller(unagreed, unagreed.Provider, storage)
            .Upload(unagreed.RequestId, EvidenceKind.After, Png(), default));
    }

    [SqlServerFact]
    public async Task StateChangeDuringImageProcessingIsRechecked_AndUncommittedFileIsRemoved()
    {
        fixture.RequireLocalDb();
        await using var s = await fixture.CreateScenarioAsync();
        using var storage = new TestStorage();
        storage.AfterSave = async () => Assert.Equal(MutationResult.Success,
            await s.Workflow.TransitionAsync(s.Provider, s.RequestId, MaintenanceRequestStatus.Accepted, MaintenanceRequestStatus.InProgress, default));
        await Controller(s, s.Customer, storage).Upload(s.RequestId, EvidenceKind.Before, Png(), default);
        Assert.Empty(await s.Db.RequestEvidence.Where(e => e.MaintenanceRequestId == s.RequestId).ToListAsync());
        Assert.Empty(Directory.GetFiles(storage.Root, "*", SearchOption.AllDirectories));
    }

    [SqlServerFact]
    public async Task EvidenceIsOptionalForStartAndCompletion_CommunicationSurvives()
    {
        fixture.RequireLocalDb();
        await using var s = await fixture.CreateScenarioAsync();
        await s.Db.Users.Where(u => u.Id == s.CustomerId || u.Id == s.ProviderId)
            .ExecuteUpdateAsync(x => x.SetProperty(u => u.PhoneNumber, "+970599123456"));
        s.Db.ChangeTracker.Clear();
        var service = s.Services.GetRequiredService<RequestDetailsService>();
        var active = (await service.GetAsync(s.Customer, s.RequestId, 1, default))!;
        Assert.True(active.Communication.CanSend);
        Assert.NotNull(active.Communication.ContactPhone);
        using var storage = new TestStorage();
        var customerController = Controller(s, s.Customer, storage);
        var chat = new RequestMessagesController(s.Db, s.Services.GetRequiredService<RequestAccessService>(),
            s.Services.GetRequiredService<RequestCommunicationPolicy>(), s.Services.GetRequiredService<RequestMutationService>())
        { ControllerContext = customerController.ControllerContext, TempData = customerController.TempData };
        await chat.Send(s.RequestId, "Private request message", default);
        Assert.Equal(1, await s.Db.RequestMessages.CountAsync(m => m.MaintenanceRequestId == s.RequestId));
        Assert.Equal(MutationResult.Success, await s.Workflow.TransitionAsync(s.Provider, s.RequestId,
            MaintenanceRequestStatus.Accepted, MaintenanceRequestStatus.InProgress, default));
        await s.AddAcceptedFinalPriceAsync();
        Assert.Equal(MutationResult.Success, await s.Workflow.TransitionAsync(s.Provider, s.RequestId,
            MaintenanceRequestStatus.InProgress, MaintenanceRequestStatus.Completed, default));
        Assert.Empty(await s.Db.RequestEvidence.Where(e => e.MaintenanceRequestId == s.RequestId).ToListAsync());
        var completed = (await service.GetAsync(s.Customer, s.RequestId, 1, default))!;
        Assert.False(completed.Communication.CanSend);
        Assert.True(completed.Communication.CanReadHistory);
        await chat.Send(s.RequestId, "Must not send after completion", default);
        Assert.Equal(1, await s.Db.RequestMessages.CountAsync(m => m.MaintenanceRequestId == s.RequestId));
        var history = Assert.IsType<RequestMessagesViewModel>(Assert.IsType<ViewResult>(await chat.Index(s.RequestId)).Model);
        Assert.Single(history.Messages.Items);
        chat.ControllerContext = Controller(s, s.Unrelated, storage).ControllerContext;
        Assert.IsType<NotFoundResult>(await chat.Send(s.RequestId, "Unauthorized", default));
    }

    [SqlServerFact]
    public async Task FakeTruncatedAndOversizedImagesAreRejected_WithoutRowsOrFiles()
    {
        fixture.RequireLocalDb();
        await using var s = await fixture.CreateScenarioAsync();
        using var storage = new TestStorage();
        foreach (var file in new[] {
            File(Encoding.UTF8.GetBytes("<script>fake PNG</script>")),
            File([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
            File(new byte[LocalPrivateMediaStorage.MaxBytes + 1]), File([]) })
        {
            var c = Controller(s, s.Customer, storage);
            await c.Upload(s.RequestId, EvidenceKind.Before, file, default);
            Assert.True(c.TempData.ContainsKey("ErrorMessage"));
        }
        Assert.Empty(await s.Db.RequestEvidence.Where(e => e.MaintenanceRequestId == s.RequestId).ToListAsync());
        Assert.Empty(Directory.GetFiles(storage.Root, "*", SearchOption.AllDirectories));
        await Assert.ThrowsAsync<InvalidDataException>(() => storage.OpenAsync("../outside.png", default));
    }

    [SqlServerFact]
    public async Task HistoricalImagesRemainReadableAndGeneral_AndTwentyImageLimitIsPreserved()
    {
        fixture.RequireLocalDb();
        await using var s = await fixture.CreateScenarioAsync();
        using var storage = new TestStorage();
        var oldFile = await storage.SaveAsync(Png(), default);
        for (var i = 0; i < 20; i++)
            s.Db.RequestEvidence.Add(new RequestEvidence { MaintenanceRequestId = s.RequestId,
                UploadedById = s.CustomerId, Kind = EvidenceKind.After, OwnershipChecked = false,
                StorageKey = oldFile.Key, ContentType = oldFile.ContentType, Size = oldFile.Size, CreatedAtUtc = DateTime.UtcNow });
        await s.Db.SaveChangesAsync();
        var c = Controller(s, s.Customer, storage);
        var index = Assert.IsType<ViewResult>(await c.Index(s.RequestId));
        var model = Assert.IsType<EvidenceViewModel>(index.Model);
        Assert.All(model.Evidence.Items, e => Assert.Equal(EvidenceKind.General, e.DisplayKind));
        var image = Assert.IsType<FileStreamResult>(await c.Image(model.Evidence.Items[0].Id, default));
        await image.FileStream.DisposeAsync();
        await c.Upload(s.RequestId, EvidenceKind.Before, Png(), default);
        Assert.True(c.TempData.ContainsKey("ErrorMessage"));
        Assert.Equal(20, await s.Db.RequestEvidence.CountAsync(e => e.MaintenanceRequestId == s.RequestId));
        Assert.Single(Directory.GetFiles(storage.Root, "*", SearchOption.AllDirectories));
        var details = (await s.Services.GetRequiredService<RequestDetailsService>().GetAsync(s.Customer, s.RequestId, 1, default))!;
        Assert.Equal(20, details.Evidence.GeneralCount);
        Assert.Equal(0, details.Evidence.AfterCount);
        Assert.Null(details.Evidence.AfterImageId);
    }

    [SqlServerFact]
    public async Task MigrationPreservesHistoricalLabelsAndMetadata_ButDisplaysGeneral()
    {
        fixture.RequireLocalDb();
        var name = "FixPal_EvidenceMigration_" + Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer($"Server=(localdb)\\mssqllocaldb;Database={name};Trusted_Connection=True;TrustServerCertificate=True").Options;
        await using var db = new ApplicationDbContext(options);
        try
        {
            await db.GetService<IMigrator>().MigrateAsync("20260917193231_AddAppointmentConfirmationLifecycle");
            var user = new ApplicationUser { Id = "historical-owner", UserName = "historical-owner" };
            var request = new MaintenanceRequest { Customer = user, Title = "Old request", Description = "Old evidence", RequestType = RequestType.PrivateService,
                ServiceCategory = new ServiceCategory { Name = "Test" }, Area = new Area { Name = "Test", City = new City { Name = "Test", IsActive = true } } };
            db.Add(request);
            await db.SaveChangesAsync();
            var time = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var key = new string('a', 32) + ".png";
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO RequestEvidence (MaintenanceRequestId, UploadedById, Kind, StorageKey, ContentType, Size, CreatedAtUtc) VALUES ({request.Id}, {user.Id}, {2}, {key}, {"image/png"}, {12L}, {time})");
            await db.Database.MigrateAsync();
            var old = await db.RequestEvidence.AsNoTracking().SingleAsync();
            Assert.Equal(EvidenceKind.After, old.Kind);
            Assert.False(old.OwnershipChecked);
            Assert.Equal(EvidenceKind.General, old.DisplayKind);
            Assert.Equal(user.Id, old.UploadedById);
            Assert.Equal(key, old.StorageKey);
            Assert.Equal(time, old.CreatedAtUtc);
        }
        finally { await db.Database.EnsureDeletedAsync(); }
    }

    private static RequestEvidenceController Controller(TestScenario s, ClaimsPrincipal user, IPrivateMediaStorage storage)
    {
        var http = new DefaultHttpContext { User = user };
        return new(s.Db, s.Services.GetRequiredService<RequestAccessService>(), storage,
            NullLogger<RequestEvidenceController>.Instance, s.Services.GetRequiredService<RequestEvidencePolicy>(),
            s.Services.GetRequiredService<RequestMutationService>())
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new EmptyTempDataProvider())
        };
    }
    private static IFormFile Png()
    {
        using var image = new Image<Rgba32>(2, 2);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return File(stream.ToArray());
    }
    private static IFormFile File(byte[] bytes) => new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "untrusted.png")
    { Headers = new HeaderDictionary(), ContentType = "image/png" };
    private sealed class EmptyTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
    private sealed class TestStorage : IPrivateMediaStorage, IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "FixPal_Evidence_" + Guid.NewGuid().ToString("N"));
        private readonly LocalPrivateMediaStorage inner;
        public Func<Task>? AfterSave { get; set; }
        public TestStorage()
        {
            Directory.CreateDirectory(Root);
            inner = new LocalPrivateMediaStorage(WebApplication.CreateBuilder(new WebApplicationOptions
            { ContentRootPath = Root, EnvironmentName = "Testing" }).Environment);
        }
        public async Task<StoredMedia> SaveAsync(IFormFile file, CancellationToken ct)
        {
            var saved = await inner.SaveAsync(file, ct);
            if (AfterSave != null) await AfterSave();
            return saved;
        }
        public Task<Stream> OpenAsync(string key, CancellationToken ct) => inner.OpenAsync(key, ct);
        public Task DeleteAsync(string key, CancellationToken ct) => inner.DeleteAsync(key, ct);
        public void Dispose() => Directory.Delete(Root, true);
    }
}
