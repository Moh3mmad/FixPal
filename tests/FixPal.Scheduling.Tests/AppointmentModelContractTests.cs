using System.Reflection;
using FixPal.Data;
using FixPal.Data.Migrations;
using FixPal.Models;
using FixPal.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace FixPal.Scheduling.Tests;

public sealed class AppointmentModelContractTests
{
    private static readonly IModel Model = CreateModel();

    [Fact]
    public void StatusValuesPreserveExistingOccupancyNumbers()
    {
        Assert.Equal(1, (int)AppointmentStatus.Confirmed);
        Assert.Equal(2, (int)AppointmentStatus.InProgress);
        Assert.Equal(6, (int)AppointmentStatus.Proposed);
        Assert.Equal(7, (int)AppointmentStatus.Rejected);
    }

    [Fact]
    public void OnlyConfirmedAndInProgressAreUniqueActiveRequestStates()
    {
        var index = Entity().GetIndexes().Single(i => i.GetDatabaseName() == "UX_Appointments_ActiveRequest");
        Assert.True(index.IsUnique);
        Assert.Equal("[Status] IN (1, 2)", index.GetFilter());
    }

    [Fact]
    public void ReplacementIndexAllowsNewProposalAfterTerminalHistory()
    {
        var index = Entity().GetIndexes().Single(i => i.GetDatabaseName() == "UX_Appointments_Replacement");
        Assert.True(index.IsUnique);
        Assert.Equal("[ReplacesAppointmentId] IS NOT NULL AND [Status] IN (1, 2, 6)", index.GetFilter());
    }

    [Fact]
    public void DecisionAuditIsNullableAndReferencesIdentityWithRestrictDelete()
    {
        var entity = Entity();
        Assert.True(entity.FindProperty(nameof(Appointment.DecisionAtUtc))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(Appointment.DecisionByUserId))!.IsNullable);
        var relationship = entity.GetForeignKeys().Single(f =>
            f.Properties.Single().Name == nameof(Appointment.DecisionByUserId));
        Assert.Equal(DeleteBehavior.Restrict, relationship.DeleteBehavior);
    }

    [Fact]
    public void CheckConstraintsCoverProposalDecisionClosureAndUtc()
    {
        var constraints = Entity().GetCheckConstraints().ToDictionary(c => c.Name!, c => c.Sql);
        Assert.Contains("[Status] IN (1, 2, 3, 4, 5, 6, 7)", constraints["CK_Appointments_Status"]);
        Assert.Contains("[Status] IN (1, 2, 6)", constraints["CK_Appointments_Closure"]);
        Assert.Contains("[Status] <> 1", constraints["CK_Appointments_Decision"]);
        Assert.Contains("[DecisionAtUtc]", constraints["CK_Appointments_Utc"]);
    }

    [Fact]
    public void FollowUpMigrationConvertsLegacyScheduledRowsToProposals()
    {
        var migration = new AddAppointmentConfirmationLifecycle();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        typeof(AddAppointmentConfirmationLifecycle)
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);
        var sql = builder.Operations.OfType<SqlOperation>().Select(o => o.Sql).ToArray();
        Assert.Contains(sql, statement => statement.Contains("SET [Status] = 6 WHERE [Status] = 1",
            StringComparison.Ordinal));
    }

    private static IEntityType Entity() => Model.FindEntityType(typeof(Appointment))!;

    private static IModel CreateModel()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=ModelOnly;Trusted_Connection=True")
            .Options;
        using var db = new ApplicationDbContext(options);
        return db.GetService<IDesignTimeModel>().Model;
    }
}
