using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClearCase.Infrastructure.Migrations
{
    public partial class AddFinancialOperationIntents : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FinancialOperationIntents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DisputeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CustomerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    OperationReference = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ExpectedVersion = table.Column<long>(type: "INTEGER", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Stage = table.Column<string>(type: "TEXT", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", nullable: true),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    LeaseId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LeaseUntilAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialOperationIntents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinancialOperationIntents_Disputes_DisputeId",
                        column: x => x.DisputeId,
                        principalTable: "Disputes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateIndex(name: "IX_FinancialOperationIntents_DisputeId",
                table: "FinancialOperationIntents", column: "DisputeId");
            migrationBuilder.CreateIndex(name: "IX_FinancialOperationIntents_OperationReference",
                table: "FinancialOperationIntents", column: "OperationReference", unique: true);
            migrationBuilder.CreateIndex(name: "IX_FinancialOperationIntents_Stage_LeaseUntilAt_CreatedAt",
                table: "FinancialOperationIntents",
                columns: new[] { "Stage", "LeaseUntilAt", "CreatedAt" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "FinancialOperationIntents");
        }
    }
}
