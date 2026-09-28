using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECommerce.Worker.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrationEventCorrelation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                schema: "worker",
                table: "notification_outbox_messages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                schema: "worker",
                table: "integration_outbox_messages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE worker.notification_outbox_messages
                SET "CorrelationId" = replace("SourceMessageId"::text, '-', '');

                UPDATE worker.integration_outbox_messages
                SET "CorrelationId" = replace("Id"::text, '-', '');
                """);

            migrationBuilder.AlterColumn<string>(
                name: "CorrelationId",
                schema: "worker",
                table: "notification_outbox_messages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CorrelationId",
                schema: "worker",
                table: "integration_outbox_messages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CorrelationId",
                schema: "worker",
                table: "notification_outbox_messages");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                schema: "worker",
                table: "integration_outbox_messages");
        }
    }
}
