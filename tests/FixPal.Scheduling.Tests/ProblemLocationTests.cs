using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using FixPal.Controllers;
using FixPal.Models;
using FixPal.Models.Enums;
using FixPal.Models.ViewModels;
using FixPal.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FixPal.Scheduling.Tests;

[Collection(nameof(SqlSchedulingCollection))]
public sealed class ProblemLocationTests(SqlSchedulingFixture fixture)
{
    private const double Latitude = 31.81234;
    private const double Longitude = 35.23456;

    [SqlServerFact]
    public async Task BeforeAgreement_HtmlAndJsonContainNoCoordinatesOrDirections()
    {
        fixture.RequireLocalDb();
        await using var s = await fixture.CreateScenarioAsync(withAgreement: false);
        await SetPoint(s);
        var model = await Details(s, s.Provider);
        Assert.NotNull(model);
        Assert.False(model.CanViewProblemLocation);
        Assert.Null(model.Latitude);
        Assert.Null(model.Longitude);
        Assert.False(model.Communication.CanSend);
        Assert.DoesNotContain(Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture), JsonSerializer.Serialize(model));
        var html = await Render(model);
        Assert.DoesNotContain("31.81234", html);
        Assert.DoesNotContain("35.23456", html);
        Assert.DoesNotContain("data-latitude", html);
        Assert.DoesNotContain("destination=", html);
        Assert.Contains(model.Location, System.Net.WebUtility.HtmlDecode(html));
    }

    [SqlServerFact]
    public async Task TruthfulAgreement_UnlocksOnlyAssignedProviderDuringActiveService()
    {
        fixture.RequireLocalDb();
        await using var s = await fixture.CreateScenarioAsync();
        await SetPoint(s);
        foreach (var state in Enum.GetValues<MaintenanceRequestStatus>())
        {
            await s.Db.MaintenanceRequests.Where(r => r.Id == s.RequestId)
                .ExecuteUpdateAsync(x => x.SetProperty(r => r.Status, state));
            var model = (await Details(s, s.Provider))!;
            var allowed = state is MaintenanceRequestStatus.Accepted or MaintenanceRequestStatus.InProgress;
            Assert.Equal(allowed, model.CanViewProblemLocation);
            Assert.Equal(allowed ? Latitude : (double?)null, model.Latitude);
            var html = await Render(model);
            Assert.Equal(allowed, html.Contains("destination="));
            Assert.Equal(allowed, html.Contains("31.81234"));
            Assert.Equal(Latitude, (await Details(s, s.Customer))!.Latitude);
        }
    }

    [SqlServerFact]
    public async Task AnonymousAndUnrelatedActorsCannotRetrieveLocation()
    {
        fixture.RequireLocalDb();
        await using var s = await fixture.CreateScenarioAsync();
        await using var other = await fixture.CreateScenarioAsync();
        await SetPoint(s);
        Assert.Null(await Details(s, new ClaimsPrincipal(new ClaimsIdentity())));
        Assert.Null(await Details(s, s.Unrelated));
        Assert.Null(await Details(s, other.Provider));
        Assert.Equal(Latitude, (await Details(s, s.Customer))!.Latitude);
    }

    [SqlServerFact]
    public async Task AdministrativeInspectionDoesNotGrantExactLocation()
    {
        fixture.RequireLocalDb();
        await using var s = await fixture.CreateScenarioAsync();
        await SetPoint(s);
        var users = s.Services.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.GetUserAsync(s.Unrelated))!;
        Assert.True((await users.AddToRoleAsync(user, FixPal.Infrastructure.Identity.AppRoles.Admin)).Succeeded);
        var admin = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Role, FixPal.Infrastructure.Identity.AppRoles.Admin)
        ], "Test"));
        var model = (await Details(s, admin))!;
        Assert.NotNull(model);
        Assert.False(model.CanViewProblemLocation);
        Assert.Null(model.Latitude);
        Assert.DoesNotContain("31.81234", await Render(model));
    }

    [SqlServerFact]
    public async Task MissingDecisionCannotMasqueradeAsAgreement()
    {
        fixture.RequireLocalDb();
        await using var s = await fixture.CreateScenarioAsync();
        await SetPoint(s);
        await s.Db.QuoteDecisions.Where(d => s.Db.RequestQuotes.Any(q => q.Id == d.RequestQuoteId
            && q.MaintenanceRequestId == s.RequestId)).ExecuteDeleteAsync();
        var model = (await Details(s, s.Provider))!;
        Assert.False(model.HasAgreement);
        Assert.Null(model.Latitude);
    }

    [SqlServerFact]
    public async Task LegacyMissingPointRendersHonestly_AndDiscoveryContainsNoCoordinates()
    {
        fixture.RequireLocalDb();
        await using var s = await fixture.CreateScenarioAsync();
        var model = (await Details(s, s.Customer))!;
        Assert.True(model.CanViewProblemLocation);
        Assert.Null(model.Latitude);
        var html = System.Net.WebUtility.HtmlDecode(await Render(model));
        Assert.Contains("لم يُحدد موقع دقيق", html);
        Assert.DoesNotContain("destination=", html);
        await SetPoint(s);
        var r = await s.Db.MaintenanceRequests.AsNoTracking().SingleAsync(r => r.Id == s.RequestId);
        var matches = await new ProviderMatchingService(s.Db).FindAsync(r.ServiceCategoryId, r.AreaId, null, default);
        Assert.DoesNotContain("Latitude", JsonSerializer.Serialize(matches));
        Assert.DoesNotContain("Longitude", JsonSerializer.Serialize(matches));
        var publicProviders = await s.Db.ProviderProfiles.Select(PublicProviderViewModel.Projection).ToListAsync();
        Assert.DoesNotContain("31.81234", JsonSerializer.Serialize(publicProviders));
        Assert.DoesNotContain("Latitude", JsonSerializer.Serialize(publicProviders));
    }

    [SqlServerFact]
    public async Task CreateAcceptsCityAreaOnlyAndOptionalPoint_RejectsIncompleteAndNonfinitePoint()
    {
        fixture.RequireLocalDb();
        await using var s = await fixture.CreateScenarioAsync();
        var r = await s.Db.MaintenanceRequests.AsNoTracking().SingleAsync(r => r.Id == s.RequestId);
        var area = await s.Db.Areas.SingleAsync(a => a.Id == r.AreaId);
        foreach (var point in new (double? Lat, double? Lng, bool Valid)[]
        {
            (null, null, true), (Latitude, Longitude, true), (Latitude, null, false),
            (null, Longitude, false), (double.NaN, Longitude, false), (Latitude, double.PositiveInfinity, false),
            (91, Longitude, false), (Latitude, -181, false)
        })
        {
            var context = new DefaultHttpContext { User = s.Customer };
            var controller = new MaintenanceRequestsController(s.Db,
                s.Services.GetRequiredService<UserManager<ApplicationUser>>(), new ProviderMatchingService(s.Db),
                s.Services.GetRequiredService<RequestDetailsService>())
            {
                ControllerContext = new ControllerContext { HttpContext = context },
                TempData = new TempDataDictionary(context, new EmptyTempDataProvider())
            };
            var model = new CreateMaintenanceRequestViewModel
            {
                Title = "Location test", Description = "Problem location creation test", AreaId = area.Id,
                CityId = area.CityId, ServiceCategoryId = r.ServiceCategoryId, Latitude = point.Lat, Longitude = point.Lng
            };
            var errors = new List<ValidationResult>();
            Validator.TryValidateObject(model, new ValidationContext(model), errors, true);
            foreach (var error in errors) controller.ModelState.AddModelError(error.MemberNames.FirstOrDefault() ?? "", error.ErrorMessage!);
            var before = await s.Db.MaintenanceRequests.CountAsync();
            var result = await controller.Create(model, default);
            Assert.Equal(point.Valid, result is RedirectToActionResult);
            Assert.Equal(before + (point.Valid ? 1 : 0), await s.Db.MaintenanceRequests.CountAsync());
        }
    }

    [Fact]
    public void GeneralProjectionIsPrivateByDefault_AndThereIsNoLocationEditAction()
    {
        var request = new MaintenanceRequest
        {
            Latitude = Latitude, Longitude = Longitude,
            ServiceCategory = new ServiceCategory(), Area = new Area { City = new City() }
        };
        var model = RequestDetailsViewModel.DetailProjection.Compile()(request);
        Assert.Null(model.Latitude);
        Assert.Null(model.Longitude);
        Assert.False(model.CanViewProblemLocation);
        var actions = typeof(MaintenanceRequestsController).GetMethods()
            .Where(m => m.DeclaringType == typeof(MaintenanceRequestsController)).Select(m => m.Name);
        Assert.DoesNotContain("Edit", actions);
        Assert.DoesNotContain("Update", actions);
    }

    private static Task SetPoint(TestScenario s) => s.Db.MaintenanceRequests.Where(r => r.Id == s.RequestId)
        .ExecuteUpdateAsync(x => x.SetProperty(r => r.Latitude, Latitude).SetProperty(r => r.Longitude, Longitude));

    private static Task<RequestDetailsViewModel?> Details(TestScenario s, ClaimsPrincipal user) =>
        s.Services.GetRequiredService<RequestDetailsService>().GetAsync(user, s.RequestId, 1, default);

    private static async Task<string> Render(RequestDetailsViewModel model)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(MaintenanceRequestsController).Assembly.GetName().Name,
            EnvironmentName = "Testing"
        });
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(MaintenanceRequestsController).Assembly);
        await using var app = builder.Build();
        await using var scope = app.Services.CreateAsyncScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var action = new ActionContext(http, new RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
        var view = scope.ServiceProvider.GetRequiredService<ICompositeViewEngine>()
            .GetView(null, "/Views/Shared/_ProblemLocation.cshtml", false);
        Assert.True(view.Success, string.Join(",", view.SearchedLocations ?? []));
        using var output = new StringWriter();
        var data = new ViewDataDictionary<RequestDetailsViewModel>(new EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = model };
        await view.View.RenderAsync(new ViewContext(action, view.View, data,
            new TempDataDictionary(http, new EmptyTempDataProvider()), output, new HtmlHelperOptions()));
        return output.ToString();
    }

    private sealed class EmptyTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
