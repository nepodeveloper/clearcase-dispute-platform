using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClearCase.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBankChargeTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BankChargeAdjustmentReference",
                table: "Disputes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Disputes_BankChargeAdjustmentReference",
                table: "Disputes",
                column: "BankChargeAdjustmentReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Disputes_CustomerId_CreatedAt_Reference",
                table: "Disputes",
                columns: new[] { "CustomerId", "CreatedAt", "Reference" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Disputes_BankChargeAdjustmentReference",
                table: "Disputes");

            migrationBuilder.DropIndex(
                name: "IX_Disputes_CustomerId_CreatedAt_Reference",
                table: "Disputes");

            migrationBuilder.DropColumn(
                name: "BankChargeAdjustmentReference",
                table: "Disputes");
        }
    }
}
