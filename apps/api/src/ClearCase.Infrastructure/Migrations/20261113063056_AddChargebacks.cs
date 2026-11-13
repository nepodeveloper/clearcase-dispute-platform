using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClearCase.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChargebacks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Chargebacks",
                columns: table => new
                {
                    DisputeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Reference = table.Column<string>(type: "TEXT", nullable: false),
                    ReasonCode = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    LastOperationOutcome = table.Column<string>(type: "TEXT", nullable: false),
                    EvidenceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EvidenceKind = table.Column<string>(type: "TEXT", nullable: true),
                    EvidenceReceivedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Chargebacks", x => x.DisputeId);
                    table.CheckConstraint("CK_Chargebacks_EvidenceMetadata", "(\"EvidenceId\" IS NULL AND \"EvidenceKind\" IS NULL AND \"EvidenceReceivedAt\" IS NULL) OR (\"EvidenceId\" IS NOT NULL AND \"EvidenceKind\" IS NOT NULL AND \"EvidenceReceivedAt\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Chargebacks_Disputes_DisputeId",
                        column: x => x.DisputeId,
                        principalTable: "Disputes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChargebackAuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DisputeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OperationReference = table.Column<string>(type: "TEXT", nullable: false),
                    Command = table.Column<string>(type: "TEXT", nullable: false),
                    ChargebackStatus = table.Column<string>(type: "TEXT", nullable: false),
                    DecisionStatus = table.Column<string>(type: "TEXT", nullable: false),
                    FinancialStatus = table.Column<string>(type: "TEXT", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", nullable: false),
                    ActorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CorrelationId = table.Column<string>(type: "TEXT", nullable: false),
                    EvidenceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EvidenceKind = table.Column<string>(type: "TEXT", nullable: true),
                    OccurredAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChargebackAuditEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChargebackAuditEvents_Chargebacks_DisputeId",
                        column: x => x.DisputeId,
                        principalTable: "Chargebacks",
                        principalColumn: "DisputeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChargebackAuditEvents_DisputeId",
                table: "ChargebackAuditEvents",
                column: "DisputeId");

            migrationBuilder.CreateIndex(
                name: "IX_ChargebackAuditEvents_OperationReference",
                table: "ChargebackAuditEvents",
                column: "OperationReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Chargebacks_Reference",
                table: "Chargebacks",
                column: "Reference",
                unique: true);

            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_ChargebackAuditEvents_PreventUpdate" BEFORE UPDATE ON "ChargebackAuditEvents"
                BEGIN SELECT RAISE(ABORT, 'ChargebackAuditEvents are append-only.'); END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_ChargebackAuditEvents_PreventDelete" BEFORE DELETE ON "ChargebackAuditEvents"
                BEGIN SELECT RAISE(ABORT, 'ChargebackAuditEvents are append-only.'); END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS \"TR_ChargebackAuditEvents_PreventDelete\";");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS \"TR_ChargebackAuditEvents_PreventUpdate\";");
            migrationBuilder.DropTable(
                name: "ChargebackAuditEvents");

            migrationBuilder.DropTable(
                name: "Chargebacks");
        }
    }
}
