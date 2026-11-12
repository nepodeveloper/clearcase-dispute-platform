using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClearCase.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RestrictTransactionCurrencyToZar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_ZarCurrency",
                table: "Transactions",
                sql: "\"Currency\" = 'ZAR'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Transactions_ZarCurrency",
                table: "Transactions");
        }
    }
}
