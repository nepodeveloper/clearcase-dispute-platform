using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClearCase.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDebitOrderOperationReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReversalOperationReference",
                table: "Disputes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Disputes_ReversalOperationReference",
                table: "Disputes",
                column: "ReversalOperationReference",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Disputes_ReversalOperationReference",
                table: "Disputes");

            migrationBuilder.DropColumn(
                name: "ReversalOperationReference",
                table: "Disputes");
        }
    }
}
