using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FixPal.Data.Migrations
{
     public partial class ImproveProviderRelationships : Migration
    {
         protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProviderProfiles_Areas_AreaId",
                table: "ProviderProfiles");

            migrationBuilder.DropForeignKey(
                name: "FK_ProviderProfiles_ServiceCategories_ServiceCategoryId",
                table: "ProviderProfiles");

            migrationBuilder.DropIndex(
                name: "IX_ProviderProfiles_UserId",
                table: "ProviderProfiles");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderProfiles_UserId",
                table: "ProviderProfiles",
                column: "UserId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ProviderProfiles_Areas_AreaId",
                table: "ProviderProfiles",
                column: "AreaId",
                principalTable: "Areas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProviderProfiles_ServiceCategories_ServiceCategoryId",
                table: "ProviderProfiles",
                column: "ServiceCategoryId",
                principalTable: "ServiceCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProviderProfiles_Areas_AreaId",
                table: "ProviderProfiles");

            migrationBuilder.DropForeignKey(
                name: "FK_ProviderProfiles_ServiceCategories_ServiceCategoryId",
                table: "ProviderProfiles");

            migrationBuilder.DropIndex(
                name: "IX_ProviderProfiles_UserId",
                table: "ProviderProfiles");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderProfiles_UserId",
                table: "ProviderProfiles",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProviderProfiles_Areas_AreaId",
                table: "ProviderProfiles",
                column: "AreaId",
                principalTable: "Areas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProviderProfiles_ServiceCategories_ServiceCategoryId",
                table: "ProviderProfiles",
                column: "ServiceCategoryId",
                principalTable: "ServiceCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
