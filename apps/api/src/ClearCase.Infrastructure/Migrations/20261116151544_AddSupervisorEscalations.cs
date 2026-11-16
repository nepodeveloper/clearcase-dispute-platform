using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClearCase.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSupervisorEscalations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DisputeEscalations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DisputeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedByEmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DisputeEscalations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DisputeEscalations_Disputes_DisputeId",
                        column: x => x.DisputeId,
                        principalTable: "Disputes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DisputeEscalationAuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EscalationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SequenceNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PrivateNote = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    OccurredAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CorrelationId = table.Column<string>(type: "TEXT", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DisputeEscalationAuditEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DisputeEscalationAuditEvents_DisputeEscalations_EscalationId",
                        column: x => x.EscalationId,
                        principalTable: "DisputeEscalations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DisputeEscalationAuditEvents_EscalationId_EmployeeId_IdempotencyKey",
                table: "DisputeEscalationAuditEvents",
                columns: new[] { "EscalationId", "EmployeeId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DisputeEscalationAuditEvents_EscalationId_SequenceNumber",
                table: "DisputeEscalationAuditEvents",
                columns: new[] { "EscalationId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DisputeEscalations_CreatedAt_Reference",
                table: "DisputeEscalations",
                columns: new[] { "CreatedAt", "Reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DisputeEscalations_DisputeId_Active",
                table: "DisputeEscalations",
                columns: new[] { "DisputeId", "Status" },
                unique: true,
                filter: "\"Status\" <> 'Resolved'");

            migrationBuilder.CreateIndex(
                name: "IX_DisputeEscalations_DisputeId_EmployeeId_IdempotencyKey",
                table: "DisputeEscalations",
                columns: new[] { "DisputeId", "CreatedByEmployeeId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_DisputeEscalationAuditEvents_PreventUpdate"
                BEFORE UPDATE ON "DisputeEscalationAuditEvents"
                BEGIN SELECT RAISE(ABORT, 'Escalation audit events are append-only'); END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_DisputeEscalationAuditEvents_PreventDelete"
                BEFORE DELETE ON "DisputeEscalationAuditEvents"
                BEGIN SELECT RAISE(ABORT, 'Escalation audit events are append-only'); END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS \"TR_DisputeEscalationAuditEvents_PreventDelete\";");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS \"TR_DisputeEscalationAuditEvents_PreventUpdate\";");
            migrationBuilder.DropTable(
                name: "DisputeEscalationAuditEvents");

            migrationBuilder.DropTable(
                name: "DisputeEscalations");
        }
    }
}
