using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClearCase.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInvestigationNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InvestigationNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DisputeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Text = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CorrelationId = table.Column<string>(type: "TEXT", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestigationNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvestigationNotes_Disputes_DisputeId",
                        column: x => x.DisputeId,
                        principalTable: "Disputes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvestigationNotes_DisputeId_CreatedAt_Id",
                table: "InvestigationNotes",
                columns: new[] { "DisputeId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_InvestigationNotes_DisputeId_EmployeeId_IdempotencyKey",
                table: "InvestigationNotes",
                columns: new[] { "DisputeId", "EmployeeId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_InvestigationNotes_PreventUpdate" BEFORE UPDATE ON "InvestigationNotes"
                BEGIN SELECT RAISE(ABORT, 'Investigation notes are append-only'); END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_InvestigationNotes_PreventDelete" BEFORE DELETE ON "InvestigationNotes"
                BEGIN SELECT RAISE(ABORT, 'Investigation notes are append-only'); END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS \"TR_InvestigationNotes_PreventDelete\";");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS \"TR_InvestigationNotes_PreventUpdate\";");
            migrationBuilder.DropTable(
                name: "InvestigationNotes");
        }
    }
}
