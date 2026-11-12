using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClearCase.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDisputes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Disputes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Reference = table.Column<string>(type: "TEXT", nullable: false),
                    CustomerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TransactionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    ReasonCode = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    CaseStatus = table.Column<string>(type: "TEXT", nullable: false),
                    DecisionStatus = table.Column<string>(type: "TEXT", nullable: false),
                    FinancialStatus = table.Column<string>(type: "TEXT", nullable: false),
                    EligibilityOutcome = table.Column<string>(type: "TEXT", nullable: false),
                    EligibilityPolicyVersion = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Disputes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Disputes_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DisputeEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DisputeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SequenceNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", nullable: false),
                    CaseStatus = table.Column<string>(type: "TEXT", nullable: false),
                    PublicDescription = table.Column<string>(type: "TEXT", nullable: false),
                    OccurredAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DisputeEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DisputeEvents_Disputes_DisputeId",
                        column: x => x.DisputeId,
                        principalTable: "Disputes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DisputeEvents_DisputeId_SequenceNumber",
                table: "DisputeEvents",
                columns: new[] { "DisputeId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Disputes_CustomerId_TransactionId_Open",
                table: "Disputes",
                columns: new[] { "CustomerId", "TransactionId" },
                unique: true,
                filter: "\"CaseStatus\" <> 'Closed'");

            migrationBuilder.CreateIndex(
                name: "IX_Disputes_Reference",
                table: "Disputes",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Disputes_TransactionId",
                table: "Disputes",
                column: "TransactionId");

            // The dispute timeline is append-only: reject in-place edits and deletions at the database.
            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_DisputeEvents_PreventUpdate" BEFORE UPDATE ON "DisputeEvents"
                BEGIN SELECT RAISE(ABORT, 'DisputeEvents are append-only.'); END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_DisputeEvents_PreventDelete" BEFORE DELETE ON "DisputeEvents"
                BEGIN SELECT RAISE(ABORT, 'DisputeEvents are append-only.'); END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS \"TR_DisputeEvents_PreventDelete\";");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS \"TR_DisputeEvents_PreventUpdate\";");

            migrationBuilder.DropTable(
                name: "DisputeEvents");

            migrationBuilder.DropTable(
                name: "Disputes");
        }
    }
}
