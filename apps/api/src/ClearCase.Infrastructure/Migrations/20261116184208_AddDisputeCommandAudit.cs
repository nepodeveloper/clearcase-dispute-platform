using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClearCase.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDisputeCommandAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DisputeCommandAuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DisputeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Command = table.Column<string>(type: "TEXT", nullable: false),
                    CorrelationId = table.Column<string>(type: "TEXT", nullable: false),
                    OccurredAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CaseStatus = table.Column<string>(type: "TEXT", nullable: false),
                    DecisionStatus = table.Column<string>(type: "TEXT", nullable: false),
                    FinancialStatus = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DisputeCommandAuditEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DisputeCommandAuditEvents_Disputes_DisputeId",
                        column: x => x.DisputeId,
                        principalTable: "Disputes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DisputeCommandAuditEvents_DisputeId_Version",
                table: "DisputeCommandAuditEvents",
                columns: new[] { "DisputeId", "Version" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_DisputeCommandAuditEvents_PreventUpdate"
                BEFORE UPDATE ON "DisputeCommandAuditEvents"
                BEGIN SELECT RAISE(ABORT, 'Dispute command audit events are append-only'); END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_DisputeCommandAuditEvents_PreventDelete"
                BEFORE DELETE ON "DisputeCommandAuditEvents"
                BEGIN SELECT RAISE(ABORT, 'Dispute command audit events are append-only'); END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS \"TR_DisputeCommandAuditEvents_PreventDelete\";");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS \"TR_DisputeCommandAuditEvents_PreventUpdate\";");
            migrationBuilder.DropTable(
                name: "DisputeCommandAuditEvents");
        }
    }
}
