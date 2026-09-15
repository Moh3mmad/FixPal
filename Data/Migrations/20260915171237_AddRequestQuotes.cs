using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FixPal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRequestQuotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RequestQuotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaintenanceRequestId = table.Column<int>(type: "int", nullable: false),
                    ProviderProfileId = table.Column<int>(type: "int", nullable: false),
                    MinimumPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MaximumPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestQuotes", x => x.Id);
                    table.CheckConstraint("CK_RequestQuotes_Prices", "[MinimumPrice] > 0 AND [MaximumPrice] >= [MinimumPrice] AND [MaximumPrice] <= 1000000");
                    table.ForeignKey(
                        name: "FK_RequestQuotes_MaintenanceRequests_MaintenanceRequestId",
                        column: x => x.MaintenanceRequestId,
                        principalTable: "MaintenanceRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RequestQuotes_ProviderProfiles_ProviderProfileId",
                        column: x => x.ProviderProfileId,
                        principalTable: "ProviderProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RequestQuotes_MaintenanceRequestId",
                table: "RequestQuotes",
                column: "MaintenanceRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RequestQuotes_ProviderProfileId",
                table: "RequestQuotes",
                column: "ProviderProfileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RequestQuotes");
        }
    }
}
