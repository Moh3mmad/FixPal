using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FixPal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPhase3Scheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProviderCalendars",
                columns: table => new
                {
                    ProviderProfileId = table.Column<int>(type: "int", nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderCalendars", x => x.ProviderProfileId);
                    table.CheckConstraint("CK_ProviderCalendars_TimeZone", "LEN(LTRIM(RTRIM([TimeZoneId]))) > 0");
                    table.CheckConstraint("CK_ProviderCalendars_UpdatedAtUtc", "DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                    table.ForeignKey(
                        name: "FK_ProviderCalendars_ProviderProfiles_ProviderProfileId",
                        column: x => x.ProviderProfileId,
                        principalTable: "ProviderProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Appointments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaintenanceRequestId = table.Column<int>(type: "int", nullable: false),
                    ProviderProfileId = table.Column<int>(type: "int", nullable: false),
                    StartUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    EndUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ClosedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    ReplacesAppointmentId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Appointments", x => x.Id);
                    table.CheckConstraint("CK_Appointments_Closure", "([Status] IN (1, 2) AND [ClosedAtUtc] IS NULL AND [ClosedByUserId] IS NULL) OR ([Status] IN (3, 4, 5) AND [ClosedAtUtc] IS NOT NULL AND [ClosedByUserId] IS NOT NULL)");
                    table.CheckConstraint("CK_Appointments_Interval", "[StartUtc] < [EndUtc]");
                    table.CheckConstraint("CK_Appointments_Replacement", "[ReplacesAppointmentId] IS NULL OR [ReplacesAppointmentId] <> [Id]");
                    table.CheckConstraint("CK_Appointments_Status", "[Status] IN (1, 2, 3, 4, 5)");
                    table.CheckConstraint("CK_Appointments_TimeZone", "LEN(LTRIM(RTRIM([TimeZoneId]))) > 0");
                    table.CheckConstraint("CK_Appointments_Utc", "DATEPART(TZOFFSET, [StartUtc]) = 0 AND DATEPART(TZOFFSET, [EndUtc]) = 0 AND DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND ([ClosedAtUtc] IS NULL OR DATEPART(TZOFFSET, [ClosedAtUtc]) = 0)");
                    table.ForeignKey(
                        name: "FK_Appointments_Appointments_ReplacesAppointmentId",
                        column: x => x.ReplacesAppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Appointments_AspNetUsers_ClosedByUserId",
                        column: x => x.ClosedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Appointments_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Appointments_MaintenanceRequests_MaintenanceRequestId",
                        column: x => x.MaintenanceRequestId,
                        principalTable: "MaintenanceRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Appointments_ProviderCalendars_ProviderProfileId",
                        column: x => x.ProviderProfileId,
                        principalTable: "ProviderCalendars",
                        principalColumn: "ProviderProfileId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProviderBlackouts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProviderProfileId = table.Column<int>(type: "int", nullable: false),
                    StartUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    EndUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RemovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    RemovedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderBlackouts", x => x.Id);
                    table.CheckConstraint("CK_ProviderBlackouts_Interval", "[StartUtc] < [EndUtc]");
                    table.CheckConstraint("CK_ProviderBlackouts_Removal", "([RemovedAtUtc] IS NULL AND [RemovedByUserId] IS NULL) OR ([RemovedAtUtc] IS NOT NULL AND [RemovedByUserId] IS NOT NULL)");
                    table.CheckConstraint("CK_ProviderBlackouts_Utc", "DATEPART(TZOFFSET, [StartUtc]) = 0 AND DATEPART(TZOFFSET, [EndUtc]) = 0 AND DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND ([RemovedAtUtc] IS NULL OR DATEPART(TZOFFSET, [RemovedAtUtc]) = 0)");
                    table.ForeignKey(
                        name: "FK_ProviderBlackouts_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProviderBlackouts_AspNetUsers_RemovedByUserId",
                        column: x => x.RemovedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProviderBlackouts_ProviderCalendars_ProviderProfileId",
                        column: x => x.ProviderProfileId,
                        principalTable: "ProviderCalendars",
                        principalColumn: "ProviderProfileId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProviderWorkingPeriods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProviderProfileId = table.Column<int>(type: "int", nullable: false),
                    DayOfWeek = table.Column<int>(type: "int", nullable: false),
                    StartLocal = table.Column<TimeOnly>(type: "time(0)", nullable: false),
                    EndLocal = table.Column<TimeOnly>(type: "time(0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderWorkingPeriods", x => x.Id);
                    table.CheckConstraint("CK_ProviderWorkingPeriods_Day", "[DayOfWeek] BETWEEN 0 AND 6");
                    table.CheckConstraint("CK_ProviderWorkingPeriods_Interval", "[StartLocal] < [EndLocal]");
                    table.ForeignKey(
                        name: "FK_ProviderWorkingPeriods_ProviderCalendars_ProviderProfileId",
                        column: x => x.ProviderProfileId,
                        principalTable: "ProviderCalendars",
                        principalColumn: "ProviderProfileId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ClosedByUserId",
                table: "Appointments",
                column: "ClosedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_CreatedByUserId",
                table: "Appointments",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ProviderInterval",
                table: "Appointments",
                columns: new[] { "ProviderProfileId", "StartUtc" })
                .Annotation("SqlServer:Include", new[] { "EndUtc", "Status", "MaintenanceRequestId" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_RequestHistory",
                table: "Appointments",
                columns: new[] { "MaintenanceRequestId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_Appointments_ActiveRequest",
                table: "Appointments",
                column: "MaintenanceRequestId",
                unique: true,
                filter: "[Status] IN (1, 2)");

            migrationBuilder.CreateIndex(
                name: "UX_Appointments_Replacement",
                table: "Appointments",
                column: "ReplacesAppointmentId",
                unique: true,
                filter: "[ReplacesAppointmentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderBlackouts_ActiveInterval",
                table: "ProviderBlackouts",
                columns: new[] { "ProviderProfileId", "StartUtc" },
                filter: "[RemovedAtUtc] IS NULL")
                .Annotation("SqlServer:Include", new[] { "EndUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderBlackouts_CreatedByUserId",
                table: "ProviderBlackouts",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderBlackouts_RemovedByUserId",
                table: "ProviderBlackouts",
                column: "RemovedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_ProviderWorkingPeriods_Interval",
                table: "ProviderWorkingPeriods",
                columns: new[] { "ProviderProfileId", "DayOfWeek", "StartLocal", "EndLocal" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Appointments");

            migrationBuilder.DropTable(
                name: "ProviderBlackouts");

            migrationBuilder.DropTable(
                name: "ProviderWorkingPeriods");

            migrationBuilder.DropTable(
                name: "ProviderCalendars");
        }
    }
}
