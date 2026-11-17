using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClearCase.Infrastructure.Migrations
{
    public partial class AddOutboxDeliveryLeases : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(name: "LeaseId", table: "OutboxMessages",
                type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<long>(name: "LeaseUntilAt", table: "OutboxMessages",
                type: "INTEGER", nullable: true);
            migrationBuilder.AddColumn<long>(name: "NextAttemptAt", table: "OutboxMessages",
                type: "INTEGER", nullable: true);
            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_PublishedAt_NextAttemptAt_LeaseUntilAt_OccurredAt",
                table: "OutboxMessages",
                columns: new[] { "PublishedAt", "NextAttemptAt", "LeaseUntilAt", "OccurredAt" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_PublishedAt_NextAttemptAt_LeaseUntilAt_OccurredAt",
                table: "OutboxMessages");
            migrationBuilder.DropColumn(name: "LeaseId", table: "OutboxMessages");
            migrationBuilder.DropColumn(name: "LeaseUntilAt", table: "OutboxMessages");
            migrationBuilder.DropColumn(name: "NextAttemptAt", table: "OutboxMessages");
        }
    }
}
