using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECommerce.Worker.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkerEmailEventFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "email_delivery_projections",
                schema: "worker",
                columns: table => new
                {
                    DeliveryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_delivery_projections", x => x.DeliveryId);
                });

            migrationBuilder.CreateTable(
                name: "integration_outbox_messages",
                schema: "worker",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LockedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_outbox_messages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_email_delivery_projections_SentAt",
                schema: "worker",
                table: "email_delivery_projections",
                column: "SentAt");

            migrationBuilder.CreateIndex(
                name: "IX_integration_outbox_messages_ProcessedAt_NextAttemptAt_Locke~",
                schema: "worker",
                table: "integration_outbox_messages",
                columns: new[] { "ProcessedAt", "NextAttemptAt", "LockedUntil" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_delivery_projections",
                schema: "worker");

            migrationBuilder.DropTable(
                name: "integration_outbox_messages",
                schema: "worker");
        }
    }
}
