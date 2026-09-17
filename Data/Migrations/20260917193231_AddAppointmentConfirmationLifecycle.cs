using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FixPal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentConfirmationLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Appointments_Replacement",
                table: "Appointments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Appointments_Closure",
                table: "Appointments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Appointments_Status",
                table: "Appointments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Appointments_Utc",
                table: "Appointments");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DecisionAtUtc",
                table: "Appointments",
                type: "datetimeoffset(7)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DecisionByUserId",
                table: "Appointments",
                type: "nvarchar(450)",
                nullable: true);

            // Status 1 previously meant a unilateral scheduled appointment. It cannot
            // truthfully become mutually confirmed without an explicit decision.
            migrationBuilder.Sql("UPDATE [Appointments] SET [Status] = 6 WHERE [Status] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_DecisionByUserId",
                table: "Appointments",
                column: "DecisionByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_Appointments_Replacement",
                table: "Appointments",
                column: "ReplacesAppointmentId",
                unique: true,
                filter: "[ReplacesAppointmentId] IS NOT NULL AND [Status] IN (1, 2, 6)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Appointments_Closure",
                table: "Appointments",
                sql: "([Status] IN (1, 2, 6) AND [ClosedAtUtc] IS NULL AND [ClosedByUserId] IS NULL) OR ([Status] IN (3, 4, 5, 7) AND [ClosedAtUtc] IS NOT NULL AND [ClosedByUserId] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Appointments_Decision",
                table: "Appointments",
                sql: "([DecisionAtUtc] IS NULL AND [DecisionByUserId] IS NULL AND [Status] <> 1 AND [Status] <> 7) OR ([DecisionAtUtc] IS NOT NULL AND [DecisionByUserId] IS NOT NULL AND [Status] <> 6)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Appointments_Status",
                table: "Appointments",
                sql: "[Status] IN (1, 2, 3, 4, 5, 6, 7)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Appointments_Utc",
                table: "Appointments",
                sql: "DATEPART(TZOFFSET, [StartUtc]) = 0 AND DATEPART(TZOFFSET, [EndUtc]) = 0 AND DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND ([DecisionAtUtc] IS NULL OR DATEPART(TZOFFSET, [DecisionAtUtc]) = 0) AND ([ClosedAtUtc] IS NULL OR DATEPART(TZOFFSET, [ClosedAtUtc]) = 0)");

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_AspNetUsers_DecisionByUserId",
                table: "Appointments",
                column: "DecisionByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_AspNetUsers_DecisionByUserId",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_DecisionByUserId",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "UX_Appointments_Replacement",
                table: "Appointments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Appointments_Closure",
                table: "Appointments");

            // The old model has no proposal/rejection states. Preserve the rows as
            // closed history rather than manufacturing a confirmed appointment.
            migrationBuilder.Sql("UPDATE [Appointments] SET [Status] = 4, [ClosedAtUtc] = COALESCE([ClosedAtUtc], [CreatedAtUtc]), [ClosedByUserId] = COALESCE([ClosedByUserId], [CreatedByUserId]) WHERE [Status] IN (6, 7)");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Appointments_Decision",
                table: "Appointments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Appointments_Status",
                table: "Appointments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Appointments_Utc",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "DecisionAtUtc",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "DecisionByUserId",
                table: "Appointments");

            migrationBuilder.CreateIndex(
                name: "UX_Appointments_Replacement",
                table: "Appointments",
                column: "ReplacesAppointmentId",
                unique: true,
                filter: "[ReplacesAppointmentId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Appointments_Closure",
                table: "Appointments",
                sql: "([Status] IN (1, 2) AND [ClosedAtUtc] IS NULL AND [ClosedByUserId] IS NULL) OR ([Status] IN (3, 4, 5) AND [ClosedAtUtc] IS NOT NULL AND [ClosedByUserId] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Appointments_Status",
                table: "Appointments",
                sql: "[Status] IN (1, 2, 3, 4, 5)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Appointments_Utc",
                table: "Appointments",
                sql: "DATEPART(TZOFFSET, [StartUtc]) = 0 AND DATEPART(TZOFFSET, [EndUtc]) = 0 AND DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND ([ClosedAtUtc] IS NULL OR DATEPART(TZOFFSET, [ClosedAtUtc]) = 0)");
        }
    }
}
