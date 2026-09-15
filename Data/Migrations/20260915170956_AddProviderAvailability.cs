using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FixPal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderAvailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Availability",
                table: "ProviderProfiles",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "AvailableAfterUtc",
                table: "ProviderProfiles",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Availability",
                table: "ProviderProfiles");

            migrationBuilder.DropColumn(
                name: "AvailableAfterUtc",
                table: "ProviderProfiles");
        }
    }
}
