using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClearCase.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUxJourneyCompletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "UpdatedAt",
                table: "FinancialOperationIntents",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
            migrationBuilder.Sql("UPDATE \"FinancialOperationIntents\" SET \"UpdatedAt\" = \"CreatedAt\";");

            migrationBuilder.CreateTable(
                name: "CustomerEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DisputeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Content = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    UploadedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerEvidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerEvidence_Disputes_DisputeId",
                        column: x => x.DisputeId,
                        principalTable: "Disputes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DisputeAssignmentAuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DisputeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    PreviousEmployeeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CorrelationId = table.Column<string>(type: "TEXT", nullable: false),
                    OccurredAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DisputeAssignmentAuditEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DisputeAssignmentAuditEvents_Disputes_DisputeId",
                        column: x => x.DisputeId,
                        principalTable: "Disputes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DisputeAssignments",
                columns: table => new
                {
                    DisputeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssignedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DisputeAssignments", x => x.DisputeId);
                    table.ForeignKey(
                        name: "FK_DisputeAssignments_Disputes_DisputeId",
                        column: x => x.DisputeId,
                        principalTable: "Disputes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DisputeWithdrawals",
                columns: table => new
                {
                    DisputeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    WithdrawnAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DisputeWithdrawals", x => x.DisputeId);
                    table.ForeignKey(
                        name: "FK_DisputeWithdrawals_Disputes_DisputeId",
                        column: x => x.DisputeId,
                        principalTable: "Disputes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationReadMarkers",
                columns: table => new
                {
                    CustomerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReadThrough = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationReadMarkers", x => x.CustomerId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerEvidence_DisputeId_UploadedAt_Id",
                table: "CustomerEvidence",
                columns: new[] { "DisputeId", "UploadedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_DisputeAssignmentAuditEvents_DisputeId_OccurredAt",
                table: "DisputeAssignmentAuditEvents",
                columns: new[] { "DisputeId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DisputeAssignments_EmployeeId",
                table: "DisputeAssignments",
                column: "EmployeeId");

            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_DisputeAssignmentAuditEvents_PreventUpdate"
                BEFORE UPDATE ON "DisputeAssignmentAuditEvents"
                BEGIN SELECT RAISE(ABORT, 'Dispute assignment audit events are append-only'); END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_DisputeAssignmentAuditEvents_PreventDelete"
                BEFORE DELETE ON "DisputeAssignmentAuditEvents"
                BEGIN SELECT RAISE(ABORT, 'Dispute assignment audit events are append-only'); END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS \"TR_DisputeAssignmentAuditEvents_PreventDelete\";");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS \"TR_DisputeAssignmentAuditEvents_PreventUpdate\";");
            migrationBuilder.DropTable(
                name: "CustomerEvidence");

            migrationBuilder.DropTable(
                name: "DisputeAssignmentAuditEvents");

            migrationBuilder.DropTable(
                name: "DisputeAssignments");

            migrationBuilder.DropTable(
                name: "DisputeWithdrawals");

            migrationBuilder.DropTable(
                name: "NotificationReadMarkers");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "FinancialOperationIntents");
        }
    }
}
