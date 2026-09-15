using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FixPal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCityAndMaintenanceRequestWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CityId",
                table: "Areas",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Cities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cities", x => x.Id);
                });

            // All pre-existing areas belong to the original Jerusalem demo.
            // Preserve their IDs and relationships before making CityId required.
            migrationBuilder.Sql("""
                INSERT INTO Cities (Name, IsActive) VALUES (N'القدس', 1);
                UPDATE Areas SET CityId = (SELECT Id FROM Cities WHERE Name = N'القدس')
                WHERE CityId IS NULL;
                IF EXISTS (SELECT 1 FROM Areas GROUP BY CityId, Name HAVING COUNT(*) > 1)
                    THROW 51000, 'Duplicate area names exist within the same city; resolve them before applying this migration.', 1;
                """);
            migrationBuilder.AlterColumn<int>(
                name: "CityId", table: "Areas", type: "int", nullable: false,
                oldClrType: typeof(int), oldType: "int", oldNullable: true);

            migrationBuilder.CreateTable(
                name: "MaintenanceRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderProfileId = table.Column<int>(type: "int", nullable: true),
                    ServiceCategoryId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    RequestType = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceRequests", x => x.Id);
                    table.CheckConstraint("CK_MaintenanceRequests_RequestType", "[RequestType] IN (1, 2)");
                    table.CheckConstraint("CK_MaintenanceRequests_Status", "[Status] BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_MaintenanceRequests_Areas_AreaId",
                        column: x => x.AreaId,
                        principalTable: "Areas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaintenanceRequests_AspNetUsers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaintenanceRequests_ProviderProfiles_ProviderProfileId",
                        column: x => x.ProviderProfileId,
                        principalTable: "ProviderProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaintenanceRequests_ServiceCategories_ServiceCategoryId",
                        column: x => x.ServiceCategoryId,
                        principalTable: "ServiceCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderProfiles_ApprovalStatus_ServiceCategoryId_AreaId",
                table: "ProviderProfiles",
                columns: new[] { "ApprovalStatus", "ServiceCategoryId", "AreaId" });

            migrationBuilder.CreateIndex(
                name: "IX_Areas_CityId_Name",
                table: "Areas",
                columns: new[] { "CityId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cities_Name",
                table: "Cities",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRequests_AreaId",
                table: "MaintenanceRequests",
                column: "AreaId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRequests_CustomerId_CreatedAtUtc_Id",
                table: "MaintenanceRequests",
                columns: new[] { "CustomerId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRequests_ProviderProfileId_Status_CreatedAtUtc_Id",
                table: "MaintenanceRequests",
                columns: new[] { "ProviderProfileId", "Status", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRequests_ServiceCategoryId",
                table: "MaintenanceRequests",
                column: "ServiceCategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_Areas_Cities_CityId",
                table: "Areas",
                column: "CityId",
                principalTable: "Cities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Areas_Cities_CityId",
                table: "Areas");

            migrationBuilder.DropTable(
                name: "Cities");

            migrationBuilder.DropTable(
                name: "MaintenanceRequests");

            migrationBuilder.DropIndex(
                name: "IX_ProviderProfiles_ApprovalStatus_ServiceCategoryId_AreaId",
                table: "ProviderProfiles");

            migrationBuilder.DropIndex(
                name: "IX_Areas_CityId_Name",
                table: "Areas");

            migrationBuilder.DropColumn(
                name: "CityId",
                table: "Areas");
        }
    }
}
