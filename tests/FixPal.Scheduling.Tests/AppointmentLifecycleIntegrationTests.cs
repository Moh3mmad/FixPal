using System.Security.Claims;
using System.Text.Json;
using FixPal.Controllers;
using FixPal.Data;
using FixPal.Infrastructure.Identity;
using FixPal.Models;
using FixPal.Models.Enums;
using FixPal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Sdk;

namespace FixPal.Scheduling.Tests;

[CollectionDefinition(nameof(SqlSchedulingCollection), DisableParallelization = true)]
public sealed class SqlSchedulingCollection : ICollectionFixture<SqlSchedulingFixture>;

[Collection(nameof(SqlSchedulingCollection))]
public sealed class AppointmentLifecycleIntegrationTests(SqlSchedulingFixture fixture)
{
    [SqlServerFact]
    public async Task Proposal_RequiresTruthfulAcceptedAgreement()
    {
        fixture.RequireLocalDb();
        var scenario = await fixture.CreateScenarioAsync(withAgreement: false);
        var result = await scenario.CustomerService.ScheduleAsync(scenario.Customer,
            scenario.Schedule(10, 11), default);

        Assert.Equal(AppointmentResultStatus.Conflict, result.Status);
        Assert.Contains(result.Errors, e => e.Code == "AgreementRequired");
        Assert.Empty(await scenario.AppointmentsAsync());
    }

    [SqlServerFact]
    public async Task ParticipantsMayPropose_ButUnrelatedUserMayNot()
    {
        fixture.RequireLocalDb();
        var customerScenario = await fixture.CreateScenarioAsync();
        Assert.Equal(AppointmentResultStatus.Success,
            (await customerScenario.CustomerService.ScheduleAsync(customerScenario.Customer,
                customerScenario.Schedule(10, 11), default)).Status);

        var providerScenario = await fixture.CreateScenarioAsync();
        Assert.Equal(AppointmentResultStatus.Success,
            (await providerScenario.ProviderService.ScheduleAsync(providerScenario.Provider,
                providerScenario.Schedule(12, 13), default)).Status);

        var unrelatedScenario = await fixture.CreateScenarioAsync();
        Assert.Equal(AppointmentResultStatus.Forbidden,
            (await unrelatedScenario.CustomerService.ScheduleAsync(unrelatedScenario.Unrelated,
                unrelatedScenario.Schedule(14, 15), default)).Status);
    }

    [SqlServerFact]
    public async Task ProposalDoesNotOccupy_AndProposerCannotConfirm()
    {
        fixture.RequireLocalDb();
        var scenario = await fixture.CreateScenarioAsync();
        await scenario.CustomerService.ScheduleAsync(scenario.Customer, scenario.Schedule(10, 11), default);
        await scenario.CustomerService.ScheduleAsync(scenario.Customer, scenario.Schedule(10, 11), default);
        var proposals = await scenario.AppointmentsAsync();

        Assert.Equal(2, proposals.Count);
        Assert.All(proposals, a => Assert.Equal(AppointmentStatus.Proposed, a.Status));
        var decision = scenario.Decision(proposals[0]);
        var result = await scenario.CustomerService.ConfirmAsync(scenario.Customer, decision, default);
        Assert.Equal(AppointmentResultStatus.Forbidden, result.Status);
        Assert.Contains(result.Errors, e => e.Code == "ProposerCannotDecide");
    }

    [SqlServerFact]
    public async Task InitialProposalIsBlockedWhenRequestAlreadyHasConfirmedOrInProgressWork()
    {
        fixture.RequireLocalDb();
        var confirmedScenario = await fixture.CreateScenarioAsync();
        await confirmedScenario.CreateConfirmedAsync(10, 11);

        var confirmedResult = await confirmedScenario.CustomerService.ScheduleAsync(
            confirmedScenario.Customer, confirmedScenario.Schedule(12, 13), default);
        Assert.Equal(AppointmentResultStatus.Conflict, confirmedResult.Status);
        Assert.Contains(confirmedResult.Errors, e => e.Code == "RequestAlreadyScheduled");
        Assert.Single(await confirmedScenario.AppointmentsAsync());

        var inProgressScenario = await fixture.CreateScenarioAsync();
        await inProgressScenario.CreateConfirmedAsync(10, 11);
        Assert.Equal(MutationResult.Success, await inProgressScenario.Workflow.TransitionAsync(
            inProgressScenario.Provider, inProgressScenario.RequestId, MaintenanceRequestStatus.Accepted,
            MaintenanceRequestStatus.InProgress, default));

        var inProgressResult = await inProgressScenario.CustomerService.ScheduleAsync(
            inProgressScenario.Customer, inProgressScenario.Schedule(12, 13), default);
        Assert.Equal(AppointmentResultStatus.Conflict, inProgressResult.Status);
        Assert.DoesNotContain(await inProgressScenario.AppointmentsAsync(),
            a => a.Status == AppointmentStatus.Proposed);
    }

    [SqlServerFact]
    public async Task OtherParticipantCanConfirmOrReject_WithAuditAndHistory()
    {
        fixture.RequireLocalDb();
        var scenario = await fixture.CreateScenarioAsync();
        await scenario.CustomerService.ScheduleAsync(scenario.Customer, scenario.Schedule(10, 11), default);
        var first = (await scenario.AppointmentsAsync()).Single();
        Assert.Equal(AppointmentResultStatus.Success,
            (await scenario.ProviderService.ConfirmAsync(scenario.Provider, scenario.Decision(first), default)).Status);

        var confirmed = (await scenario.AppointmentsAsync()).Single();
        Assert.Equal(AppointmentStatus.Confirmed, confirmed.Status);
        Assert.Equal(scenario.ProviderId, confirmed.DecisionByUserId);
        Assert.NotNull(confirmed.DecisionAtUtc);

        var secondScenario = await fixture.CreateScenarioAsync();
        await secondScenario.ProviderService.ScheduleAsync(secondScenario.Provider,
            secondScenario.Schedule(12, 13), default);
        var proposal = (await secondScenario.AppointmentsAsync()).Single();
        Assert.Equal(AppointmentResultStatus.Success,
            (await secondScenario.CustomerService.RejectAsync(secondScenario.Customer,
                secondScenario.Decision(proposal), default)).Status);
        var rejected = (await secondScenario.AppointmentsAsync()).Single();
        Assert.Equal(AppointmentStatus.Rejected, rejected.Status);
        Assert.Equal(secondScenario.CustomerId, rejected.DecisionByUserId);
        Assert.NotNull(rejected.ClosedAtUtc);
    }

    [SqlServerFact]
    public async Task ProposerMayWithdraw_AndOtherParticipantMayCancelConfirmed()
    {
        fixture.RequireLocalDb();
        var scenario = await fixture.CreateScenarioAsync();
        await scenario.CustomerService.ScheduleAsync(scenario.Customer, scenario.Schedule(10, 11), default);
        var proposal = (await scenario.AppointmentsAsync()).Single();
        Assert.Equal(AppointmentResultStatus.Success,
            (await scenario.CustomerService.CancelAsync(scenario.Customer, scenario.Cancel(proposal), default)).Status);
        Assert.Equal(AppointmentStatus.Cancelled, (await scenario.AppointmentsAsync()).Single().Status);

        var confirmedScenario = await fixture.CreateScenarioAsync();
        await confirmedScenario.CustomerService.ScheduleAsync(confirmedScenario.Customer,
            confirmedScenario.Schedule(12, 13), default);
        var pending = (await confirmedScenario.AppointmentsAsync()).Single();
        await confirmedScenario.ProviderService.ConfirmAsync(confirmedScenario.Provider,
            confirmedScenario.Decision(pending), default);
        var confirmed = (await confirmedScenario.AppointmentsAsync()).Single();
        Assert.Equal(AppointmentResultStatus.Success,
            (await confirmedScenario.CustomerService.CancelAsync(confirmedScenario.Customer,
                confirmedScenario.Cancel(confirmed), default)).Status);
        Assert.Equal(AppointmentStatus.Cancelled, (await confirmedScenario.AppointmentsAsync()).Single().Status);

        var released = await fixture.CreateScenarioAsync(
            existingProviderId: confirmedScenario.ProviderProfileId,
            providerUserId: confirmedScenario.ProviderId);
        await released.CustomerService.ScheduleAsync(released.Customer, released.Schedule(12, 13), default);
        var releasedProposal = (await released.AppointmentsAsync()).Single();
        Assert.Equal(AppointmentResultStatus.Success,
            (await released.ProviderService.ConfirmAsync(released.Provider,
                released.Decision(releasedProposal), default)).Status);
    }

    [SqlServerFact]
    public async Task ConfirmationRevalidatesBlackoutAndWorkingHours()
    {
        fixture.RequireLocalDb();
        var blackoutScenario = await fixture.CreateScenarioAsync();
        await blackoutScenario.CustomerService.ScheduleAsync(blackoutScenario.Customer,
            blackoutScenario.Schedule(10, 11), default);
        var proposal = (await blackoutScenario.AppointmentsAsync()).Single();
        await blackoutScenario.AddBlackoutAsync(proposal.StartUtc, proposal.EndUtc);
        var blackoutResult = await blackoutScenario.ProviderService.ConfirmAsync(
            blackoutScenario.Provider, blackoutScenario.Decision(proposal), default);
        Assert.Contains(blackoutResult.Errors, e => e.Code == "BlackoutConflict");
        Assert.Equal(AppointmentStatus.Proposed, (await blackoutScenario.AppointmentsAsync()).Single().Status);

        var hoursScenario = await fixture.CreateScenarioAsync();
        await hoursScenario.CustomerService.ScheduleAsync(hoursScenario.Customer,
            hoursScenario.Schedule(12, 13), default);
        var hoursProposal = (await hoursScenario.AppointmentsAsync()).Single();
        await hoursScenario.ReplaceWorkingHoursAsync(new TimeOnly(14, 0), new TimeOnly(18, 0));
        var hoursResult = await hoursScenario.ProviderService.ConfirmAsync(
            hoursScenario.Provider, hoursScenario.Decision(hoursProposal), default);
        Assert.Contains(hoursResult.Errors, e => e.Code == "OutsideWorkingHours");

        var pastScenario = await fixture.CreateScenarioAsync();
        await pastScenario.CustomerService.ScheduleAsync(pastScenario.Customer,
            pastScenario.Schedule(16, 17), default);
        var futureProposal = (await pastScenario.AppointmentsAsync()).Single();
        await pastScenario.MoveAppointmentAsync(futureProposal.Id,
            new DateTimeOffset(2029, 1, 7, 16, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2029, 1, 7, 17, 0, 0, TimeSpan.Zero));
        var pastProposal = (await pastScenario.AppointmentsAsync()).Single();
        var pastResult = await pastScenario.ProviderService.ConfirmAsync(
            pastScenario.Provider, pastScenario.Decision(pastProposal), default);
        Assert.Contains(pastResult.Errors, e => e.Code == "StartMustBeFuture");
    }

    [SqlServerFact]
    public async Task ReschedulePreservesConfirmedUntilReplacementIsAccepted()
    {
        fixture.RequireLocalDb();
        var scenario = await fixture.CreateScenarioAsync();
        var original = await scenario.CreateConfirmedAsync(10, 11);
        var proposed = await scenario.CustomerService.RescheduleAsync(scenario.Customer,
            scenario.Reschedule(original, 12, 13), default);
        Assert.Equal(AppointmentResultStatus.Success, proposed.Status);

        var beforeDecision = await scenario.AppointmentsAsync();
        Assert.Contains(beforeDecision, a => a.Id == original.Id && a.Status == AppointmentStatus.Confirmed);
        var replacement = beforeDecision.Single(a => a.Status == AppointmentStatus.Proposed);

        await scenario.ProviderService.RejectAsync(scenario.Provider, scenario.Decision(replacement), default);
        var afterRejection = await scenario.AppointmentsAsync();
        Assert.Contains(afterRejection, a => a.Id == original.Id && a.Status == AppointmentStatus.Confirmed);
        Assert.Contains(afterRejection, a => a.Id == replacement.Id && a.Status == AppointmentStatus.Rejected);

        var freshOriginal = afterRejection.Single(a => a.Id == original.Id);
        await scenario.CustomerService.RescheduleAsync(scenario.Customer,
            scenario.Reschedule(freshOriginal, 14, 15), default);
        var acceptedProposal = (await scenario.AppointmentsAsync()).Single(a => a.Status == AppointmentStatus.Proposed);
        Assert.Equal(AppointmentResultStatus.Success,
            (await scenario.ProviderService.ConfirmAsync(scenario.Provider,
                scenario.Decision(acceptedProposal), default)).Status);
        var final = await scenario.AppointmentsAsync();
        Assert.Contains(final, a => a.Id == original.Id && a.Status == AppointmentStatus.Superseded);
        Assert.Contains(final, a => a.Id == acceptedProposal.Id && a.Status == AppointmentStatus.Confirmed);

        var conflictScenario = await fixture.CreateScenarioAsync();
        var conflictOriginal = await conflictScenario.CreateConfirmedAsync(10, 11);
        await conflictScenario.CustomerService.RescheduleAsync(conflictScenario.Customer,
            conflictScenario.Reschedule(conflictOriginal, 12, 13), default);
        var conflictingReplacement = (await conflictScenario.AppointmentsAsync())
            .Single(a => a.Status == AppointmentStatus.Proposed);
        await conflictScenario.AddBlackoutAsync(conflictingReplacement.StartUtc, conflictingReplacement.EndUtc);
        var conflictResult = await conflictScenario.ProviderService.ConfirmAsync(
            conflictScenario.Provider, conflictScenario.Decision(conflictingReplacement), default);
        Assert.Contains(conflictResult.Errors, e => e.Code == "BlackoutConflict");
        var preserved = await conflictScenario.AppointmentsAsync();
        Assert.Contains(preserved, a => a.Id == conflictOriginal.Id && a.Status == AppointmentStatus.Confirmed);
        Assert.Contains(preserved, a => a.Id == conflictingReplacement.Id && a.Status == AppointmentStatus.Proposed);
    }

    [SqlServerFact]
    public async Task MigratedDirectRescheduleChainCanBeConfirmedWithoutInventingPriorConfirmation()
    {
        fixture.RequireLocalDb();
        var scenario = await fixture.CreateScenarioAsync();
        var proposal = await scenario.SeedMigratedDirectRescheduleChainAsync();

        var result = await scenario.ProviderService.ConfirmAsync(
            scenario.Provider, scenario.Decision(proposal), default);

        Assert.Equal(AppointmentResultStatus.Success, result.Status);
        var appointments = await scenario.AppointmentsAsync();
        Assert.Equal(2, appointments.Count(a => a.Status == AppointmentStatus.Superseded));
        Assert.Contains(appointments, a => a.Id == proposal.Id && a.Status == AppointmentStatus.Confirmed);
        Assert.Equal(3, appointments.Count);
    }

    [SqlServerFact]
    public async Task ConfirmationMigrationPreservesLegacyDirectRescheduleChainAsUnconfirmedHistory()
    {
        fixture.RequireLocalDb();
        var migrated = await fixture.VerifyLegacyDirectRescheduleMigrationAsync();

        Assert.Equal(AppointmentStatus.Superseded, migrated[0].Status);
        Assert.Equal(AppointmentStatus.Superseded, migrated[1].Status);
        Assert.Equal(AppointmentStatus.Proposed, migrated[2].Status);
        Assert.Equal(migrated[0].Id, migrated[1].ReplacesAppointmentId);
        Assert.Equal(migrated[1].Id, migrated[2].ReplacesAppointmentId);
        Assert.DoesNotContain(migrated, a => a.Status == AppointmentStatus.Confirmed);
    }

    [SqlServerFact]
    public async Task OverlapIsEnforcedOnlyAtConfirmation_AndAdjacencyIsAllowed()
    {
        fixture.RequireLocalDb();
        var first = await fixture.CreateScenarioAsync();
        var occupied = await first.CreateConfirmedAsync(10, 11);

        var second = await fixture.CreateScenarioAsync(existingProviderId: first.ProviderProfileId,
            providerUserId: first.ProviderId);
        await second.CustomerService.ScheduleAsync(second.Customer, second.Schedule(10, 11), default);
        var overlap = (await second.AppointmentsAsync()).Single();
        var conflict = await second.ProviderService.ConfirmAsync(second.Provider, second.Decision(overlap), default);
        Assert.Contains(conflict.Errors, e => e.Code == "ProviderTimeConflict");

        var adjacent = await fixture.CreateScenarioAsync(existingProviderId: first.ProviderProfileId,
            providerUserId: first.ProviderId);
        await adjacent.CustomerService.ScheduleAsync(adjacent.Customer, adjacent.Schedule(11, 12), default);
        var adjacentProposal = (await adjacent.AppointmentsAsync()).Single();
        Assert.Equal(AppointmentResultStatus.Success,
            (await adjacent.ProviderService.ConfirmAsync(adjacent.Provider,
                adjacent.Decision(adjacentProposal), default)).Status);
        Assert.Equal(AppointmentStatus.Confirmed, occupied.Status);
    }

    [SqlServerFact]
    public async Task ConcurrentOverlappingConfirmationsHaveAtMostOneWinner()
    {
        fixture.RequireLocalDb();
        var first = await fixture.CreateScenarioAsync();
        var second = await fixture.CreateScenarioAsync(existingProviderId: first.ProviderProfileId,
            providerUserId: first.ProviderId);
        await first.CustomerService.ScheduleAsync(first.Customer, first.Schedule(10, 11), default);
        await second.CustomerService.ScheduleAsync(second.Customer, second.Schedule(10, 11), default);
        var firstProposal = (await first.AppointmentsAsync()).Single();
        var secondProposal = (await second.AppointmentsAsync()).Single();

        var results = await Task.WhenAll(
            first.ProviderService.ConfirmAsync(first.Provider, first.Decision(firstProposal), default),
            second.ProviderService.ConfirmAsync(second.Provider, second.Decision(secondProposal), default));

        Assert.Equal(1, results.Count(r => r.Status == AppointmentResultStatus.Success));
        Assert.Equal(1, await fixture.CountConfirmedAsync(first.ProviderProfileId));
    }

    [SqlServerFact]
    public async Task ReadModelSeparatesConfirmedProposalAndHistory()
    {
        fixture.RequireLocalDb();
        var scenario = await fixture.CreateScenarioAsync();
        var confirmed = await scenario.CreateConfirmedAsync(10, 11);
        await scenario.CustomerService.RescheduleAsync(scenario.Customer,
            scenario.Reschedule(confirmed, 12, 13), default);

        var panel = await scenario.ProviderService.GetForRequestAsync(
            scenario.Provider, scenario.RequestId, default);
        Assert.NotNull(panel);
        Assert.Equal(AppointmentStatus.Confirmed, panel!.ConfirmedAppointment!.Status);
        Assert.Single(panel.PendingProposals);
        Assert.True(panel.PendingProposals[0].CanConfirm);
        Assert.False(panel.PendingProposals[0].CreatedByCurrentUser);
    }

    [SqlServerFact]
    public async Task CustomerCalendarPreviewShowsOtherBookingOnlyAsBusyTime()
    {
        fixture.RequireLocalDb();
        await using var first = await fixture.CreateScenarioAsync();
        await first.CreateConfirmedAsync(10, 11);
        await using var second = await fixture.CreateScenarioAsync(
            existingProviderId: first.ProviderProfileId, providerUserId: first.ProviderId);

        var panel = await second.CustomerService.GetForRequestAsync(second.Customer, second.RequestId, default);
        var monday = Assert.Single(panel!.CalendarPreview!.Days, day => day.Date == new DateOnly(2030, 1, 7));
        Assert.False(Assert.Single(monday.Slots, slot => slot.StartLocal.Hour == 10).IsAvailable);
        Assert.True(Assert.Single(monday.Slots, slot => slot.StartLocal.Hour == 11).IsAvailable);

        var previewJson = JsonSerializer.Serialize(panel.CalendarPreview);
        Assert.DoesNotContain(first.CustomerId, previewJson, StringComparison.Ordinal);
        Assert.DoesNotContain("MaintenanceRequest", previewJson, StringComparison.Ordinal);
        Assert.Equal(["StartLocal", "EndLocal", "IsAvailable"],
            typeof(CustomerCalendarSlot).GetProperties().Select(property => property.Name));
        var providerPanel = await second.ProviderService.GetForRequestAsync(second.Provider, second.RequestId, default);
        Assert.Null(providerPanel!.CalendarPreview);
        Assert.Null(await second.CustomerService.GetForRequestAsync(second.Unrelated, second.RequestId, default));
    }

    [SqlServerFact]
    public async Task RequestStartAndCompletionSynchronizeConfirmedAppointment()
    {
        fixture.RequireLocalDb();
        var scenario = await fixture.CreateScenarioAsync();
        var confirmed = await scenario.CreateConfirmedAsync(10, 11);

        Assert.Equal(MutationResult.Success, await scenario.Workflow.TransitionAsync(
            scenario.Provider, scenario.RequestId, MaintenanceRequestStatus.Accepted,
            MaintenanceRequestStatus.InProgress, default));
        Assert.Equal(AppointmentStatus.InProgress,
            (await scenario.AppointmentsAsync()).Single(a => a.Id == confirmed.Id).Status);

        await scenario.AddAcceptedFinalPriceAsync();
        Assert.Equal(MutationResult.Success, await scenario.Workflow.TransitionAsync(
            scenario.Provider, scenario.RequestId, MaintenanceRequestStatus.InProgress,
            MaintenanceRequestStatus.Completed, default));
        var completed = (await scenario.AppointmentsAsync()).Single(a => a.Id == confirmed.Id);
        Assert.Equal(AppointmentStatus.Completed, completed.Status);
        Assert.NotNull(completed.ClosedAtUtc);
        Assert.Equal(scenario.ProviderId, completed.ClosedByUserId);
    }

    [SqlServerFact]
    public async Task PendingProposalCannotActAsConfirmation_ButLegacyNoAppointmentStillStarts()
    {
        fixture.RequireLocalDb();
        var proposed = await fixture.CreateScenarioAsync();
        await proposed.CustomerService.ScheduleAsync(proposed.Customer, proposed.Schedule(10, 11), default);
        Assert.Equal(MutationResult.Conflict, await proposed.Workflow.TransitionAsync(
            proposed.Provider, proposed.RequestId, MaintenanceRequestStatus.Accepted,
            MaintenanceRequestStatus.InProgress, default));

        var legacy = await fixture.CreateScenarioAsync();
        Assert.Equal(MutationResult.Success, await legacy.Workflow.TransitionAsync(
            legacy.Provider, legacy.RequestId, MaintenanceRequestStatus.Accepted,
            MaintenanceRequestStatus.InProgress, default));
        Assert.Empty(await legacy.AppointmentsAsync());
    }

    [SqlServerFact]
    public async Task PendingReplacementBlocksStartAndPreservesConfirmedAppointmentAndRequest()
    {
        fixture.RequireLocalDb();
        var scenario = await fixture.CreateScenarioAsync();
        var confirmed = await scenario.CreateConfirmedAsync(10, 11);
        await scenario.CustomerService.RescheduleAsync(scenario.Customer,
            scenario.Reschedule(confirmed, 12, 13), default);

        Assert.Equal(MutationResult.Conflict, await scenario.Workflow.TransitionAsync(
            scenario.Provider, scenario.RequestId, MaintenanceRequestStatus.Accepted,
            MaintenanceRequestStatus.InProgress, default));

        var appointments = await scenario.AppointmentsAsync();
        Assert.Contains(appointments, a => a.Id == confirmed.Id && a.Status == AppointmentStatus.Confirmed);
        Assert.Contains(appointments, a => a.Status == AppointmentStatus.Proposed
            && a.ReplacesAppointmentId == confirmed.Id);
        Assert.Equal(MaintenanceRequestStatus.Accepted, await scenario.RequestStatusAsync());
    }

}

public sealed class AppointmentControllerContractTests
{
    [Fact]
    public void WritesAreAuthorizedAndRateLimited()
    {
        Assert.NotNull(typeof(AppointmentsController).GetCustomAttributes(typeof(AuthorizeAttribute), true).SingleOrDefault());
        foreach (var name in new[] { "Schedule", "Reschedule", "Confirm", "Reject", "Cancel" })
        {
            var method = typeof(AppointmentsController).GetMethod(name)!;
            Assert.NotNull(method.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true).SingleOrDefault());
        }
    }
}

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("FIXPAL_RUN_SQL_TESTS"), "1", StringComparison.Ordinal))
            Skip = "Set FIXPAL_RUN_SQL_TESTS=1 to run against a verified isolated SQL Server database.";
    }
}

public sealed class SqlSchedulingFixture : IAsyncLifetime
{
    private readonly string _databaseName = $"FixPal_Phase3_Tests_{Guid.NewGuid():N}";
    private ServiceProvider _root = null!;
    private DateTimeOffset _now = new(2030, 1, 1, 8, 0, 0, TimeSpan.Zero);
    private string? _unavailableReason;
    public string ConnectionString => $"Server=(localdb)\\mssqllocaldb;Database={_databaseName};Trusted_Connection=True;MultipleActiveResultSets=true";

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(() => _now));
        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(ConnectionString));
        services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        services.AddScoped<AccountPhoneService>();
        services.AddScoped<RequestCommunicationPolicy>();
        services.AddScoped<RequestDetailsService>();
        services.AddScoped<RequestEvidencePolicy>();
        services.AddScoped<RequestAccessService>();
        services.AddScoped<RequestAgreementPolicy>();
        services.AddScoped<RequestMutationService>();
        services.AddScoped<SchedulingTimePolicy>();
        services.AddScoped<AppointmentService>();
        services.AddScoped<RequestWorkflowService>();
        _root = services.BuildServiceProvider();
        try
        {
            await using var scope = _root.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            foreach (var role in AppRoles.All) await roles.CreateAsync(new IdentityRole(role));
        }
        catch (Microsoft.Data.SqlClient.SqlException ex)
        {
            _unavailableReason = $"Isolated LocalDB unavailable: {ex.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (_unavailableReason != null)
        {
            await _root.DisposeAsync();
            return;
        }
        await using var scope = _root.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureDeletedAsync();
        await _root.DisposeAsync();
    }

    public void RequireLocalDb()
    {
        if (_unavailableReason != null) throw SkipException.ForSkip(_unavailableReason);
    }

    public async Task<TestScenario> CreateScenarioAsync(bool withAgreement = true,
        int? existingProviderId = null, string? providerUserId = null)
    {
        var scope = _root.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<ApplicationDbContext>();
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var token = Guid.NewGuid().ToString("N");
        var customerId = $"customer-{token}";
        var providerId = providerUserId ?? $"provider-{token}";
        var unrelatedId = $"other-{token}";

        await CreateUserAsync(users, customerId, AppRoles.Customer);
        if (providerUserId == null) await CreateUserAsync(users, providerId, AppRoles.Provider);
        await CreateUserAsync(users, unrelatedId, AppRoles.Customer);

        int providerProfileId;
        int categoryId;
        int areaId;
        if (existingProviderId == null)
        {
            var category = new ServiceCategory { Name = $"Category {token}" };
            var city = new City { Name = $"City {token}", IsActive = true };
            var area = new Area { Name = $"Area {token}", City = city };
            db.AddRange(category, city, area);
            await db.SaveChangesAsync();
            var profile = new ProviderProfile
            {
                UserId = providerId, DisplayName = "Provider", ProviderType = ProviderType.Individual,
                ApprovalStatus = ApprovalStatus.Approved, ServiceCategoryId = category.Id, AreaId = area.Id
            };
            db.ProviderProfiles.Add(profile);
            await db.SaveChangesAsync();
            providerProfileId = profile.Id;
            categoryId = category.Id;
            areaId = area.Id;
            db.ProviderCalendars.Add(new ProviderCalendar
            {
                ProviderProfileId = profile.Id, TimeZoneId = TimeZoneInfo.Utc.Id,
                IsEnabled = true, UpdatedAtUtc = _now
            });
            db.ProviderWorkingPeriods.Add(new ProviderWorkingPeriod
            {
                ProviderProfileId = profile.Id, DayOfWeek = DayOfWeek.Monday,
                StartLocal = new TimeOnly(0, 0), EndLocal = new TimeOnly(23, 59)
            });
            await db.SaveChangesAsync();
        }
        else
        {
            providerProfileId = existingProviderId.Value;
            var profile = await db.ProviderProfiles.AsNoTracking().SingleAsync(p => p.Id == providerProfileId);
            categoryId = profile.ServiceCategoryId;
            areaId = profile.AreaId;
        }

        var request = new MaintenanceRequest
        {
            CustomerId = customerId, ProviderProfileId = providerProfileId,
            ServiceCategoryId = categoryId, AreaId = areaId, Title = "Repair",
            Description = "Repair request", RequestType = RequestType.PrivateService,
            Status = MaintenanceRequestStatus.Accepted, CreatedAtUtc = _now.UtcDateTime,
            AcceptedAtUtc = _now.UtcDateTime
        };
        db.MaintenanceRequests.Add(request);
        await db.SaveChangesAsync();
        if (withAgreement)
        {
            var quote = new RequestQuote
            {
                MaintenanceRequestId = request.Id, ProviderProfileId = providerProfileId,
                State = QuoteState.Draft,
                MinimumPrice = 100, MaximumPrice = 120, SubmittedAtUtc = _now.UtcDateTime,
            };
            db.RequestQuotes.Add(quote);
            await db.SaveChangesAsync();
            db.QuoteRevisions.Add(new QuoteRevision
            {
                RequestQuoteId = quote.Id, Number = 1, ProviderAuthorId = providerId,
                MinimumPrice = 100, MaximumPrice = 120, CreatedAtUtc = _now.UtcDateTime
            });
            await db.SaveChangesAsync();
            quote.State = QuoteState.Accepted;
            quote.CurrentRevisionNumber = 1;
            quote.AcceptedRevisionNumber = 1;
            quote.AcceptedAtUtc = _now.UtcDateTime;
            await db.SaveChangesAsync();
            db.QuoteDecisions.Add(new QuoteDecision
            {
                RequestQuoteId = quote.Id, RevisionNumber = 1, CustomerAuthorId = customerId,
                State = QuoteState.Accepted, CreatedAtUtc = _now.UtcDateTime
            });
            await db.SaveChangesAsync();
        }

        return new TestScenario(scope, db, services.GetRequiredService<AppointmentService>(),
            services.GetRequiredService<RequestWorkflowService>(),
            request.Id, providerProfileId, customerId, providerId, unrelatedId, _now);
    }

    public async Task<int> CountConfirmedAsync(int providerProfileId)
    {
        await using var scope = _root.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Appointments
            .CountAsync(a => a.ProviderProfileId == providerProfileId && a.Status == AppointmentStatus.Confirmed);
    }

    public async Task<List<Appointment>> VerifyLegacyDirectRescheduleMigrationAsync()
    {
        var databaseName = $"FixPal_Phase3_LegacyMigration_{Guid.NewGuid():N}";
        var connectionString = $"Server=(localdb)\\mssqllocaldb;Database={databaseName};Trusted_Connection=True;MultipleActiveResultSets=true";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connectionString).Options;
        try
        {
            await using (var oldDb = new ApplicationDbContext(options))
            {
                await oldDb.GetService<IMigrator>().MigrateAsync("20260917150633_AddPhase3Scheduling");
                var token = Guid.NewGuid().ToString("N");
                var customerId = $"legacy-customer-{token}";
                var providerUserId = $"legacy-provider-{token}";
                oldDb.Users.AddRange(
                    new ApplicationUser { Id = customerId, UserName = customerId, NormalizedUserName = customerId.ToUpperInvariant() },
                    new ApplicationUser { Id = providerUserId, UserName = providerUserId, NormalizedUserName = providerUserId.ToUpperInvariant() });
                var category = new ServiceCategory { Name = $"Legacy category {token}" };
                var city = new City { Name = $"Legacy city {token}", IsActive = true };
                var area = new Area { Name = $"Legacy area {token}", City = city };
                oldDb.AddRange(category, city, area);
                await oldDb.SaveChangesAsync();
                var profile = new ProviderProfile
                {
                    UserId = providerUserId, DisplayName = "Legacy provider", ProviderType = ProviderType.Individual,
                    ApprovalStatus = ApprovalStatus.Approved, ServiceCategoryId = category.Id, AreaId = area.Id
                };
                oldDb.ProviderProfiles.Add(profile);
                await oldDb.SaveChangesAsync();
                var request = new MaintenanceRequest
                {
                    CustomerId = customerId, ProviderProfileId = profile.Id, ServiceCategoryId = category.Id,
                    AreaId = area.Id, Title = "Legacy request", Description = "Legacy direct reschedule",
                    RequestType = RequestType.PrivateService, Status = MaintenanceRequestStatus.Accepted,
                    CreatedAtUtc = _now.UtcDateTime, AcceptedAtUtc = _now.UtcDateTime
                };
                oldDb.MaintenanceRequests.Add(request);
                oldDb.ProviderCalendars.Add(new ProviderCalendar
                {
                    ProviderProfileId = profile.Id, TimeZoneId = TimeZoneInfo.Utc.Id,
                    IsEnabled = true, UpdatedAtUtc = _now
                });
                await oldDb.SaveChangesAsync();

                var firstCreated = _now.AddMinutes(1);
                var firstReplaced = _now.AddMinutes(2);
                var secondReplaced = _now.AddMinutes(3);
                await oldDb.Database.ExecuteSqlInterpolatedAsync($@"
SET IDENTITY_INSERT [Appointments] ON;
INSERT INTO [Appointments] ([Id], [MaintenanceRequestId], [ProviderProfileId], [StartUtc], [EndUtc], [TimeZoneId], [Status], [CreatedAtUtc], [CreatedByUserId], [ClosedAtUtc], [ClosedByUserId], [ReplacesAppointmentId])
VALUES
(1001, {request.Id}, {profile.Id}, {_now.AddDays(6).AddHours(2)}, {_now.AddDays(6).AddHours(3)}, {TimeZoneInfo.Utc.Id}, 5, {firstCreated}, {customerId}, {firstReplaced}, {customerId}, NULL),
(1002, {request.Id}, {profile.Id}, {_now.AddDays(6).AddHours(3)}, {_now.AddDays(6).AddHours(4)}, {TimeZoneInfo.Utc.Id}, 5, {firstReplaced}, {customerId}, {secondReplaced}, {customerId}, 1001),
(1003, {request.Id}, {profile.Id}, {_now.AddDays(6).AddHours(4)}, {_now.AddDays(6).AddHours(5)}, {TimeZoneInfo.Utc.Id}, 1, {secondReplaced}, {customerId}, NULL, NULL, 1002);
SET IDENTITY_INSERT [Appointments] OFF;");
                await oldDb.Database.MigrateAsync();
            }

            await using var migratedDb = new ApplicationDbContext(options);
            return await migratedDb.Appointments.AsNoTracking().OrderBy(a => a.Id).ToListAsync();
        }
        finally
        {
            await using var cleanup = new ApplicationDbContext(options);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private static async Task CreateUserAsync(UserManager<ApplicationUser> users, string id, string role)
    {
        var user = new ApplicationUser { Id = id, UserName = $"{id}@test.local", Email = $"{id}@test.local" };
        Assert.True((await users.CreateAsync(user)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
    }

    private sealed class FixedTimeProvider(Func<DateTimeOffset> now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now();
    }
}

public sealed class TestScenario(
    AsyncServiceScope scope,
    ApplicationDbContext db,
    AppointmentService service,
    RequestWorkflowService workflow,
    int requestId,
    int providerProfileId,
    string customerId,
    string providerId,
    string unrelatedId,
    DateTimeOffset now) : IAsyncDisposable
{
    public ApplicationDbContext Db => db;
    public IServiceProvider Services => scope.ServiceProvider;
    public int RequestId => requestId;
    public int ProviderProfileId => providerProfileId;
    public string CustomerId => customerId;
    public string ProviderId => providerId;
    public AppointmentService CustomerService => service;
    public AppointmentService ProviderService => service;
    public RequestWorkflowService Workflow => workflow;
    public ClaimsPrincipal Customer => Principal(customerId, AppRoles.Customer);
    public ClaimsPrincipal Provider => Principal(providerId, AppRoles.Provider);
    public ClaimsPrincipal Unrelated => Principal(unrelatedId, AppRoles.Customer);

    public ScheduleAppointmentCommand Schedule(int startHour, int endHour) =>
        new(requestId, Local(startHour), Local(endHour));
    public RescheduleAppointmentCommand Reschedule(Appointment current, int startHour, int endHour) =>
        new(requestId, current.Id, Local(startHour), Local(endHour), Convert.ToBase64String(current.RowVersion));
    public AppointmentDecisionCommand Decision(Appointment appointment) =>
        new(requestId, appointment.Id, Convert.ToBase64String(appointment.RowVersion));
    public CancelAppointmentCommand Cancel(Appointment appointment) =>
        new(requestId, appointment.Id, Convert.ToBase64String(appointment.RowVersion));

    public async Task<List<Appointment>> AppointmentsAsync()
    {
        db.ChangeTracker.Clear();
        return await db.Appointments.AsNoTracking().Where(a => a.MaintenanceRequestId == requestId)
            .OrderBy(a => a.Id).ToListAsync();
    }

    public async Task<Appointment> CreateConfirmedAsync(int startHour, int endHour)
    {
        await service.ScheduleAsync(Customer, Schedule(startHour, endHour), default);
        var proposal = (await AppointmentsAsync()).Single(a => a.Status == AppointmentStatus.Proposed);
        await service.ConfirmAsync(Provider, Decision(proposal), default);
        return (await AppointmentsAsync()).Single(a => a.Status == AppointmentStatus.Confirmed);
    }

    public async Task AddBlackoutAsync(DateTimeOffset start, DateTimeOffset end)
    {
        db.ProviderBlackouts.Add(new ProviderBlackout
        {
            ProviderProfileId = providerProfileId, StartUtc = start, EndUtc = end,
            CreatedAtUtc = now, CreatedByUserId = providerId
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    public async Task ReplaceWorkingHoursAsync(TimeOnly start, TimeOnly end)
    {
        await db.ProviderWorkingPeriods.Where(p => p.ProviderProfileId == providerProfileId).ExecuteDeleteAsync();
        db.ProviderWorkingPeriods.Add(new ProviderWorkingPeriod
        {
            ProviderProfileId = providerProfileId, DayOfWeek = DayOfWeek.Monday,
            StartLocal = start, EndLocal = end
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    public async Task MoveAppointmentAsync(int appointmentId, DateTimeOffset start, DateTimeOffset end)
    {
        await db.Appointments.Where(a => a.Id == appointmentId).ExecuteUpdateAsync(s => s
            .SetProperty(a => a.StartUtc, start)
            .SetProperty(a => a.EndUtc, end));
        db.ChangeTracker.Clear();
    }

    public async Task AddAcceptedFinalPriceAsync()
    {
        await db.RequestQuotes.Where(q => q.MaintenanceRequestId == requestId).ExecuteUpdateAsync(s => s
            .SetProperty(q => q.FinalPrice, 110m)
            .SetProperty(q => q.FinalPriceAcceptedAtUtc, now.UtcDateTime));
        db.ChangeTracker.Clear();
    }

    public async Task<Appointment> SeedMigratedDirectRescheduleChainAsync()
    {
        var firstClosed = now.AddMinutes(1);
        var secondClosed = now.AddMinutes(2);
        var first = new Appointment
        {
            MaintenanceRequestId = requestId, ProviderProfileId = providerProfileId,
            StartUtc = now.AddDays(6).AddHours(2), EndUtc = now.AddDays(6).AddHours(3),
            TimeZoneId = TimeZoneInfo.Utc.Id, Status = AppointmentStatus.Superseded,
            CreatedAtUtc = now, CreatedByUserId = customerId,
            ClosedAtUtc = firstClosed, ClosedByUserId = customerId
        };
        db.Appointments.Add(first);
        await db.SaveChangesAsync();
        var second = new Appointment
        {
            MaintenanceRequestId = requestId, ProviderProfileId = providerProfileId,
            StartUtc = now.AddDays(6).AddHours(3), EndUtc = now.AddDays(6).AddHours(4),
            TimeZoneId = TimeZoneInfo.Utc.Id, Status = AppointmentStatus.Superseded,
            CreatedAtUtc = firstClosed, CreatedByUserId = customerId,
            ClosedAtUtc = secondClosed, ClosedByUserId = customerId,
            ReplacesAppointmentId = first.Id
        };
        db.Appointments.Add(second);
        await db.SaveChangesAsync();
        var proposal = new Appointment
        {
            MaintenanceRequestId = requestId, ProviderProfileId = providerProfileId,
            StartUtc = now.AddDays(6).AddHours(4), EndUtc = now.AddDays(6).AddHours(5),
            TimeZoneId = TimeZoneInfo.Utc.Id, Status = AppointmentStatus.Proposed,
            CreatedAtUtc = secondClosed, CreatedByUserId = customerId,
            ReplacesAppointmentId = second.Id
        };
        db.Appointments.Add(proposal);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return (await AppointmentsAsync()).Single(a => a.Id == proposal.Id);
    }

    public async Task<MaintenanceRequestStatus> RequestStatusAsync()
    {
        db.ChangeTracker.Clear();
        return await db.MaintenanceRequests.Where(r => r.Id == requestId).Select(r => r.Status).SingleAsync();
    }

    public ValueTask DisposeAsync() => scope.DisposeAsync();

    private static DateTime Local(int hour) => new(2030, 1, 7, hour, 0, 0, DateTimeKind.Unspecified);
    private static ClaimsPrincipal Principal(string id, string role) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, id), new Claim(ClaimTypes.Name, id), new Claim(ClaimTypes.Role, role)],
        "Test"));
}
