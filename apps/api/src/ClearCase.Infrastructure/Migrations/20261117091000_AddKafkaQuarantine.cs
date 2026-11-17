using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClearCase.Infrastructure.Migrations
{
    public partial class AddKafkaQuarantine : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KafkaQuarantinedRecords",
                columns: table => new
                {
                    ConsumerName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Topic = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Partition = table.Column<int>(type: "INTEGER", nullable: false),
                    Offset = table.Column<long>(type: "INTEGER", nullable: false),
                    FailureCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PayloadSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PayloadByteLength = table.Column<int>(type: "INTEGER", nullable: false),
                    QuarantinedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KafkaQuarantinedRecords",
                        x => new { x.ConsumerName, x.Topic, x.Partition, x.Offset });
                });
            migrationBuilder.CreateIndex(name: "IX_KafkaQuarantinedRecords_QuarantinedAt",
                table: "KafkaQuarantinedRecords", column: "QuarantinedAt");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "KafkaQuarantinedRecords");
        }
    }
}
