using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClearCase.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CustomerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    MerchantName = table.Column<string>(type: "TEXT", nullable: true),
                    AmountMinorUnits = table.Column<long>(type: "INTEGER", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", nullable: false),
                    TransactionDate = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Reference = table.Column<string>(type: "TEXT", nullable: true),
                    IsDisputable = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDebit = table.Column<bool>(type: "INTEGER", nullable: false),
                    HasRequiredDisputeInformation = table.Column<bool>(type: "INTEGER", nullable: false),
                    CollectionDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    MandateType = table.Column<string>(type: "TEXT", nullable: true),
                    MandateAuthorised = table.Column<bool>(type: "INTEGER", nullable: true),
                    MandateMatchesCollection = table.Column<bool>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_CustomerId_TransactionDate_Id",
                table: "Transactions",
                columns: new[] { "CustomerId", "TransactionDate", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_CustomerId_Type_TransactionDate_Id",
                table: "Transactions",
                columns: new[] { "CustomerId", "Type", "TransactionDate", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Transactions");
        }
    }
}
