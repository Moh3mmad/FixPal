using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FixPal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPrivateRequestEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RequestEvidence",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaintenanceRequestId = table.Column<int>(type: "int", nullable: false),
                    UploadedById = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StorageUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ContentType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestEvidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RequestEvidence_AspNetUsers_UploadedById",
                        column: x => x.UploadedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RequestEvidence_MaintenanceRequests_MaintenanceRequestId",
                        column: x => x.MaintenanceRequestId,
                        principalTable: "MaintenanceRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RequestEvidence_MaintenanceRequestId_CreatedAtUtc_Id",
                table: "RequestEvidence",
                columns: new[] { "MaintenanceRequestId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_RequestEvidence_UploadedById",
                table: "RequestEvidence",
                column: "UploadedById");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RequestEvidence");
        }
    }
}
