using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FixPal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPrivateRequestCoordinates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                table: "MaintenanceRequests",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                table: "MaintenanceRequests",
                type: "float",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_MaintenanceRequests_Coordinates",
                table: "MaintenanceRequests",
                sql: "([Latitude] IS NULL AND [Longitude] IS NULL) OR ([Latitude] IS NOT NULL AND [Longitude] IS NOT NULL AND [Latitude] BETWEEN -90 AND 90 AND [Longitude] BETWEEN -180 AND 180)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_MaintenanceRequests_Coordinates",
                table: "MaintenanceRequests");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "MaintenanceRequests");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "MaintenanceRequests");
        }
    }
}
