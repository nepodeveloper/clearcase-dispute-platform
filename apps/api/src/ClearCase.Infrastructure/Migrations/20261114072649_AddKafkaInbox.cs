using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClearCase.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKafkaInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConsumerEffects",
                columns: table => new
                {
                    ConsumerName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AggregateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EffectType = table.Column<string>(type: "TEXT", nullable: false),
                    ProcessedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsumerEffects", x => new { x.ConsumerName, x.EventId });
                });

            migrationBuilder.CreateTable(
                name: "ConsumerInbox",
                columns: table => new
                {
                    ConsumerName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AggregateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AggregateVersion = table.Column<long>(type: "INTEGER", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", nullable: false),
                    ProcessedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsumerInbox", x => new { x.ConsumerName, x.EventId });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConsumerEffects");

            migrationBuilder.DropTable(
                name: "ConsumerInbox");
        }
    }
}
