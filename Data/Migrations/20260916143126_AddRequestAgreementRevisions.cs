using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FixPal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRequestAgreementRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AcceptedRevisionNumber",
                table: "RequestQuotes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CurrentRevisionNumber",
                table: "RequestQuotes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "RequestQuotes",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<int>(
                name: "State",
                table: "RequestQuotes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsLegacy",
                table: "MaintenanceRequests",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "QuoteRevisions",
                columns: table => new
                {
                    RequestQuoteId = table.Column<int>(type: "int", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    ProviderAuthorId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    MinimumPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MaximumPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteRevisions", x => new { x.RequestQuoteId, x.Number });
                    table.CheckConstraint("CK_QuoteRevisions_Terms", "[Number] > 0 AND [MinimumPrice] > 0 AND [MaximumPrice] >= [MinimumPrice] AND [MaximumPrice] <= 1000000");
                    table.ForeignKey(
                        name: "FK_QuoteRevisions_AspNetUsers_ProviderAuthorId",
                        column: x => x.ProviderAuthorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuoteRevisions_RequestQuotes_RequestQuoteId",
                        column: x => x.RequestQuoteId,
                        principalTable: "RequestQuotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QuoteDecisions",
                columns: table => new
                {
                    RequestQuoteId = table.Column<int>(type: "int", nullable: false),
                    RevisionNumber = table.Column<int>(type: "int", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    CustomerAuthorId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteDecisions", x => new { x.RequestQuoteId, x.RevisionNumber });
                    table.CheckConstraint("CK_QuoteDecisions_State", "[State] IN (2,3,4)");
                    table.ForeignKey(
                        name: "FK_QuoteDecisions_AspNetUsers_CustomerAuthorId",
                        column: x => x.CustomerAuthorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuoteDecisions_QuoteRevisions_RequestQuoteId_RevisionNumber",
                        columns: x => new { x.RequestQuoteId, x.RevisionNumber },
                        principalTable: "QuoteRevisions",
                        principalColumns: new[] { "RequestQuoteId", "Number" },
                        onDelete: ReferentialAction.Restrict);
                });

            // Preserve original rows/events. Only existing quote acceptance is backfilled.
            // Legacy self-dealing/completed-without-quote rows stay readable, never auto-agreed.
            migrationBuilder.Sql("""
                UPDATE MaintenanceRequests SET IsLegacy = 1;
                INSERT INTO QuoteRevisions (RequestQuoteId, Number, ProviderAuthorId, MinimumPrice, MaximumPrice, Note, CreatedAtUtc)
                SELECT q.Id, 1, p.UserId, q.MinimumPrice, q.MaximumPrice, q.Note, q.SubmittedAtUtc
                FROM RequestQuotes q JOIN ProviderProfiles p ON p.Id = q.ProviderProfileId;
                INSERT INTO QuoteDecisions (RequestQuoteId, RevisionNumber, State, CustomerAuthorId, CreatedAtUtc)
                SELECT q.Id, 1, 2, r.CustomerId, q.AcceptedAtUtc
                FROM RequestQuotes q JOIN MaintenanceRequests r ON r.Id = q.MaintenanceRequestId
                WHERE q.AcceptedAtUtc IS NOT NULL;
                UPDATE RequestQuotes SET CurrentRevisionNumber = 1,
                    AcceptedRevisionNumber = CASE WHEN AcceptedAtUtc IS NOT NULL THEN 1 ELSE NULL END,
                    State = CASE WHEN AcceptedAtUtc IS NOT NULL THEN 2 ELSE 1 END;
                """);
            migrationBuilder.CreateIndex(
                name: "IX_RequestQuotes_Id_AcceptedRevisionNumber",
                table: "RequestQuotes",
                columns: new[] { "Id", "AcceptedRevisionNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_RequestQuotes_Id_CurrentRevisionNumber",
                table: "RequestQuotes",
                columns: new[] { "Id", "CurrentRevisionNumber" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_RequestQuotes_Agreement",
                table: "RequestQuotes",
                sql: "([State] = 0 AND [CurrentRevisionNumber] IS NULL AND [AcceptedRevisionNumber] IS NULL) OR ([State] IN (1,3,4) AND [CurrentRevisionNumber] IS NOT NULL AND [AcceptedRevisionNumber] IS NULL) OR ([State] = 2 AND [AcceptedRevisionNumber] IS NOT NULL AND [CurrentRevisionNumber] = [AcceptedRevisionNumber] AND [AcceptedAtUtc] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteDecisions_CustomerAuthorId",
                table: "QuoteDecisions",
                column: "CustomerAuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteDecisions_RequestQuoteId",
                table: "QuoteDecisions",
                column: "RequestQuoteId",
                unique: true,
                filter: "[State] = 2");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteRevisions_ProviderAuthorId",
                table: "QuoteRevisions",
                column: "ProviderAuthorId");

            migrationBuilder.AddForeignKey(
                name: "FK_RequestQuotes_QuoteRevisions_Id_AcceptedRevisionNumber",
                table: "RequestQuotes",
                columns: new[] { "Id", "AcceptedRevisionNumber" },
                principalTable: "QuoteRevisions",
                principalColumns: new[] { "RequestQuoteId", "Number" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RequestQuotes_QuoteRevisions_Id_CurrentRevisionNumber",
                table: "RequestQuotes",
                columns: new[] { "Id", "CurrentRevisionNumber" },
                principalTable: "QuoteRevisions",
                principalColumns: new[] { "RequestQuoteId", "Number" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RequestQuotes_QuoteRevisions_Id_AcceptedRevisionNumber",
                table: "RequestQuotes");

            migrationBuilder.DropForeignKey(
                name: "FK_RequestQuotes_QuoteRevisions_Id_CurrentRevisionNumber",
                table: "RequestQuotes");

            migrationBuilder.DropTable(
                name: "QuoteDecisions");

            migrationBuilder.DropTable(
                name: "QuoteRevisions");

            migrationBuilder.DropIndex(
                name: "IX_RequestQuotes_Id_AcceptedRevisionNumber",
                table: "RequestQuotes");

            migrationBuilder.DropIndex(
                name: "IX_RequestQuotes_Id_CurrentRevisionNumber",
                table: "RequestQuotes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RequestQuotes_Agreement",
                table: "RequestQuotes");

            migrationBuilder.DropColumn(
                name: "AcceptedRevisionNumber",
                table: "RequestQuotes");

            migrationBuilder.DropColumn(
                name: "CurrentRevisionNumber",
                table: "RequestQuotes");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "RequestQuotes");

            migrationBuilder.DropColumn(
                name: "State",
                table: "RequestQuotes");

            migrationBuilder.DropColumn(
                name: "IsLegacy",
                table: "MaintenanceRequests");
        }
    }
}

