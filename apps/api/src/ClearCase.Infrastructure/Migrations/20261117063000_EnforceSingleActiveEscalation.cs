using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClearCase.Infrastructure.Migrations
{
    /// <summary>Enforce one non-resolved escalation per dispute, across all active statuses.</summary>
    public partial class EnforceSingleActiveEscalation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A legacy database with multiple active escalations per dispute fails
            // migration without deleting or rewriting escalation and audit records.
            migrationBuilder.DropIndex(
                name: "IX_DisputeEscalations_DisputeId_Active",
                table: "DisputeEscalations");
            migrationBuilder.CreateIndex(
                name: "IX_DisputeEscalations_DisputeId_Active",
                table: "DisputeEscalations",
                column: "DisputeId",
                unique: true,
                filter: "\"Status\" <> 'Resolved'");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DisputeEscalations_DisputeId_Active",
                table: "DisputeEscalations");
            migrationBuilder.CreateIndex(
                name: "IX_DisputeEscalations_DisputeId_Active",
                table: "DisputeEscalations",
                columns: new[] { "DisputeId", "Status" },
                unique: true,
                filter: "\"Status\" <> 'Resolved'");
        }
    }
}
