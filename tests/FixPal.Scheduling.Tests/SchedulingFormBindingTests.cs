using System.Globalization;
using FixPal.Controllers;
using FixPal.Models.Enums;
using FixPal.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace FixPal.Scheduling.Tests;

[Collection(nameof(SqlSchedulingCollection))]
public sealed class SchedulingFormBindingTests(SqlSchedulingFixture fixture)
{
    [Theory]
    [InlineData("ar-PS")]
    [InlineData("en-US")]
    [InlineData("")]
    public async Task BrowserCivilTimeRetainsUnspecifiedKind(string culture)
    {
        var (command, state) = await BindAsync(1, "2030-01-07T10:00", "2030-01-07T11:00", culture);
        Assert.True(state.IsValid);
        Assert.Equal(DateTimeKind.Unspecified, command.StartLocal.Kind);
        Assert.Equal(DateTimeKind.Unspecified, command.EndLocal.Kind);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Hebron");
        var result = new SchedulingTimePolicy(TimeProvider.System)
            .ResolveLocalInterval(command.StartLocal, command.EndLocal, zone);
        Assert.NotNull(result.Interval);
        Assert.Equal(command.StartLocal, TimeZoneInfo.ConvertTime(result.Interval!.StartUtc, zone).DateTime);
    }

    [SqlServerFact]
    public async Task BoundFormsUseProviderCivilTimeAndReportSpecificValidationCodes()
    {
        fixture.RequireLocalDb();
        var cases = new[]
        {
            ("2030-01-07T09:00", "2030-01-07T17:00", (string?)null, (string?)null),
            ("2030-01-07T16:00", "2030-01-07T18:00", "OutsideWorkingHours", "الموعد المختار خارج ساعات عمل مزود الخدمة."),
            ("2029-01-01T10:00", "2029-01-01T11:00", "StartMustBeFuture", "يجب اختيار موعد في المستقبل."),
            ("2030-01-07T11:00", "2030-01-07T10:00", "InvalidInterval", "يجب أن يكون وقت نهاية الموعد بعد وقت البداية."),
            ("2030-01-07T10:00", "2030-01-07T10:00", "InvalidInterval", "يجب أن يكون وقت نهاية الموعد بعد وقت البداية."),
            ("2030-01-07T10:00:01", "2030-01-07T11:00", "MinutePrecisionRequired", "اختر الوقت بالساعات والدقائق فقط، دون ثوانٍ أو أجزاء من الثانية.")
        };
        foreach (var (start, end, code, message) in cases)
        {
            await using var scenario = await fixture.CreateScenarioAsync();
            await ConfigureHebronAsync(scenario);
            var (command, state) = await BindAsync(scenario.RequestId, start, end);
            Assert.True(state.IsValid);
            var log = new CapturingLogger();
            var controller = Controller(scenario, log, state);
            Assert.IsType<RedirectToActionResult>(await controller.Schedule(command, default));
            if (code == null)
            {
                Assert.True(controller.TempData.ContainsKey("SuccessMessage"));
                var saved = Assert.Single(await scenario.AppointmentsAsync());
                Assert.Equal("Asia/Hebron", saved.TimeZoneId);
                var zone = TimeZoneInfo.FindSystemTimeZoneById(saved.TimeZoneId);
                Assert.Equal(command.StartLocal, TimeZoneInfo.ConvertTime(saved.StartUtc, zone).DateTime);
                Assert.Equal(command.EndLocal, TimeZoneInfo.ConvertTime(saved.EndUtc, zone).DateTime);
                Assert.Empty(log.Messages);
            }
            else
            {
                Assert.Equal(message, controller.TempData["ErrorMessage"]);
                Assert.Contains(log.Messages, m => m.Contains(code));
                Assert.Empty(await scenario.AppointmentsAsync());
            }
        }
    }

    [SqlServerFact]
    public async Task BoundFormBlackoutIsRejectedWithUsefulFeedback()
    {
        fixture.RequireLocalDb();
        await using var scenario = await fixture.CreateScenarioAsync();
        await ConfigureHebronAsync(scenario);
        var (command, state) = await BindAsync(scenario.RequestId, "2030-01-07T10:00", "2030-01-07T11:00");
        var interval = new SchedulingTimePolicy(TimeProvider.System).ResolveLocalInterval(command.StartLocal,
            command.EndLocal, TimeZoneInfo.FindSystemTimeZoneById("Asia/Hebron")).Interval!;
        await scenario.AddBlackoutAsync(interval.StartUtc, interval.EndUtc);
        var log = new CapturingLogger();
        var controller = Controller(scenario, log, state);
        await controller.Schedule(command, default);
        Assert.Equal("مزود الخدمة غير متاح خلال هذه الفترة. اختر وقتًا آخر.", controller.TempData["ErrorMessage"]);
        Assert.Contains(log.Messages, m => m.Contains("BlackoutConflict"));
        Assert.Empty(await scenario.AppointmentsAsync());
    }

    [SqlServerFact]
    public async Task BoundProposalsAllowAdjacencyButConfirmationRejectsRealOverlap()
    {
        fixture.RequireLocalDb();
        await using var occupied = await fixture.CreateScenarioAsync();
        await ConfigureHebronAsync(occupied);
        await occupied.CreateConfirmedAsync(10, 11);
        foreach (var overlaps in new[] { true, false })
        {
            await using var scenario = await fixture.CreateScenarioAsync(existingProviderId: occupied.ProviderProfileId,
                providerUserId: occupied.ProviderId);
            var (command, state) = await BindAsync(scenario.RequestId,
                overlaps ? "2030-01-07T10:00" : "2030-01-07T11:00",
                overlaps ? "2030-01-07T11:00" : "2030-01-07T12:00");
            var log = new CapturingLogger();
            var controller = Controller(scenario, log, state);
            await controller.Schedule(command, default);
            Assert.True(controller.TempData.ContainsKey("SuccessMessage"));
            var proposal = Assert.Single(await scenario.AppointmentsAsync());
            controller.HttpContext.User = scenario.Provider;
            controller.TempData.Clear();
            await controller.Confirm(scenario.Decision(proposal), default);
            if (overlaps)
            {
                Assert.Equal("هذا الوقت محجوز أو غير متاح. اختر وقتًا آخر.", controller.TempData["ErrorMessage"]);
                Assert.Contains(log.Messages, m => m.Contains("ProviderTimeConflict"));
                Assert.Equal(AppointmentStatus.Proposed, Assert.Single(await scenario.AppointmentsAsync()).Status);
            }
            else
            {
                Assert.True(controller.TempData.ContainsKey("SuccessMessage"));
                Assert.Equal(AppointmentStatus.Confirmed, Assert.Single(await scenario.AppointmentsAsync()).Status);
            }
        }
    }

    [SqlServerFact]
    public async Task MalformedFormDoesNotReachMutationOrExposeSubmittedText()
    {
        fixture.RequireLocalDb();
        await using var scenario = await fixture.CreateScenarioAsync();
        var (command, state) = await BindAsync(scenario.RequestId, "private-invalid-value", "2030-01-07T11:00");
        Assert.False(state.IsValid);
        var log = new CapturingLogger();
        var controller = Controller(scenario, log, state);
        await controller.Schedule(command, default);
        Assert.Contains("تعذر قراءة بيانات الموعد", (string)controller.TempData["ErrorMessage"]!);
        Assert.Contains(log.Messages, m => m.Contains("InvalidFormInput"));
        Assert.DoesNotContain(log.Messages, m => m.Contains("private-invalid-value"));
        Assert.Empty(await scenario.AppointmentsAsync());
    }

    private static async Task ConfigureHebronAsync(TestScenario scenario)
    {
        await scenario.Db.ProviderCalendars.Where(c => c.ProviderProfileId == scenario.ProviderProfileId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.TimeZoneId, "Asia/Hebron"));
        await scenario.ReplaceWorkingHoursAsync(new TimeOnly(9, 0), new TimeOnly(17, 0));
    }

    private static AppointmentsController Controller(TestScenario scenario, CapturingLogger log, ModelStateDictionary state)
    {
        var http = new DefaultHttpContext { User = scenario.Customer };
        return new AppointmentsController(scenario.CustomerService, log)
        {
            ControllerContext = new ControllerContext(new ActionContext(http, new RouteData(),
                new ControllerActionDescriptor { ActionName = "Schedule" }, state)),
            TempData = new TempDataDictionary(http, new MemoryTempDataProvider())
        };
    }

    private static async Task<(ScheduleAppointmentCommand, ModelStateDictionary)> BindAsync(
        int requestId, string start, string end, string culture = "ar-PS")
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddControllersWithViews();
        await using var app = builder.Build();
        await using var scope = app.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var metadata = services.GetRequiredService<IModelMetadataProvider>().GetMetadataForType(typeof(ScheduleAppointmentCommand));
        var binder = services.GetRequiredService<IModelBinderFactory>().CreateBinder(new ModelBinderFactoryContext { Metadata = metadata });
        var action = new ActionContext(new DefaultHttpContext { RequestServices = services }, new RouteData(), new ControllerActionDescriptor());
        var form = new FormCollection(new Dictionary<string, StringValues>
        {
            ["MaintenanceRequestId"] = requestId.ToString(CultureInfo.InvariantCulture),
            ["StartLocal"] = start, ["EndLocal"] = end
        });
        var context = DefaultModelBindingContext.CreateBindingContext(action,
            new FormValueProvider(BindingSource.Form, form, CultureInfo.GetCultureInfo(culture)), metadata, null, "");
        await binder.BindModelAsync(context);
        var command = Assert.IsType<ScheduleAppointmentCommand>(context.Result.Model);
        services.GetRequiredService<IObjectModelValidator>().Validate(action, context.ValidationState, "", command);
        return (command, action.ModelState);
    }

    private sealed class MemoryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class CapturingLogger : ILogger<AppointmentsController>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
