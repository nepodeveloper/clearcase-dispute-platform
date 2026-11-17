using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClearCase.Infrastructure.Migrations
{
    public partial class AddOutboxParkingAndRecoveryAudit : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long?>(
                name: "ParkedAt", table: "OutboxMessages", type: "INTEGER", nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "ParkReasonCode", table: "OutboxMessages", type: "TEXT",
                maxLength: 64, nullable: true);
            migrationBuilder.CreateTable(
                name: "OutboxRecoveryAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    OccurredAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxRecoveryAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutboxRecoveryAudits_OutboxMessages_EventId",
                        column: x => x.EventId,
                        principalTable: "OutboxMessages",
                        principalColumn: "EventId",
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_ParkedAt_OccurredAt",
                table: "OutboxMessages", columns: new[] { "ParkedAt", "OccurredAt" });
            migrationBuilder.CreateIndex(name: "IX_OutboxRecoveryAudits_EventId_OccurredAt",
                table: "OutboxRecoveryAudits", columns: new[] { "EventId", "OccurredAt" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "OutboxRecoveryAudits");
            migrationBuilder.DropIndex(name: "IX_OutboxMessages_ParkedAt_OccurredAt",
                table: "OutboxMessages");
            migrationBuilder.DropColumn(name: "ParkedAt", table: "OutboxMessages");
            migrationBuilder.DropColumn(name: "ParkReasonCode", table: "OutboxMessages");
        }
    }
}
